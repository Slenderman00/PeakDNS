using System.Net.Sockets;
using System.Net;

namespace GoodDns
{
    public class UniversalClient {
        TcpClient? tcpClient;
        UdpClient? udpClient;
        IPEndPoint? clientEndPoint;

        public UniversalClient(TcpClient? tcpClient = null, UdpClient? udpClient = null, IPEndPoint? clientEndPoint = null) {
            this.tcpClient = tcpClient;
            this.udpClient = udpClient;
            this.clientEndPoint = clientEndPoint;
        }

        public void Send(byte[] packet) {
            if(tcpClient != null) {
                tcpClient?.GetStream().Write(packet, 0, packet.Length);
            }
            if(udpClient != null) {
                udpClient?.Send(packet, packet.Length, this.clientEndPoint);
            }
        }

        public void Close() {
            if(tcpClient != null) {
                tcpClient.Close();
            }
        }
    }

    public class Server
    {
        public delegate void PacketHandler(byte[] packet, bool isTCP, UniversalClient client);
        PacketHandler _packetHandler;
        UDP udp;
        TCP tcp;
        Settings settings;
        public Server(PacketHandler _packetHandler, Settings settings)
        {
            this.settings = settings;
            this._packetHandler = _packetHandler;
            //create a new UDP server
            this.udp = new UDP(int.Parse(settings.GetSetting("server", "port", "54321")), packetHandler, settings);
            this.tcp = new TCP(int.Parse(settings.GetSetting("server", "port", "54321")), packetHandler, settings);
        }

        void packetHandler(byte[] packet, bool isTCP, UniversalClient client)
        {
            _packetHandler(packet, isTCP, client);
        }

        public void Start()
        {
            //start the server
            this.udp.Start();
            this.tcp.Start();
        }

        public void Stop()
        {
            //stop the server
            this.udp.Stop();
            this.tcp.Stop();
        }
    }
}
