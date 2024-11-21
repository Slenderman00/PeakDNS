using k8s;
using k8s.Models;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PeakDNS.Kubernetes
{
    public class Provider
    {
        public Cache cache;
        private readonly Settings settings;
        private readonly k8s.Kubernetes _client;
        private static Logging<Provider> logger;
        private CancellationTokenSource _cancellationTokenSource;

        public Provider(Settings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            logger = new Logging<Provider>(
                settings.GetSetting("logging", "path", "./log.txt"),
                logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5"))
            );
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
                Update();
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }

        private void Update()
        {
            // Cache _cache = new Cache();
            try
            {
                var namespaces = _client.ListNamespace();
                foreach (var ns in namespaces.Items)
                {
                    if (ns.Metadata?.Labels == null ||
                        !ns.Metadata.Labels.TryGetValue("dns.peak/domain", out string? domain))
                    {
                        continue;
                    }

                    var pods = _client.ListNamespacedPod(ns.Metadata.Name);
                    foreach (var pod in pods.Items)
                    {
                        if (!string.IsNullOrEmpty(pod.Status?.PodIP))
                        {
                            string podHash = GenerateShortHash(pod.Metadata?.Name);
                            logger.Debug($"Domain: {podHash}.{domain}");
                            logger.Debug($"Pod: {pod.Metadata?.Name}");
                            logger.Debug($"IP: {pod.Status.PodIP}");

                            var podMetrics = _client.GetNamespacedPodMetricsByLabel(ns.Metadata.Name, $"app={pod.Metadata?.Name}", null);
                            if (podMetrics.Items != null && podMetrics.Items.Count > 0)
                            {
                                var cpuUsage = podMetrics.Items[0].Containers[0].Usage["cpu"];
                                var memoryUsage = podMetrics.Items[0].Containers[0].Usage["memory"];
                                logger.Debug($"CPU Usage: {cpuUsage}");
                                logger.Debug($"Memory Usage: {memoryUsage}");
                            }
                            else
                            {
                                logger.Debug("No load metrics available for the pod.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error reading Kubernetes data: {ex.Message}");
            }
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