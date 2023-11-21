using System;
using System.Net;
using GoodDns.DNS;
using GoodDns.DNS.Server;

namespace GoodDns
{
    class Program
    {
        static void Main(string[] args)
        {
            RecordRequester recordRequester = new RecordRequester();

            Server server = new Server((byte[] packet, bool isTCP) =>
            {
                Console.WriteLine("Packet Received {0}", isTCP ? "TCP" : "UDP");
                //write the packet as hex
                Console.WriteLine(BitConverter.ToString(packet).Replace("-", " "));
                Packet _packet = new Packet();
                _packet.Load(packet, isTCP);
                _packet.Print();
                recordRequester.RequestRecord(_packet, new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53), (Packet packet) =>
                {
                    Console.WriteLine("Got response from 1.1.1.1");
                    packet.Print();
                });
                recordRequester.Update();
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
        }
    }
}