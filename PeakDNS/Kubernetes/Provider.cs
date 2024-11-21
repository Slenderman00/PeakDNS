using PeakDNS.Storage;
using PeakDNS.DNS;
using k8s;
using k8s.Models;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PeakDNS.DNS.Server;

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

        private Question CreateQuestion(string domainName, string podIP) {
            // A check must be implemented to check if the addess is IPv6 or IPv4 
            Question question = new Question(domainName, RTypes.A, RClasses.IN, this.settings);
            return question;
        }

        private Answer CreateAnswer(string domainName, string podIP) {
            byte[] rData = Utility.ParseIP(podIP);
            Answer answer = new Answer(domainName, RTypes.A, RClasses.IN, 60, (ushort)rData.Length, rData);
            return answer;
        }

        private Packet CreatePacket(string domainName, string podIP) {
            Question question = CreateQuestion(domainName, podIP);
            Answer answer = CreateAnswer(domainName, podIP);
            Packet packet = new Packet(this.settings);
            packet.AddQuestion(question);
            
            Answer[] answers = new Answer[1];
            answers[0] = answer; 
            packet.answers = answers;

            return packet;
        }

        private void Update()
        {
            Cache _cache = new Cache(this.settings);
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
                        if (pod.Status == null || string.IsNullOrEmpty(pod.Status.PodIP) || pod.Metadata == null)
                        {
                            continue;
                        }
     
                        string podHash = GenerateShortHash(pod.Metadata?.Name);
                        logger.Debug($"Domain: {podHash}.{domain}");
                        logger.Debug($"Pod: {pod.Metadata?.Name}");
                        logger.Debug($"IP: {pod.Status.PodIP}");

                        if (podHash == null) throw new ArgumentNullException(nameof(podHash));
                        if (domain == null) throw new ArgumentNullException(nameof(domain));
                        if (settings == null) throw new ArgumentNullException(nameof(settings));

                        Packet packet = CreatePacket($"{podHash}.{domain}.", pod.Status.PodIP);
                        _cache.addRecord(packet);
                        
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error reading Kubernetes data: {ex.Message}");
            }
            this.cache = _cache;
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