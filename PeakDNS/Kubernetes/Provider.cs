using PeakDNS.DNS.Server;
using k8s;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PeakDNS.Kubernetes
{
    public class Provider
    {
        private readonly Settings settings;
        private readonly k8s.Kubernetes _client;
        private static Logging<Provider> logger;
        private CancellationTokenSource _cancellationTokenSource;
        public BIND bind;
        private PrometheusClient _prometheusClient;
        private readonly ConcurrentDictionary<string, string> _clusterTypes = new();
        private readonly ConcurrentDictionary<string, Record> _currentRecords = new();
        private readonly ConcurrentDictionary<string, string> _reservedTopDomains = new();

        public Provider(Settings settings)
        {
            this.settings = settings;
            bind = new BIND(settings, "peak.");

            bind.SetSOARecord(
                primaryNameserver: "ns1.peak.",
                hostmaster: "admin.peak.",
                serial: "2024032601",
                refresh: "3600",
                retry: "1800",
                expire: "604800",
                ttl: 3600,
                minimumTTL: 300
            );

            logger = new Logging<Provider>(
                settings.GetSetting("logging", "path", "./log.txt"),
                logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5"))
            );

            logger.Info("Provider initialized with SOA record");

            var config = KubernetesClientConfiguration.InClusterConfig();
            _client = new k8s.Kubernetes(config);
            _prometheusClient = new PrometheusClient(settings);
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

                // Split the string into exactly 2 parts after removing prefix
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
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }

        private async Task Update()
        {
            try
            {
                logger.Debug("Starting DNS record update");
                var newRecords = new ConcurrentDictionary<string, Record>();
                var namespaces = await _client.ListNamespaceAsync();

                foreach (var ns in namespaces.Items)
                {
                    logger.Debug($"Processing namespace: {ns.Metadata?.Name}");

                    if (ns.Metadata?.Labels == null)
                    {
                        logger.Debug($"Namespace {ns.Metadata?.Name} has no labels, skipping");
                        continue;
                    }

                    foreach (var label in ns.Metadata.Labels)
                    {
                        logger.Debug($"Namespace {ns.Metadata.Name} label: {label.Key}={label.Value}");
                    }

                    if (!ns.Metadata.Labels.TryGetValue("dns.peak/domain", out string? domain))
                    {
                        logger.Debug($"Namespace {ns.Metadata.Name} has no dns.peak/domain label, skipping");
                        continue;
                    }

                    logger.Debug($"Found domain label: {domain} for namespace {ns.Metadata.Name}");

                    bool isTopLevelRequest = false;
                    if (ns.Metadata.Labels.TryGetValue("dns.peak/only-top", out string? onlyTop))
                    {
                        logger.Debug($"Found only-top label: {onlyTop} for namespace {ns.Metadata.Name}");
                        isTopLevelRequest = bool.TryParse(onlyTop, out bool isOnlyTop) && isOnlyTop;
                        logger.Debug($"Parsed only-top to: {isTopLevelRequest}");
                    }

                    if (isTopLevelRequest)
                    {
                        logger.Debug($"Processing top-level domain request for {domain}");
                        if (_reservedTopDomains.TryGetValue(domain, out string? existingNs))
                        {
                            if (existingNs != ns.Metadata.Name)
                            {
                                logger.Warning($"Namespace {ns.Metadata.Name} attempted to register reserved top domain {domain} (owned by {existingNs})");
                                continue;
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
                        continue;
                    }

                    var pods = await _client.ListNamespacedPodAsync(ns.Metadata.Name);
                    logger.Debug($"Found {pods.Items.Count} pods in namespace {ns.Metadata.Name}");

                    logger.Debug($"Checking for loadbalance label: {ns.Metadata.Labels.ContainsKey("dns.peak/loadbalance")}");
                    if (ns.Metadata.Labels.TryGetValue("dns.peak/loadbalance", out string? labelValue))
                    {
                        var prometheusQuery = await GetLoadBalanceExpression(labelValue, ns.Metadata.Name);
                        if (string.IsNullOrEmpty(prometheusQuery))
                        {
                            logger.Warning($"Failed to get loadbalance expression for namespace {ns.Metadata.Name}");
                            continue;
                        }

                        var clusterType = ns.Metadata.Labels.TryGetValue("cluster-type", out string type) ? type : "default";

                        if (!_clusterTypes.TryGetValue(domain, out string existingType))
                        {
                            _clusterTypes.TryAdd(domain, clusterType);
                        }
                        else if (existingType != clusterType)
                        {
                            logger.Warning($"Skipping load balancing for domain {domain} due to cluster type mismatch. Existing: {existingType}, Current: {clusterType}");
                            continue;
                        }

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
                                .Replace("%namespace%", ns.Metadata.Name)
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

                        if (bestPodIp != null)
                        {
                            var fqdn = $"{domain}.";
                            await ProcessRecord(newRecords, fqdn, bestPodIp);
                        }
                        continue;
                    }

                    foreach (var pod in pods.Items)
                    {
                        if (pod.Status == null ||
                            string.IsNullOrEmpty(pod.Status.PodIP) ||
                            pod.Metadata == null ||
                            string.IsNullOrEmpty(pod.Metadata.Name))
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

                CleanupOldRecords(newRecords);
                UpdateCurrentRecords(newRecords);

                logger.Debug($"Update completed. Total active records: {_currentRecords.Count}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error reading Kubernetes data: {ex.Message}");
                throw;
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
            var record = Record.CreateARecord(settings, fqdn, 300, podIP);
            logger.Debug($"Created A record: {fqdn} -> {podIP}");

            if (newRecords.TryAdd(fqdn, record))
            {
                logger.Debug($"Added new record to collection: {fqdn}");
            }

            if (!_currentRecords.TryGetValue(fqdn, out var existingRecord))
            {
                bind.AddRecord(record);
                logger.Debug($"Added new record to BIND: {fqdn}");
            }
            else if (!CompareRecords(existingRecord, record))
            {
                bind.RemoveRecord(existingRecord);
                bind.AddRecord(record);
                logger.Debug($"Updated existing record in BIND: {fqdn}");
            }
            else
            {
                logger.Debug($"Record unchanged in BIND: {fqdn}");
            }
        }

        private void CleanupOldRecords(ConcurrentDictionary<string, Record> newRecords)
        {
            foreach (var oldRecord in _currentRecords)
            {
                if (!newRecords.ContainsKey(oldRecord.Key))
                {
                    bind.RemoveRecord(oldRecord.Value);
                    logger.Debug($"Removed old record: {oldRecord.Key}");
                }
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
                for (int i = 0; i < 4; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}