namespace GoodDns
{
    public class Server
    {
        public delegate void PacketHandler(byte[] packet, bool isTCP);
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

        void packetHandler(byte[] packet, bool isTCP)
        {
            _packetHandler(packet, isTCP);
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
