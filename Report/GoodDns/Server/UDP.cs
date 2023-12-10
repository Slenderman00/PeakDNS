using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace GoodDns
{
    class UDP
    {
        Settings settings;
        Logging<UDP> logger;

        //Make the UdpClient nullable to get the linter to shut up
        UdpClient listener;
        Task? listenTask;
        Task[] ClientPool;
        CancellationTokenSource cts = new CancellationTokenSource();

        public delegate void PacketHandlerCallback(byte[] packet, bool isTCP, UniversalClient client);
        PacketHandlerCallback callback;
        public UDP(int port, PacketHandlerCallback packetHandler, Settings settings)
        {
            this.settings = settings;
            logger = new Logging<UDP>(settings.GetSetting("logging", "path", "./log.txt"), logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5")));
            ClientPool = new Task[int.Parse(settings.GetSetting("server", "udpThreads", "10"))];
            listener = new UdpClient(port);
            callback = packetHandler;
        }

        public void Start()
        {
            logger.Info("Starting UDP server");

            CancellationToken ct = cts.Token;

            listenTask = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        assignTask(await listener.ReceiveAsync(), ct);
                    }
                }
                catch (ObjectDisposedException)
                {
                    logger.Warning("ObjectDisposedException: listener was disposed");
                }

            }, ct);

            Task.WaitAll(Array.FindAll(ClientPool, task => task != null));

        }
        private void assignTask(UdpReceiveResult data, CancellationToken ct)
        {
            for (int i = 0; i < ClientPool.Length; i++)
            {
                if (ClientPool[i] == null || ClientPool[i].IsCompleted)
                {
                    ClientPool[i] = Task.Run(() =>
                    {
                        HandleClient(data);
                    }, ct);
                    break;
                }
            }
        }

        private void HandleClient(UdpReceiveResult data)
        {
            logger.Info("Client connected from: " + data.RemoteEndPoint);
            //create a new udp socket for use by the UniversalClient
            callback(data.Buffer, false, new UniversalClient(clientEndPoint: data.RemoteEndPoint, udpClient: listener, tcpClient: null));
        }

        public void Stop()
        {
            logger.Info("Stopping UDP server");
            listener?.Close();
            cts.Cancel();
        }
    }
}