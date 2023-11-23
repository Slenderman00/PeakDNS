using System;
using System.Net;
using GoodDns.DNS;
using GoodDns.DNS.Server;

namespace GoodDns
{
    class Program
    {
        static Logging<Program> logger = new Logging<Program>("./log.txt", logLevel: 5);
        static BIND[] zones;
        static void Init() {
            //read all files in zones directory
            string[] files = Directory.GetFiles("./zones");
            //filter out non .zone files
            files = files.Where(file => file.EndsWith(".zone")).ToArray();
            //create a new array of BIND objects
            zones = new BIND[files.Length];
            //loop through each file
            for(int i = 0; i < files.Length; i++) {
                //create a new BIND object
                zones[i] = new BIND(files[i]);
                logger.Info("Loaded zone file: " + files[i]);
            }
        }

        static void Main(string[] args)
        {
            RecordRequester recordRequester = new RecordRequester();

            Init();

            Server server = new Server((byte[] packet, bool isTCP, universalClient client) =>
            {
                logger.Success("Request callback invoked");
                //write the packet as hex
                //logger.Debug(BitConverter.ToString(packet).Replace("-", " "));
                Packet _packet = new Packet();
                _packet.Load(packet, isTCP);
                _packet.Print();

                //loop trough all zones and check if one of them can answer the question
                foreach(BIND zone in zones) {
                    if(zone.canAnwser(_packet.questions[0])) {
                        Answer[] answers = zone.getAnswers(_packet.questions[0]);
                        _packet.answers = answers;
                        _packet.answerCount = (ushort)answers.Length;
                        _packet.flagpole.QR = true;
                        _packet.flagpole.AA = true;
                        _packet.flagpole.RA = true;
                        _packet.flagpole.RD = false;
                        _packet.flagpole.TC = false;

                        _packet.flagpole.RCode = RCodes.NOERROR;

                        //remove the question
                        _packet.questions = null;
                        _packet.questionCount = 0;

                        _packet.ToBytes(isTCP);

                        client.Send(_packet.packet);
                    }
                }

                //PRINT THE PACKET
                logger.Debug("Response packet");
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
        }
    }
}