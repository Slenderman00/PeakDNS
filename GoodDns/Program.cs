using System;

namespace GoodDns
{
    class Program
    {
        static void Main(string[] args)
        {
            Server server = new Server((byte[] packet, bool isTCP) =>
            {
                Console.WriteLine("Packet Received {0}", isTCP ? "TCP" : "UDP");
                //write the packet as hex
                Console.WriteLine(BitConverter.ToString(packet));
                DnsPacket _packet = new DnsPacket();
                _packet.Load(packet, isTCP);
                _packet.Print();
            });
            server.Start();

            while (true)
            {
                Console.WriteLine("Press any key to stop the server");
                Console.ReadLine();
                Console.WriteLine("Stopping the server");
                server.Stop();
                break;
            }
            Console.WriteLine("a key was pressed, exiting");
        }
    }
}