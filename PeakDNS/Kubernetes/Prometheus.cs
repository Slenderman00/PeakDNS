using k8s;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PeakDNS.Kubernetes
{
    public class PrometheusClient
    {
        private readonly IKubernetes _client;
        private string _prometheusDNSname;
        private string _name;
        private readonly Logging<PrometheusClient> logger;

        public PrometheusClient(Settings settings)
        {
            _name = settings.GetSetting("provider", "prometheusDNSName", "prometheus-operated");
            logger = new Logging<PrometheusClient>(
                settings.GetSetting("logging", "path", "./log.txt"),
                logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5"))
            );
            
            logger.Info("Initializing PrometheusClient");
            logger.Debug($"Using prometheus DNS name: {_name}");
            
            try
            {
                _client = new k8s.Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig());
                logger.Info("Successfully created Kubernetes client");
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to create Kubernetes client: {ex.Message}");
                throw;
            }
        }

        public void Initialize()
        {
            logger.Info("Starting PrometheusClient initialization");
            
            _prometheusDNSname = _name;
            logger.Info($"Using Prometheus service DNS: {_prometheusDNSname}");
        }

        

        public async Task<string> GetPodWithLowestMetricAsync(string query)
        {
            logger.Info($"Getting pod with lowest metric for query: {query}");
            
            using (var client = new HttpClient())
            {
                try
                {
                    var url = $"http://{_prometheusDNSname}:9090/api/v1/query?query={Uri.EscapeDataString(query)}";
                    logger.Debug($"Making request to: {url}");
                    
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    
                    var json = await response.Content.ReadAsStringAsync();
                    logger.Debug($"Received response: {json}");
                    
                    var result = JsonConvert.DeserializeObject<PrometheusQueryResult>(json);
                    var lowestPod = result.Data.Result
                        .OrderBy(r => double.Parse(r.Value.Last().ToString()))
                        .FirstOrDefault();

                    var podName = lowestPod?.Metric["kubernetes_pod_name"].ToString();
                    logger.Info($"Found pod with lowest metric: {podName}");
                    return podName;
                }
                catch (Exception ex)
                {
                    logger.Error($"Error getting pod with lowest metric: {ex.Message}");
                    throw;
                }
            }
        }

        public async Task<double?> GetMetricValueAsync(string query)
        {
            logger.Info($"Getting metric value for query: {query}");
            
            using (var client = new HttpClient())
            {
                try
                {
                    var url = $"http://{_prometheusDNSname}:9090/api/v1/query?query={Uri.EscapeDataString(query)}";
                    logger.Debug($"Making request to: {url}");
                    
                    var response = await client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    
                    var json = await response.Content.ReadAsStringAsync();
                    logger.Debug($"Received response: {json}");
                    
                    var result = JObject.Parse(json);
                    var value = result["data"]?["result"]?[0]?["value"]?[1]?.Value<double?>();
                    
                    logger.Info($"Retrieved metric value: {value}");
                    return value;
                }
                catch (Exception ex)
                {
                    logger.Error($"Error getting metric value: {ex.Message}");
                    throw;
                }
            }
        }

        private class PrometheusQueryResult
        {
            public PrometheusData? Data { get; set; }
        }

        private class PrometheusData
        {
            public List<PrometheusMetric>? Result { get; set; }
        }

        private class PrometheusMetric
        {
            public Dictionary<string, object>? Metric { get; set; }
            public List<object>? Value { get; set; }
        }
    }
}