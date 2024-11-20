using System;
using System.Net;
using PeakDNS.DNS;
using PeakDNS.DNS.Server;
using PeakDNS.Storage;

namespace PeakDNS
{
    class Program
    {
        static Settings settings = new Settings();
        static Logging<Program> logger = new Logging<Program>(settings.GetSetting("logging", "path", "./log.txt"), logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5")));
        static BIND[] zones;
        static void Init()
        {
            //read all files in zones directory
            string[] files = Directory.GetFiles(settings.GetSetting("requester", "path", "./zones"));
            //filter out non .zone files
            files = files.Where(file => file.EndsWith(".zone")).ToArray();
            //create a new array of BIND objects
            zones = new BIND[files.Length];
            //loop through each file
            for (int i = 0; i < files.Length; i++)
            {
                //create a new BIND object
                zones[i] = new BIND(files[i], settings);
                logger.Info("Loaded zone file: " + files[i]);
                zones[i].Print();
            }
        }

        static void Main(string[] args)
        {
            RecordRequester recordRequester = new RecordRequester(settings);
            Cache cache = new Cache(settings);

            Init();

            Server server = new Server((byte[] packet, bool isTCP, UniversalClient client) =>
            {

                logger.Success("Request callback invoked");
                Packet _packet = new Packet(settings);
                _packet.Load(packet, isTCP);
                _packet.Print();
                
                //check if the answer is in the cache
                if(cache.hasAnswer(_packet)) {
                    //get the answer from the cache
                    Answer[] answers = cache.getAnswers(_packet);
                    //set the answers
                    _packet.answers = answers;
                    //set the answer count
                    _packet.answerCount = (ushort)answers.Length;
                    //set the flags
                    _packet.flagpole.QR = true;
                    _packet.flagpole.AA = false;
                    _packet.flagpole.RA = true;
                    _packet.flagpole.RD = true;
                    _packet.flagpole.TC = false;
                    _packet.flagpole.NS = false;
                    _packet.flagpole.AD = false;
                    _packet.flagpole.CD = false;

                    _packet.flagpole.RCode = RCodes.NOERROR;

                    _packet.ToBytes(isTCP);
                    client.Send(_packet.packet);
                    client.Close();

                    return;
                }


                //loop trough all zones and check if one of them can answer the question
                foreach (BIND zone in zones)
                {
                    if (zone.canAnwser(_packet.questions[0]))
                    {
                        Answer[] answers = zone.getAnswers(_packet.questions[0]);
                        _packet.answers = answers;
                        _packet.answerCount = (ushort)answers.Length;
                        _packet.flagpole.QR = true;
                        _packet.flagpole.AA = false;
                        _packet.flagpole.RA = true;
                        _packet.flagpole.RD = true;
                        _packet.flagpole.TC = false;
                        _packet.flagpole.NS = false;
                        _packet.flagpole.AD = false;
                        _packet.flagpole.CD = false;

                        _packet.flagpole.RCode = RCodes.NOERROR;

                        _packet.ToBytes(isTCP);
                        client.Send(_packet.packet);
                        client.Close();

                        //add the answer to the cache
                        cache.addRecord(_packet);

                        return;
                    }
                    else
                    {
                        recordRequester.RequestRecord(_packet, new IPEndPoint(IPAddress.Parse(settings.GetSetting("requester", "server", "1.1.1.1")), 53), (Packet packet) =>
                        {
                            logger.Success("Response callback invoked");
                            packet.Print();
                            client.Send(packet.ToBytes(isTCP));

                            cache.addRecord(packet);

                            client.Close();

                            return;
                        });

                        return;
                    }
                }

            }, settings);

            server.Start();
            cache.Start();
            recordRequester.Start();

            while (true)
            {
                Thread.Sleep(Timeout.Infinite);
            }
        }
    }
}