using PeakDNS.DNS.Server;
using k8s;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using k8s.Models;

namespace PeakDNS.Kubernetes
{
    public class ProviderSettings
    {
        public DnsSettings Dns { get; set; }
        public SoaSettings Soa { get; set; }
        public LoadBalancingSettings LoadBalancing { get; set; }
        public HashSettings Hash { get; set; }
        public LoggingSettings Logging { get; set; }

        public class DnsSettings
        {
            public string Prefix { get; set; } = "peak.";
            public int UpdateInterval { get; set; } = 30;
            public int RecordTTL { get; set; } = 300;
        }

        public class SoaSettings
        {
            public string Serial { get; set; } = "2024032601";
            public int Refresh { get; set; } = 3600;
            public int Retry { get; set; } = 1800;
            public int Expire { get; set; } = 604800;
            public int TTL { get; set; } = 3600;
            public int MinimumTTL { get; set; } = 300;
            public string PrimaryNameserver { get; set; } = "ns1.peak.";
            public string Hostmaster { get; set; } = "admin.peak.";
        }

        public class LoadBalancingSettings
        {
            public double DefaultOverloadThreshold { get; set; } = 1.5;
            public string DefaultMode { get; set; } = "singlebest";
        }

        public class HashSettings
        {
            public int Length { get; set; } = 4;
        }

        public class LoggingSettings
        {
            public string Path { get; set; } = "./log.txt";
            public int LogLevel { get; set; } = 5;
        }
    }

    public class Provider
    {
        private readonly ProviderSettings _settings;
        private readonly k8s.Kubernetes _client;
        private static Logging<Provider> logger;
        private CancellationTokenSource _cancellationTokenSource;
        public BIND bind;
        private PrometheusClient _prometheusClient;
        private readonly ConcurrentDictionary<string, string> _clusterTypes = new();
        private readonly ConcurrentDictionary<string, Record> _currentRecords = new();
        private readonly ConcurrentDictionary<string, string> _reservedTopDomains = new();
        Settings _configSettings;



        public Provider(Settings configSettings)
        {
            _configSettings = configSettings;

            _settings = new ProviderSettings
            {
                Dns = new ProviderSettings.DnsSettings
                {
                    Prefix = configSettings.GetSetting("dns", "prefix", "peak."),
                    UpdateInterval = int.Parse(configSettings.GetSetting("dns", "updateInterval", "30")),
                    RecordTTL = int.Parse(configSettings.GetSetting("dns", "recordTTL", "300"))
                },
                Soa = new ProviderSettings.SoaSettings
                {
                    Serial = configSettings.GetSetting("soa", "serial", "2024032601"),
                    Refresh = int.Parse(configSettings.GetSetting("soa", "refresh", "3600")),
                    Retry = int.Parse(configSettings.GetSetting("soa", "retry", "1800")),
                    Expire = int.Parse(configSettings.GetSetting("soa", "expire", "604800")),
                    TTL = int.Parse(configSettings.GetSetting("soa", "ttl", "3600")),
                    MinimumTTL = int.Parse(configSettings.GetSetting("soa", "minimumTTL", "300")),
                    PrimaryNameserver = configSettings.GetSetting("soa", "primaryNameserver", "ns1.peak."),
                    Hostmaster = configSettings.GetSetting("soa", "hostmaster", "admin.peak.")
                },
                LoadBalancing = new ProviderSettings.LoadBalancingSettings
                {
                    DefaultOverloadThreshold = double.Parse(configSettings.GetSetting("loadbalancing", "defaultOverloadThreshold", "1.5")),
                    DefaultMode = configSettings.GetSetting("loadbalancing", "defaultMode", "singlebest")
                },
                Hash = new ProviderSettings.HashSettings
                {
                    Length = int.Parse(configSettings.GetSetting("hash", "length", "4"))
                },
                Logging = new ProviderSettings.LoggingSettings
                {
                    Path = configSettings.GetSetting("logging", "path", "./log.txt"),
                    LogLevel = int.Parse(configSettings.GetSetting("logging", "logLevel", "5"))
                }
            };

            bind = new BIND(configSettings, _settings.Dns.Prefix);

            bind.SetSOARecord(
                primaryNameserver: _settings.Soa.PrimaryNameserver,
                hostmaster: _settings.Soa.Hostmaster,
                serial: _settings.Soa.Serial,
                refresh: _settings.Soa.Refresh.ToString(),
                retry: _settings.Soa.Retry.ToString(),
                expire: _settings.Soa.Expire.ToString(),
                ttl: _settings.Soa.TTL,
                minimumTTL: _settings.Soa.MinimumTTL
            );

            logger = new Logging<Provider>(
                _settings.Logging.Path,
                _settings.Logging.LogLevel
            );

            var config = KubernetesClientConfiguration.InClusterConfig();
            _client = new k8s.Kubernetes(config);
            _prometheusClient = new PrometheusClient(configSettings);
        }


        private async Task<string> GetLoadBalanceExpression(string labelValue, string namespaceName)
        {
            try
            {
                const string PREFIX = "configmap.";
                if (!labelValue.StartsWith(PREFIX))
                {
                    return labelValue;
                }

                var parts = labelValue.Substring(PREFIX.Length).Split('.');
                if (parts.Length != 2)
                {
                    logger.Error($"Invalid configmap reference format: {labelValue}, expected format: configmap.name.key");
                    return string.Empty;
                }

                var configMapName = parts[0];
                var configMapKey = parts[1];

                logger.Debug($"Loading from ConfigMap - Name: '{configMapName}', Key: '{configMapKey}', Namespace: '{namespaceName}'");

                try
                {
                    var configMap = await _client.ReadNamespacedConfigMapAsync(
                        name: configMapName,
                        namespaceParameter: namespaceName);

                    if (configMap == null)
                    {
                        logger.Error($"ConfigMap '{configMapName}' not found in namespace '{namespaceName}'");
                        return string.Empty;
                    }

                    if (!configMap.Data.TryGetValue(configMapKey, out var expression))
                    {
                        logger.Error($"Key '{configMapKey}' not found in ConfigMap '{configMapName}'");
                        return string.Empty;
                    }

                    logger.Debug($"Successfully loaded expression from ConfigMap '{configMapName}/{configMapKey}'");
                    return expression;
                }
                catch (Exception ex)
                {
                    logger.Error($"Error accessing ConfigMap: {ex.Message}");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error processing ConfigMap expression: {ex.Message}\nStack trace: {ex.StackTrace}");
                return string.Empty;
            }
        }

        public void Start()
        {
            _prometheusClient.Initialize();
            _cancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => RunUpdateLoop(_cancellationTokenSource.Token));
        }


        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
        }

        private async Task RunUpdateLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Update();
                }
                catch (Exception ex)
                {
                    logger.Error($"Error in update loop: {ex}");
                }
                await Task.Delay(TimeSpan.FromSeconds(_settings.Dns.UpdateInterval), cancellationToken);
            }
        }



        private async Task Update()
        {
            try
            {
                logger.Debug("Starting DNS record update");
                await ClearExistingRecords();

                var newRecords = new ConcurrentDictionary<string, Record>();
                var namespaces = await _client.ListNamespaceAsync();

                foreach (var ns in namespaces.Items)
                {
                    await ProcessNamespace(ns, newRecords);
                }

                UpdateCurrentRecords(newRecords);
                logger.Debug($"Update completed. Total active records: {_currentRecords.Count}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error reading Kubernetes data: {ex.Message}");
                throw;
            }
        }

        private async Task ClearExistingRecords()
        {
            foreach (var record in _currentRecords)
            {
                bind.RemoveRecord(record.Value);
                logger.Debug($"Removed existing record: {record.Key}");
            }
            _currentRecords.Clear();
        }

        private async Task ProcessNamespace(V1Namespace ns, ConcurrentDictionary<string, Record> newRecords)
        {
            if (!ValidateNamespace(ns, out string? domain, out bool isTopLevelRequest))
                return;

            if (!await HandleTopLevelDomain(ns, domain, isTopLevelRequest))
                return;

            var pods = await _client.ListNamespacedPodAsync(ns.Metadata.Name);

            if (ns.Metadata.Labels.TryGetValue("dns.peak/loadbalance", out string? labelValue))
            {
                await HandleLoadBalancing(ns, pods, domain, labelValue, newRecords);
            }
            else
            {
                await HandleStandardRecords(pods, domain, isTopLevelRequest, newRecords);
            }
        }

        private bool ValidateNamespace(V1Namespace ns, out string? domain, out bool isTopLevelRequest)
        {
            domain = null;
            isTopLevelRequest = false;

            if (ns.Metadata?.Labels == null)
            {
                logger.Debug($"Namespace {ns.Metadata?.Name} has no labels, skipping");
                return false;
            }

            if (!ns.Metadata.Labels.TryGetValue("dns.peak/domain", out domain))
            {
                logger.Debug($"Namespace {ns.Metadata.Name} has no dns.peak/domain label, skipping");
                return false;
            }

            if (ns.Metadata.Labels.TryGetValue("dns.peak/only-top", out string? onlyTop))
            {
                isTopLevelRequest = bool.TryParse(onlyTop, out bool isOnlyTop) && isOnlyTop;
            }

            return true;
        }

        private async Task<bool> HandleTopLevelDomain(V1Namespace ns, string domain, bool isTopLevelRequest)
        {
            if (isTopLevelRequest)
            {
                if (_reservedTopDomains.TryGetValue(domain, out string? existingNs))
                {
                    if (existingNs != ns.Metadata.Name)
                    {
                        logger.Warning($"Namespace {ns.Metadata.Name} attempted to register reserved top domain {domain} (owned by {existingNs})");
                        return false;
                    }
                }
                else
                {
                    _reservedTopDomains.TryAdd(domain, ns.Metadata.Name);
                    logger.Info($"Reserved top domain {domain} for namespace {ns.Metadata.Name}");
                }
            }
            else if (_reservedTopDomains.ContainsKey(domain))
            {
                logger.Warning($"Namespace {ns.Metadata.Name} attempted to use reserved top domain {domain}");
                return false;
            }

            return true;
        }

        private async Task<List<(string podIP, double metric)>> GetPodMetrics(V1PodList pods, string prometheusQuery, string namespaceName)
        {
            var metrics = new List<(string podIP, double metric)>();

            foreach (var pod in pods.Items)
            {
                if (pod.Status?.PodIP == null || pod.Metadata?.Name == null)
                {
                    LogPodSkipReason(pod);
                    continue;
                }

                var query = prometheusQuery
                    .Replace("%pod-name%", pod.Metadata.Name)
                    .Replace("%namespace%", namespaceName);

                try
                {
                    var metric = await _prometheusClient.GetMetricValueAsync(query);
                    if (metric.HasValue)
                    {
                        metrics.Add((pod.Status.PodIP, metric.Value));
                    }
                }
                catch (Exception ex)
                {
                    logger.Warning($"Failed to get metric for pod {pod.Metadata.Name}: {ex.Message}");
                }
            }

            return metrics;
        }

        private async Task HandleLoadBalancing(V1Namespace ns, V1PodList pods, string domain, string labelValue, ConcurrentDictionary<string, Record> newRecords)
        {
            logger.Debug($"Starting load balancing for namespace {ns.Metadata.Name} with domain {domain}");

            var prometheusQuery = await GetLoadBalanceExpression(labelValue, ns.Metadata.Name);
            if (string.IsNullOrEmpty(prometheusQuery))
            {
                logger.Warning($"Empty Prometheus query for namespace {ns.Metadata.Name}");
                return;
            }

            var clusterType = ns.Metadata.Labels.TryGetValue("cluster-type", out string type) ? type : "default";
            logger.Debug($"Cluster type for domain {domain}: {clusterType}");

            if (!ValidateClusterType(domain, clusterType))
            {
                logger.Warning($"Invalid cluster type {clusterType} for domain {domain}");
                return;
            }

            var mode = (ns.Metadata.Annotations != null &&
                        ns.Metadata.Annotations.TryGetValue("dns.peak/loadbalance-mode", out var modeValue))
                ? modeValue.ToLowerInvariant()
                : _settings.LoadBalancing.DefaultMode;

            logger.Info($"Load balancing mode for {domain}: {mode}");

            if (mode == "excludeoverloaded")
            {
                var overloadThresholdStr = ns.Metadata.Annotations?.TryGetValue("dns.peak/overload-threshold", out var thresholdValue) == true
                    ? thresholdValue
                    : _settings.LoadBalancing.DefaultOverloadThreshold.ToString();

                var overloadThreshold = double.Parse(overloadThresholdStr);
                logger.Debug($"Overload threshold for {domain}: {overloadThreshold}");

                var metrics = await GetPodMetrics(pods, prometheusQuery, ns.Metadata.Name);
                if (!metrics.Any())
                {
                    logger.Warning($"No metrics retrieved for {domain}");
                    return;
                }

                var avgLoad = metrics.Average(m => m.metric);
                var threshold = avgLoad * overloadThreshold;
                logger.Info($"Average load: {avgLoad:F2}, Threshold: {threshold:F2}");

                var nonOverloaded = metrics.Where(m => m.metric <= threshold).ToList();
                logger.Info($"Found {nonOverloaded.Count} non-overloaded pods out of {metrics.Count} total");

                foreach (var pod in nonOverloaded)
                {
                    await ProcessRecord(newRecords, $"{domain}.", pod.podIP);
                    logger.Debug($"Added record for pod {pod.podIP} with load {pod.metric:F2}");
                }
            }
            else
            {
                logger.Debug($"Using single best pod selection for {domain}");
                var bestPodIp = await GetBestPodIp(pods, prometheusQuery, ns.Metadata.Name, clusterType);

                if (bestPodIp != null)
                {
                    await ProcessRecord(newRecords, $"{domain}.", bestPodIp);
                    logger.Info($"Selected best pod {bestPodIp} for domain {domain}");
                }
                else
                {
                    logger.Warning($"No suitable pod found for {domain}");
                }
            }
        }

        private bool ValidateClusterType(string domain, string clusterType)
        {
            if (!_clusterTypes.TryGetValue(domain, out string existingType))
            {
                _clusterTypes.TryAdd(domain, clusterType);
                return true;
            }

            if (existingType != clusterType)
            {
                logger.Warning($"Skipping load balancing for domain {domain} due to cluster type mismatch. Existing: {existingType}, Current: {clusterType}");
                return false;
            }

            return true;
        }

        private async Task<string?> GetBestPodIp(V1PodList pods, string prometheusQuery, string namespaceName, string clusterType)
        {
            var bestMetric = 0.0;
            string? bestPodIp = pods.Items[0].Status.PodIP;

            foreach (var pod in pods.Items)
            {
                if (pod.Status?.PodIP == null || pod.Metadata?.Name == null)
                {
                    LogPodSkipReason(pod);
                    continue;
                }

                var query = prometheusQuery
                    .Replace("%pod-name%", pod.Metadata.Name)
                    .Replace("%namespace%", namespaceName)
                    .Replace("%cluster-name%", clusterType);

                try
                {
                    var metric = await _prometheusClient.GetMetricValueAsync(query);
                    var metricValue = metric.HasValue ? metric.Value : 0;

                    if (metricValue >= bestMetric)
                    {
                        bestMetric = metricValue;
                        bestPodIp = pod.Status.PodIP;
                    }
                }
                catch (Exception ex)
                {
                    logger.Warning($"Failed to get metric for pod {pod.Metadata.Name}: {ex.Message}");
                }
            }

            return bestPodIp;
        }

        private async Task HandleStandardRecords(V1PodList pods, string domain, bool isTopLevelRequest, ConcurrentDictionary<string, Record> newRecords)
        {
            foreach (var pod in pods.Items)
            {
                if (pod.Status == null || string.IsNullOrEmpty(pod.Status.PodIP) ||
                    pod.Metadata == null || string.IsNullOrEmpty(pod.Metadata.Name))
                {
                    LogPodSkipReason(pod);
                    continue;
                }

                try
                {
                    string fqdn = BuildFQDN(domain, pod.Metadata.Name, isTopLevelRequest);
                    await ProcessRecord(newRecords, fqdn, pod.Status.PodIP);
                }
                catch (Exception ex)
                {
                    logger.Error($"Error processing pod {pod.Metadata.Name}: {ex}");
                }
            }
        }

        private void LogPodSkipReason(k8s.Models.V1Pod pod)
        {
            if (pod.Status == null)
                logger.Debug($"Skipping pod {pod.Metadata?.Name}: Status is null");
            else if (string.IsNullOrEmpty(pod.Status.PodIP))
                logger.Debug($"Skipping pod {pod.Metadata?.Name}: PodIP is null or empty");
            else if (pod.Metadata == null)
                logger.Debug($"Skipping pod: Metadata is null");
            else if (string.IsNullOrEmpty(pod.Metadata.Name))
                logger.Debug($"Skipping pod: Pod name is null or empty");
        }

        private string BuildFQDN(string domain, string podName, bool isTopLevelRequest)
        {
            domain = domain.TrimEnd('.');

            if (isTopLevelRequest)
            {
                if (!domain.EndsWith(".peak", StringComparison.OrdinalIgnoreCase))
                {
                    domain = $"{domain}.peak";
                }
                return $"{domain}.";
            }

            var baseDomain = domain.EndsWith(".peak", StringComparison.OrdinalIgnoreCase)
                ? domain
                : $"{domain}.peak";
            return $"{GenerateShortHash(podName)}.{baseDomain}.";
        }

        private async Task ProcessRecord(ConcurrentDictionary<string, Record> newRecords, string fqdn, string podIP)
        {
            var record = Record.CreateARecord(_configSettings, fqdn, _settings.Dns.RecordTTL, podIP);
            logger.Debug($"Created A record: {fqdn} -> {podIP}");

            if (newRecords.TryAdd(fqdn, record))
            {
                bind.AddRecord(record);
                logger.Debug($"Added new record: {fqdn}");
            }
        }

        private void UpdateCurrentRecords(ConcurrentDictionary<string, Record> newRecords)
        {
            _currentRecords.Clear();
            foreach (var record in newRecords)
            {
                _currentRecords.TryAdd(record.Key, record.Value);
            }
        }

        private bool CompareRecords(Record r1, Record r2)
        {
            if (r1.type != r2.type) return false;
            if (r1.name != r2.name) return false;
            if (r1.data == null || r2.data == null) return false;
            if (r1.data.Length != r2.data.Length) return false;

            return r1.data.SequenceEqual(r2.data);
        }

        private string GenerateShortHash(string input)
        {
            using (var md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < _settings.Hash.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}