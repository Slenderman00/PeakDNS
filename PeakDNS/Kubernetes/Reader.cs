using k8s;
using k8s.Models;

namespace PeakDNS.Kubernetes
{
    public class SimpleKubernetesReader
    {
        private readonly Settings settings;
        private readonly k8s.Kubernetes _client;
        private static Logging<SimpleKubernetesReader> logger;

        public SimpleKubernetesReader(Settings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            logger = new Logging<SimpleKubernetesReader>(
                settings.GetSetting("logging", "path", "./log.txt"),
                logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5"))
            );

            var config = KubernetesClientConfiguration.InClusterConfig();
            _client = new k8s.Kubernetes(config);
        }

        public void PrintDomainsAndIPs()
        {
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
                            string podName = pod.Metadata?.Name;
                            podName = podName.TrimEnd('-').Substring(0, 8);
                            logger.Debug($"\nDomain: {podName}.{domain}");
                            logger.Debug($" Pod: {pod.Metadata?.Name}");
                            logger.Debug($" IP: {pod.Status.PodIP}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error reading Kubernetes data: {ex.Message}");
            }
        }
    }
}