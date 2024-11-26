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

        private async Task Update()
        {
            try
            {
                // Create new records set
                var newRecords = new ConcurrentDictionary<string, Record>();

                var namespaces = await _client.ListNamespaceAsync();
                foreach (var ns in namespaces.Items)
                {
                    if (ns.Metadata?.Labels == null ||
                        !ns.Metadata.Labels.TryGetValue("dns.peak/domain", out string? domain))
                    {
                        continue;
                    }

                    var pods = await _client.ListNamespacedPodAsync(ns.Metadata.Name);
                    foreach (var pod in pods.Items)
                    {
                        if (pod.Status == null || 
                            string.IsNullOrEmpty(pod.Status.PodIP) || 
                            pod.Metadata == null ||
                            string.IsNullOrEmpty(pod.Metadata.Name))
                        {
                            continue;
                        }

                        try
                        {
                            string podHash = GenerateShortHash(pod.Metadata.Name);
                            string fqdn = $"{podHash}.{domain}.";
                            
                            logger.Debug($"Updating record - FQDN: {fqdn}, Pod: {pod.Metadata.Name}, IP: {pod.Status.PodIP}");

                            var record = Record.CreateARecord(settings, fqdn, 300, pod.Status.PodIP);
                            newRecords.TryAdd(fqdn, record);

                            // Add or update record in BIND
                            if (!_currentRecords.TryGetValue(fqdn, out var existingRecord))
                            {
                                bind.AddRecord(record);
                                logger.Debug($"Added new record for {fqdn}");
                            }
                            else if (!CompareRecords(existingRecord, record))
                            {
                                // Remove old record and add new one if IP changed
                                bind.RemoveRecord(existingRecord);
                                bind.AddRecord(record);
                                logger.Debug($"Updated record for {fqdn}");
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error($"Error processing pod {pod.Metadata.Name}: {ex}");
                        }
                    }
                }

                // Remove records that no longer exist
                foreach (var oldRecord in _currentRecords)
                {
                    if (!newRecords.ContainsKey(oldRecord.Key))
                    {
                        bind.RemoveRecord(oldRecord.Value);
                        logger.Debug($"Removed record for {oldRecord.Key}");
                    }
                }

                // Update current records
                _currentRecords.Clear();
                foreach (var record in newRecords)
                {
                    _currentRecords.TryAdd(record.Key, record.Value);
                }
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