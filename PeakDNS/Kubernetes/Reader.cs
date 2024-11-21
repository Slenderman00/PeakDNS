using k8s;
using k8s.Models;
using System.Security.Cryptography;

namespace PeakDNS.Kubernetes
{
    public class KubernetesReader
    {
        private readonly Settings settings;
        private readonly k8s.Kubernetes _client;
        private static Logging<KubernetesReader> logger;

        public KubernetesReader(Settings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            logger = new Logging<KubernetesReader>(
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
                            string podHash =  GenerateShortHash(pod.Metadata?.Name);
                            logger.Debug($"Domain: {podHash}.{domain}");
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

private static string GenerateShortHash(string input)
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
