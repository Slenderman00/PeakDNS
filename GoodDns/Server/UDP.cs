using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace GoodDns
{
    class UDP
    {
        Logging<UDP> logger = new Logging<UDP>("./log.txt", logLevel: 5);

        //Make the UdpClient nullable to get the linter to shut up
        UdpClient listener;
        Task? listenTask;
        Task[] ClientPool = new Task[10];
        CancellationTokenSource cts = new CancellationTokenSource();

        public delegate void PacketHandlerCallback(byte[] packet, bool isTCP, UniversalClient client);
        PacketHandlerCallback callback;
        public UDP(int port, PacketHandlerCallback packetHandler)
        {
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
            callback(data.Buffer, false, new UniversalClient(udpClient: listener, tcpClient: null));
        }

        public void Stop()
        {
            logger.Info("Stopping UDP server");
            listener?.Close();
            cts.Cancel();
        }
    }
}