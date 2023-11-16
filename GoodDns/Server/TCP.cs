using System.Net;
using System.Net.Sockets;
using System;
using System.Threading.Tasks;

namespace GoodDns {
    class TCP {
        TcpListener? listener;
        Task? listenTask;
        bool running = false;
        Task[] clientPool = new Task[10];
        CancellationTokenSource cts = new CancellationTokenSource();

        public delegate void PacketHandlerCallback(byte[] packet, bool isTCP);
        PacketHandlerCallback callback;
        public TCP(int port, PacketHandlerCallback packetHandler) {
            listener = new TcpListener(IPAddress.Any, port);
            callback = packetHandler;
        }

        public void Start() {
            CancellationToken ct = cts.Token;

            this.running = true;
            listener?.Start();
            listenTask = Task.Run(() => {
                try {
                    TcpClient? client;
                    while((client = listener?.AcceptTcpClient()) != null && (running || !cts.IsCancellationRequested)) {
                        if(client != null) {
                            assignTask(client, ct);
                        }
                    }
                } catch(SocketException e) {

                }

                Task.WaitAll(Array.FindAll(clientPool, task => task != null));
                listener?.Stop();
            }, ct);
        }

        public void assignTask(TcpClient client, CancellationToken ct) {
            for(int i = 0; i < clientPool.Length; i++) {
                if(clientPool[i] == null || clientPool[i].IsCompleted) {
                    clientPool[i] = Task.Run(() => {
                        HandleClient(client, ct);
                    }, ct);
                    break;
                }
            }
        }

        private void HandleClient(TcpClient client, CancellationToken ct) {
            NetworkStream? stream = client.GetStream();
            byte[]? buffer = new byte[1024];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (ct.IsCancellationRequested) {
                    break;
                }

                callback(buffer, true);
            }
        }

        public void Stop() {
            running = false;
            cts.Cancel();
            listener?.Stop();
            listenTask?.Wait();
        }
    }
}