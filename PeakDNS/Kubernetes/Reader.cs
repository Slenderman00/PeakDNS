using k8s;
using k8s.Models;
namespace PeakDNS.Kubernetes 
{    public class SimpleKubernetesReader
    {
        Settings settings;
        private readonly Kubernetes _client;
        static Logging<SimpleKubernetesReader> logger;

        public SimpleKubernetesReader(Settings settings = null)
        {
            static Logging<SimpleKubernetesReader> logger = new Logging<SimpleKubernetesReader>(settings.GetSetting("logging", "path", "./log.txt"), logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5")));
            var config = KubernetesClientConfiguration.InClusterConfig();
            _client = new Kubernetes(config);
        }

        public async Task PrintDomainsAndIPs()
        {
            try 
            {
                var namespaces = await _client.ListNamespaceAsync();
                
                foreach (var ns in namespaces.Items)
                {
                    // Skip if namespace has no labels or no dns domain
                    if (ns.Metadata.Labels == null || 
                        !ns.Metadata.Labels.TryGetValue("dns.peak/domain", out string? domain))
                    {
                        continue;
                    }

                    Console.WriteLine($"\nDomain: {domain}");
                    
                    // Get pods in this namespace
                    var pods = await _client.ListNamespacedPodAsync(ns.Metadata.Name);
                    foreach (var pod in pods.Items)
                    {
                        if (!string.IsNullOrEmpty(pod.Status.PodIP))
                        {
                            logger.Debug($"  Pod: {pod.Metadata.Name}");
                            logger.Debug($"  IP:  {pod.Status.PodIP}");
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