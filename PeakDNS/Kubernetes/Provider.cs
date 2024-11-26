using PeakDNS.Storage;
using PeakDNS.DNS;
using PeakDNS.DNS.Server;
using k8s;
using k8s.Models;
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PeakDNS.Kubernetes
{
    public class Provider
    {
        private readonly Settings settings;
        private readonly k8s.Kubernetes _client;
        private static Logging<Provider> logger;
        private CancellationTokenSource _cancellationTokenSource;
        public BIND bind;

        // Track current records for cleanup
        private readonly ConcurrentDictionary<string, Record> _currentRecords = new();

        public Provider(Settings settings)
        {
            this.settings = settings;

            // Initialize BIND with wildcard matching
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
        }

        public void Start()
        {
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

        private readonly ConcurrentDictionary<string, string> _reservedTopDomains = new();

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

                    // Log all labels for debugging
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

                    // If it's a top level request, check if domain is already reserved
                    if (isTopLevelRequest)
                    {
                        logger.Debug($"Processing top-level domain request for {domain}");
                        if (_reservedTopDomains.TryGetValue(domain, out string? existingNs))
                        {
                            logger.Debug($"Domain {domain} is already reserved by namespace {existingNs}");
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

                    foreach (var pod in pods.Items)
                    {
                        if (pod.Status == null ||
                            string.IsNullOrEmpty(pod.Status.PodIP) ||
                            pod.Metadata == null ||
                            string.IsNullOrEmpty(pod.Metadata.Name))
                        {
                            if (pod.Status == null)
                            {
                                logger.Debug($"Skipping pod {pod.Metadata?.Name}: Status is null");
                                continue;
                            }
                            if (string.IsNullOrEmpty(pod.Status.PodIP))
                            {
                                logger.Debug($"Skipping pod {pod.Metadata?.Name}: PodIP is null or empty");
                                continue;
                            }
                            if (pod.Metadata == null)
                            {
                                logger.Debug($"Skipping pod: Metadata is null");
                                continue;
                            }
                            if (string.IsNullOrEmpty(pod.Metadata.Name))
                            {
                                logger.Debug($"Skipping pod: Pod name is null or empty");
                                continue;
                            }

                            continue;
                        }

                        try
                        {
                            string fqdn;
                            // Remove any trailing dots from the domain first
                            domain = domain.TrimEnd('.');

                            // For top-level domains, don't add any prefix regardless of .peak
                            if (isTopLevelRequest)
                            {
                                // Ensure we have .peak at the end
                                if (!domain.EndsWith(".peak", StringComparison.OrdinalIgnoreCase))
                                {
                                    domain = $"{domain}.peak";
                                }
                                fqdn = $"{domain}.";
                            }
                            else
                            {
                                // For non-top-level, add hash prefix and ensure .peak suffix
                                var baseDomain = domain.EndsWith(".peak", StringComparison.OrdinalIgnoreCase)
                                    ? domain
                                    : $"{domain}.peak";
                                fqdn = $"{GenerateShortHash(pod.Metadata.Name)}.{baseDomain}.";
                            }

                            logger.Debug($"Creating record - FQDN: {fqdn}, Pod: {pod.Metadata.Name}, IP: {pod.Status.PodIP}, TopLevel: {isTopLevelRequest}");

                            var record = Record.CreateARecord(settings, fqdn, 300, pod.Status.PodIP);
                            logger.Debug($"Created A record: {fqdn} -> {pod.Status.PodIP}");

                            if (newRecords.TryAdd(fqdn, record))
                            {
                                logger.Debug($"Added new record to collection: {fqdn}");
                            }

                            // Add or update record in BIND
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
                        catch (Exception ex)
                        {
                            logger.Error($"Error processing pod {pod.Metadata.Name}: {ex}");
                        }
                    }
                }

                // Clean up old records
                foreach (var oldRecord in _currentRecords)
                {
                    if (!newRecords.ContainsKey(oldRecord.Key))
                    {
                        bind.RemoveRecord(oldRecord.Value);
                        logger.Debug($"Removed old record: {oldRecord.Key}");
                    }
                }

                // Update current records
                _currentRecords.Clear();
                foreach (var record in newRecords)
                {
                    _currentRecords.TryAdd(record.Key, record.Value);
                }

                logger.Debug($"Update completed. Total active records: {_currentRecords.Count}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error reading Kubernetes data: {ex.Message}");
                throw;
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