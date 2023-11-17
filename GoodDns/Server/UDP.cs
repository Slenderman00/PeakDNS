using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace GoodDns
{
    class UDP
    {
        //Make the UdpClient nullable to get the linter to shut up
        UdpClient listener;
        Task? listenTask;
        Task[] ClientPool = new Task[10];
        CancellationTokenSource cts = new CancellationTokenSource();
        bool running = false;

        public delegate void PacketHandlerCallback(byte[] packet, bool isTCP);
        PacketHandlerCallback callback;
        public UDP(int port, PacketHandlerCallback packetHandler)
        {
            listener = new UdpClient(port);
            callback = packetHandler;
        }

        public void Start()
        {
            CancellationToken ct = cts.Token;

            running = true;

            listenTask = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        assignTask(await listener.ReceiveAsync(), ct);
                    }
                }
                catch (ObjectDisposedException e)
                {

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
            callback(data.Buffer, false);
        }

        public void Stop()
        {
            running = false;
            listener?.Close();
            cts.Cancel();
        }
    }
}