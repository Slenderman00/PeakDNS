using System.Net;
using System.Net.Sockets;
using System;
using System.Threading.Tasks;

namespace PeakDNS {
    class TCP {
        Settings settings;
        Logging<TCP> logger;
        TcpListener? listener;
        Task? listenTask;
        bool running = false;
        Task[] clientPool;
        CancellationTokenSource cts = new CancellationTokenSource();

        public delegate void PacketHandlerCallback(byte[] packet, bool isTCP, UniversalClient client);
        PacketHandlerCallback callback;
        public TCP(int port, PacketHandlerCallback packetHandler, Settings settings) {
            this.settings = settings;
            logger = new Logging<TCP>(settings.GetSetting("logging", "path", "./log.txt"), logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5")));
            listener = new TcpListener(IPAddress.Any, port);
            clientPool = new Task[int.Parse(settings.GetSetting("server", "tcpThreads", "10"))];
            callback = packetHandler;
        }

        public void Start() {
            logger.Info("Starting TCP server");
            this.running = true;
            listener?.Start();
            
            listenTask = Task.Run(() => {
                try {
                    while (running && !cts.Token.IsCancellationRequested) {
                        var client = listener?.AcceptTcpClient();
                        if (client != null) {
                            logger.Info("Client connected from: " + client.Client.RemoteEndPoint);
                            assignTask(client, cts.Token);
                        }
                    }
                } catch (Exception ex) {
                    if (running && !cts.Token.IsCancellationRequested) {
                        logger.Error($"Error: {ex.Message}");
                        logger.Error($"Stack trace: {ex.StackTrace}");
                    }
                } finally {
                    Task.WaitAll(Array.FindAll(clientPool, task => task != null));
                    listener?.Stop();
                }
            });
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
            try {
                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (ct.IsCancellationRequested) {
                        break;
                    }

                    callback(buffer, true, new UniversalClient(tcpClient: client));
                }
            } catch(System.IO.IOException e) {
                logger.Warning($"IOException: {e.Message}");
            } catch(ObjectDisposedException e) {
                logger.Warning($"ObjectDisposedException: {e.Message}");
            }
        }

        public void Stop() {
            logger.Info("Stopping TCP server");
            running = false;
            cts.Cancel();
            listener?.Stop();
            listenTask?.Wait();
        }
    }
}