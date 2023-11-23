using System;
using System.Net;
using GoodDns.DNS;
using GoodDns.DNS.Server;

namespace GoodDns
{
    class Program
    {
        static Logging<Program> logger = new Logging<Program>("./log.txt", logLevel: 5);
        static void Main(string[] args)
        {
            RecordRequester recordRequester = new RecordRequester();

            Server server = new Server((byte[] packet, bool isTCP) =>
            {
                logger.Success("Request callback invoked");
                //write the packet as hex
                //logger.Debug(BitConverter.ToString(packet).Replace("-", " "));
                Packet _packet = new Packet();
                _packet.Load(packet, isTCP);
                _packet.Print();
                recordRequester.RequestRecord(_packet, new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53), (Packet packet) =>
                {
                    logger.Success("Response callback invoked");
                    packet.Print();
                });
                recordRequester.Update();
            });
            server.Start();

            while (true)
            {
                logger.Info("Enter 'stop' to stop the server");
                Console.ReadLine();
                logger.Info("Stopping server");
                server.Stop();
                break;
            }
            BIND bind = new BIND("./test.zone");
        }
    }
}