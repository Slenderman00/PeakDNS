using PeakDNS.DNS.Server;
using k8s;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using k8s.Models;

namespace PeakDNS.Kubernetes
{

    public class Provider
    {
        private readonly k8s.Kubernetes _client;
        private static Logging<Provider> logger;
        private CancellationTokenSource _cancellationTokenSource;
        public BIND bind;
        private PrometheusClient _prometheusClient;
        private readonly ConcurrentDictionary<string, string> _clusterTypes = new();
        private readonly ConcurrentDictionary<string, Record> _currentRecords = new();
        private readonly ConcurrentDictionary<string, string> _reservedTopDomains = new();
        private readonly Settings _configSettings;

        public Provider(Settings configSettings)
        {
            _configSettings = configSettings;
            bind = new BIND(configSettings, _configSettings.GetSetting("dns", "prefix", "peak."));

            bind.SetSOARecord(
                primaryNameserver: _configSettings.GetSetting("soa", "primaryNameserver", "ns1.peak."),
                hostmaster: _configSettings.GetSetting("soa", "hostmaster", "admin.peak."),
                serial: _configSettings.GetSetting("soa", "serial", "2024032601"),
                refresh: _configSettings.GetSetting("soa", "refresh", "3600"),
                retry: _configSettings.GetSetting("soa", "retry", "1800"),
                expire: _configSettings.GetSetting("soa", "expire", "604800"),
                ttl: int.Parse(_configSettings.GetSetting("soa", "ttl", "3600")),
                minimumTTL: int.Parse(_configSettings.GetSetting("soa", "minimumTTL", "300"))
            );

            logger = new Logging<Provider>(
                _configSettings.GetSetting("logging", "path", "./log.txt"),
                int.Parse(_configSettings.GetSetting("logging", "logLevel", "5"))
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
                await Task.Delay(TimeSpan.FromSeconds(int.Parse(_configSettings.GetSetting("dns", "updateInterval", "30"))), cancellationToken);
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
                    await ProcessLabels(ns, newRecords);
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
        private record DeploymentInfo(V1Deployment Deployment, IDictionary<string, string> Labels);
        private record EffectiveConfig(string Domain, bool TopLevel, string? LoadBalance);

        private async Task ProcessLabels(V1Namespace ns, ConcurrentDictionary<string, Record> newRecords)
        {
            logger.Debug($"Starting ProcessLabels for namespace {ns.Metadata.Name}");

            if (!ValidateAndInitializeNamespace(ns, out string? namespaceDomain, out bool isTopLevelRequest))
            {
                return;
            }

            var deploymentSelectors = await GetDeploymentSelectors(ns);
            var allPods = await GetNamespacedPods(ns);
            var podGroups = GroupPodsByDeployment(allPods, deploymentSelectors);

            await ProcessDeploymentPods(ns, podGroups.DeploymentPods, deploymentSelectors, namespaceDomain, isTopLevelRequest, newRecords);
            await ProcessStandalonePods(ns, podGroups.StandalonePods, namespaceDomain, isTopLevelRequest, newRecords);

            logger.Debug($"Completed ProcessLabels for namespace {ns.Metadata.Name}");
        }

        private bool ValidateAndInitializeNamespace(V1Namespace ns, out string? namespaceDomain, out bool isTopLevelRequest)
        {
            if (!ValidateNamespace(ns, out namespaceDomain, out isTopLevelRequest))
            {
                logger.Debug($"Namespace validation failed for {ns.Metadata.Name}");
                return false;
            }

            if (!HandleTopLevelDomain(ns, namespaceDomain, isTopLevelRequest).Result)
            {
                logger.Debug($"Top level domain handling failed for namespace {ns.Metadata.Name}, domain {namespaceDomain}");
                return false;
            }

            return true;
        }

        private async Task<Dictionary<string, DeploymentInfo>> GetDeploymentSelectors(V1Namespace ns)
        {
            var deployments = await _client.ListNamespacedDeploymentAsync(ns.Metadata.Name);
            return deployments.Items
                .Where(d => d.Metadata?.Name != null && d.Spec?.Selector?.MatchLabels != null)
                .ToDictionary(
                    d => d.Metadata.Name,
                    d => new DeploymentInfo(d, d.Spec.Selector.MatchLabels)
                );
        }

        private async Task<V1PodList> GetNamespacedPods(V1Namespace ns)
        {
            logger.Debug($"Fetching all pods for namespace {ns.Metadata.Name}");
            return await _client.ListNamespacedPodAsync(ns.Metadata.Name);
        }

        private (Dictionary<string, List<V1Pod>> DeploymentPods, List<V1Pod> StandalonePods) GroupPodsByDeployment(
            V1PodList pods,
            Dictionary<string, DeploymentInfo> deploymentSelectors)
        {
            var podsByDeployment = new Dictionary<string, List<V1Pod>>();
            var standalonePods = new List<V1Pod>();

            foreach (var pod in pods.Items)
            {
                if (!IsValidPod(pod)) continue;

                var owningDeployment = FindOwningDeployment(pod, deploymentSelectors);
                if (owningDeployment != null)
                {
                    if (!podsByDeployment.ContainsKey(owningDeployment))
                    {
                        podsByDeployment[owningDeployment] = new List<V1Pod>();
                    }
                    podsByDeployment[owningDeployment].Add(pod);
                }
                else
                {
                    standalonePods.Add(pod);
                }
            }

            return (podsByDeployment, standalonePods);
        }

        private bool IsValidPod(V1Pod pod)
        {
            if (pod.Status?.PodIP == null || pod.Metadata?.Labels == null)
            {
                LogPodSkipReason(pod);
                return false;
            }
            return true;
        }

        private string? FindOwningDeployment(V1Pod pod, Dictionary<string, DeploymentInfo> deploymentSelectors)
        {
            return deploymentSelectors
                .FirstOrDefault(kvp => kvp.Value.Labels.All(label =>
                    pod.Metadata.Labels.ContainsKey(label.Key) &&
                    pod.Metadata.Labels[label.Key] == label.Value))
                .Key;
        }

        private async Task ProcessDeploymentPods(
            V1Namespace ns,
            Dictionary<string, List<V1Pod>> podsByDeployment,
            Dictionary<string, DeploymentInfo> deploymentSelectors,
            string namespaceDomain,
            bool isTopLevelRequest,
            ConcurrentDictionary<string, Record> newRecords)
        {
            var processedPodIPs = new HashSet<string>();

            foreach (var (deploymentName, pods) in podsByDeployment)
            {
                var deployment = deploymentSelectors[deploymentName].Deployment;

                foreach (var pod in pods)
                {
                    if (pod.Status?.PodIP == null || processedPodIPs.Contains(pod.Status.PodIP))
                        continue;

                    var config = DetermineEffectiveConfig(pod, deployment, ns, namespaceDomain, isTopLevelRequest);
                    await ProcessPodWithConfig(ns, pod, config, newRecords);
                    processedPodIPs.Add(pod.Status.PodIP);
                }
            }
        }

        private async Task ProcessStandalonePods(
            V1Namespace ns,
            List<V1Pod> standalonePods,
            string namespaceDomain,
            bool isTopLevelRequest,
            ConcurrentDictionary<string, Record> newRecords)
        {
            var processedPodIPs = new HashSet<string>();

            foreach (var pod in standalonePods)
            {
                if (pod.Status?.PodIP == null || processedPodIPs.Contains(pod.Status.PodIP))
                    continue;

                var config = DetermineEffectiveConfig(pod, null, ns, namespaceDomain, isTopLevelRequest);
                await ProcessPodWithConfig(ns, pod, config, newRecords);
                processedPodIPs.Add(pod.Status.PodIP);
            }
        }

        private EffectiveConfig DetermineEffectiveConfig(V1Pod pod, V1Deployment? deployment, V1Namespace ns, string? namespaceDomain, bool isTopLevelRequest)
        {
            // 1. Check pod labels first (highest priority)
            if (pod.Metadata?.Labels != null && pod.Metadata.Labels.TryGetValue("dns.peak/domain", out var podDomain))
            {
                logger.Debug($"Using pod-level DNS configuration for {pod.Metadata.Name}");
                return new EffectiveConfig(
                    Domain: podDomain,
                    TopLevel: pod.Metadata.Labels.TryGetValue("dns.peak/only-top", out var podOnlyTop) &&
                             bool.TryParse(podOnlyTop, out bool isOnlyTop) && isOnlyTop,
                    LoadBalance: pod.Metadata.Labels.TryGetValue("dns.peak/loadbalance", out var loadBalance) ? loadBalance : null
                );
            }

            // 2. Then deployment labels (medium priority)
            if (deployment?.Metadata?.Labels != null && deployment.Metadata.Labels.TryGetValue("dns.peak/domain", out var deploymentDomain))
            {
                logger.Debug($"Using deployment-level DNS configuration for pod {pod.Metadata?.Name}");
                return new EffectiveConfig(
                    Domain: deploymentDomain,
                    TopLevel: deployment.Metadata.Labels.TryGetValue("dns.peak/only-top", out var depOnlyTop) &&
                             bool.TryParse(depOnlyTop, out bool isOnlyTop) && isOnlyTop,
                    LoadBalance: deployment.Metadata.Labels.TryGetValue("dns.peak/loadbalance", out var loadBalance) ? loadBalance : null
                );
            }

            // 3. Finally namespace labels (lowest priority)
            logger.Debug($"Using namespace-level DNS configuration for pod {pod.Metadata?.Name}");
            return new EffectiveConfig(
                Domain: namespaceDomain ?? "default.peak",
                TopLevel: isTopLevelRequest,
                LoadBalance: ns.Metadata.Labels?.TryGetValue("dns.peak/loadbalance", out var loadBalance) == true ? loadBalance : null
            );
        }
        private async Task ProcessPodWithConfig(
            V1Namespace ns,
            V1Pod pod,
            EffectiveConfig config,
            ConcurrentDictionary<string, Record> newRecords)
        {
            if (config.LoadBalance != null)
            {
                var podList = new V1PodList { Items = new List<V1Pod> { pod } };
                await HandleLoadBalancing(ns, podList, config.Domain, config.LoadBalance, newRecords);
            }
            else
            {
                string fqdn = BuildFQDN(config.Domain, pod.Metadata.Name, config.TopLevel);
                await ProcessRecord(newRecords, fqdn, pod.Status.PodIP);
            }
        }
        private bool ValidateNamespace(V1Namespace ns, out string? domain, out bool isTopLevelRequest)
        {
            domain = null;
            isTopLevelRequest = false;

            if (ns.Metadata == null)
            {
                logger.Debug($"Namespace has no metadata");
                return false;
            }

            logger.Debug($"Checking DNS configuration for namespace {ns.Metadata.Name}");

            bool hasValidConfig = false;

            // First check pod-level DNS labels
            var pods = _client.ListNamespacedPodAsync(ns.Metadata.Name).Result;
            foreach (var pod in pods.Items)
            {
                if (pod.Metadata?.Labels != null && pod.Metadata.Labels.TryGetValue("dns.peak/domain", out var podDomain))
                {
                    logger.Debug($"Found pod {pod.Metadata.Name} with DNS domain {podDomain} in namespace {ns.Metadata.Name}");
                    hasValidConfig = true;
                    break;
                }
            }

            // Then check deployment-level DNS labels
            if (!hasValidConfig)
            {
                var deployments = _client.ListNamespacedDeploymentAsync(ns.Metadata.Name).Result;
                foreach (var deployment in deployments.Items)
                {
                    if (deployment.Metadata?.Labels != null && deployment.Metadata.Labels.TryGetValue("dns.peak/domain", out var deploymentDomain))
                    {
                        logger.Debug($"Found deployment {deployment.Metadata.Name} with DNS domain {deploymentDomain} in namespace {ns.Metadata.Name}");
                        hasValidConfig = true;
                        break;
                    }
                }
            }

            // Finally check namespace-level DNS labels
            if (!hasValidConfig && ns.Metadata.Labels != null && ns.Metadata.Labels.TryGetValue("dns.peak/domain", out domain))
            {
                if (ns.Metadata.Labels.TryGetValue("dns.peak/only-top", out string? onlyTop))
                {
                    isTopLevelRequest = bool.TryParse(onlyTop, out bool isOnlyTop) && isOnlyTop;
                }
                logger.Debug($"Using namespace DNS configuration: domain={domain}, isTopLevelRequest={isTopLevelRequest}");
                hasValidConfig = true;
            }

            if (!hasValidConfig)
            {
                logger.Debug($"No DNS configuration found at any level in namespace {ns.Metadata.Name}");
                return false;
            }

            return true;
        }

        private async Task<bool> HandleTopLevelDomain(V1Namespace ns, string? namespaceDomain, bool isTopLevelRequest)
        {
            // If namespace domain is null, it means we're only processing pod-level DNS configs
            if (namespaceDomain == null)
            {
                logger.Debug($"Namespace {ns.Metadata.Name} has no domain configuration, proceeding with pod-level DNS");
                return true;
            }

            if (isTopLevelRequest)
            {
                if (_reservedTopDomains.TryGetValue(namespaceDomain, out string? existingNs))
                {
                    if (existingNs != ns.Metadata.Name)
                    {
                        logger.Warning($"Namespace {ns.Metadata.Name} attempted to register reserved top domain {namespaceDomain} (owned by {existingNs})");
                        return false;
                    }
                }
                else
                {
                    _reservedTopDomains.TryAdd(namespaceDomain, ns.Metadata.Name);
                    logger.Info($"Reserved top domain {namespaceDomain} for namespace {ns.Metadata.Name}");
                }
            }
            else if (_reservedTopDomains.ContainsKey(namespaceDomain))
            {
                logger.Warning($"Namespace {ns.Metadata.Name} attempted to use reserved top domain {namespaceDomain}");
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

            // Get the owner (Deployment) labels
            var deployment = await _client.ListNamespacedDeploymentAsync(ns.Metadata.Name);
            var ownerDeployment = deployment.Items.FirstOrDefault(d =>
                d.Spec.Selector.MatchLabels.All(label =>
                    pods.Items.FirstOrDefault()?.Metadata.Labels.ContainsKey(label.Key) == true &&
                    pods.Items.FirstOrDefault()?.Metadata.Labels[label.Key] == label.Value));

            string mode;
            if (ownerDeployment?.Metadata?.Labels != null &&
                ownerDeployment.Metadata.Labels.TryGetValue("dns.peak/loadbalance-mode", out var deploymentMode))
            {
                mode = deploymentMode.ToLowerInvariant();
                logger.Debug($"Using deployment-level load balancing mode: {mode}");
            }
            else if (ns.Metadata.Labels != null &&
                     ns.Metadata.Labels.TryGetValue("dns.peak/loadbalance-mode", out var namespaceMode))
            {
                mode = namespaceMode.ToLowerInvariant();
                logger.Debug($"Using namespace-level load balancing mode: {mode}");
            }
            else
            {
                mode = _configSettings.GetSetting("loadbalancing", "defaultMode", "singlebest");
                logger.Debug($"Using default load balancing mode: {mode}");
            }

            logger.Info($"Load balancing mode for {domain}: {mode}");

            string overloadThresholdStr;
            if (ownerDeployment?.Metadata?.Labels != null &&
                ownerDeployment.Metadata.Labels.TryGetValue("dns.peak/overload-threshold", out var deploymentThreshold))
            {
                overloadThresholdStr = deploymentThreshold;
                logger.Debug($"Using deployment-level overload threshold: {overloadThresholdStr}");
            }
            else if (ns.Metadata.Labels?.TryGetValue("dns.peak/overload-threshold", out var namespaceThreshold) == true)
            {
                overloadThresholdStr = namespaceThreshold;
                logger.Debug($"Using namespace-level overload threshold: {overloadThresholdStr}");
            }
            else
            {
                overloadThresholdStr = _configSettings.GetSetting("loadbalancing", "defaultOverloadThreshold", "1.5");
                logger.Debug($"Using default overload threshold: {overloadThresholdStr}");
            }

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

            if (mode == "excludeoverloaded")
            {
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

                var fqdn = $"{domain}.";
                foreach (var pod in nonOverloaded)
                {
                    var record = Record.CreateARecord(_configSettings, fqdn,
                        int.Parse(_configSettings.GetSetting("dns", "recordTTL", "300")), pod.podIP);
                    var recordKey = $"{fqdn}_{pod.podIP}";
                    if (newRecords.TryAdd(recordKey, record))
                    {
                        bind.AddRecord(record);
                        logger.Debug($"Added record for pod {pod.podIP} with load {pod.metric:F2}");
                    }
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
            var record = Record.CreateARecord(_configSettings, fqdn,
                int.Parse(_configSettings.GetSetting("dns", "recordTTL", "300")), podIP);
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
                for (int i = 0; i < int.Parse(_configSettings.GetSetting("hash", "length", "4")); i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}