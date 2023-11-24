using System.Net.Sockets;

namespace GoodDns
{
    public class UniversalClient {
        TcpClient? tcpClient;
        UdpClient? udpClient;

        public UniversalClient(TcpClient? tcpClient = null, UdpClient? udpClient = null) {
            this.tcpClient = tcpClient;
            this.udpClient = udpClient;
        }

        public void Send(byte[] packet) {
            if(tcpClient != null) {
                tcpClient?.GetStream().Write(packet, 0, packet.Length);
            }
            if(udpClient != null) {
                udpClient?.Send(packet, packet.Length);
            }
        }
    }

    public class Server
    {
        public delegate void PacketHandler(byte[] packet, bool isTCP, UniversalClient client);
        PacketHandler _packetHandler;
        UDP udp;
        TCP tcp;
        public Server(PacketHandler _packetHandler)
        {
            this._packetHandler = _packetHandler;
            //create a new UDP server
            this.udp = new UDP(54321, packetHandler);
            this.tcp = new TCP(54321, packetHandler);
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
