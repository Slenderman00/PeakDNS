using System;
using System.Net;
using System.Collections.Generic;
using PeakDNS.DNS;
using PeakDNS.DNS.Server;
using PeakDNS.Storage;
using PeakDNS.Kubernetes;

namespace PeakDNS
{
    class Program
    {
        static Settings settings = new Settings();
        static Logging<Program> logger = new Logging<Program>(
            settings.GetSetting("logging", "path", "./log.txt"),
            logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5"))
        );
        static List<BIND> zones = new List<BIND>();

        static void Init()
        {
            try
            {
                // Read all files in zones directory
                string zonePath = settings.GetSetting("requester", "path", "./zones");
                if (Directory.Exists(zonePath))
                {
                    string[] files = Directory.GetFiles(zonePath, "*.zone");
                    foreach (string file in files)
                    {
                        try
                        {
                            var zone = new BIND(file, settings);
                            zones.Add(zone);
                            logger.Info($"Loaded zone file: {file}");
                            zone.Print();
                        }
                        catch (Exception ex)
                        {
                            logger.Error($"Failed to load zone file {file}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    logger.Warning($"Zone directory not found: {zonePath}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error initializing zones: {ex.Message}");
            }
        }

        static void Main(string[] args)
        {
            try
            {
                logger.Info($"Starting DNS server on {settings.GetSetting("server", "bind", "0.0.0.0")}:{settings.GetSetting("server", "port", "53")}");

                var provider = new DynamicRecords(settings);
                var recordRequester = new RecordRequester(settings);
                var cache = new Cache(settings);

                Init();

                zones.Add(provider.bind);
                logger.Info($"Added Kubernetes provider zone. Total zones: {zones.Count}");

                Server server = new Server((byte[] packet, bool isTCP, UniversalClient client) =>
                {
                    try
                    {
                        logger.Debug($"Received DNS request - TCP: {isTCP}, Size: {packet.Length} bytes");
                        HandleDNSRequest(packet, isTCP, client, cache, recordRequester);
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error handling DNS request: {ex}");
                        SendErrorResponse(client, packet, isTCP);
                    }
                }, settings);

                server.Start();
                provider.Start();
                cache.Start();
                recordRequester.Start();

                logger.Info("DNS Server started successfully");

                while (true)
                {
                    Thread.Sleep(Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Fatal error in main program: {ex}");
                throw;
            }
        }

        private static void HandleDNSRequest(byte[] packetData, bool isTCP, UniversalClient client,
            Cache cache, RecordRequester recordRequester)
        {
            logger.Info($"=== Starting DNS Request Processing ===");
            logger.Debug($"Received packet size: {packetData.Length}");

            Packet packet = new Packet(settings);
            packet.Load(packetData, isTCP);
            packet.Print();

            if (cache.HasAnswer(packet))
            {
                logger.Info("Cache hit - sending cached response");
                SendCachedResponse(packet, cache, client, isTCP);
                return;
            }

            logger.Debug($"Checking {zones.Count} zones for answers");
            foreach (BIND zone in zones)
            {
                logger.Debug($"Checking zone with origin: {zone.origin}");
                if (zone.canAnwser(packet.questions[0]))
                {
                    logger.Info($"Found answering zone: {zone.origin}");
                    SendZoneResponse(zone, packet, cache, client, isTCP);
                    return;
                }
            }

            logger.Info("No local zone can answer - forwarding to upstream DNS");
            ForwardToUpstreamDNS(packet, recordRequester, cache, client, isTCP);
        }

        private static void SendCachedResponse(Packet packet, Cache cache, UniversalClient client, bool isTCP)
        {
            try
            {
                logger.Debug("Preparing cached response");
                Answer[] answers = cache.GetAnswers(packet);
                logger.Info($"Sending {answers.Length} cached answers");
                SendResponse(packet, answers, client, isTCP);
            }
            catch (Exception ex)
            {
                logger.Error($"Error sending cached response: {ex}");
                throw;
            }
        }

        private static void SendZoneResponse(BIND zone, Packet packet, Cache cache, UniversalClient client, bool isTCP)
        {
            try
            {
                logger.Debug($"Getting answers from zone {zone.origin}");
                Answer[] answers = zone.getAnswers(packet.questions[0]);

                if (answers != null && answers.Length > 0)
                {
                    logger.Info($"Sending {answers.Length} answers from zone");
                    SendResponse(packet, answers, client, isTCP);
                    cache.AddRecord(packet);
                }
                else
                {
                    logger.Info("Zone returned no answers - sending NXDOMAIN");
                    SendNXDomainResponse(client, packet, isTCP);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error sending zone response: {ex}");
                throw;
            }
        }

        private static void SendResponse(Packet packet, Answer[] answers, UniversalClient client, bool isTCP)
        {
            try
            {
                logger.Debug("Preparing DNS response packet");
                packet.answers = answers;
                packet.answerCount = (ushort)answers.Length;
                packet.flagpole.QR = true;
                packet.flagpole.AA = true;  // This is our authoritative answer
                packet.flagpole.RA = true;
                packet.flagpole.RD = true;
                packet.flagpole.TC = false;
                packet.flagpole.NS = false;
                packet.flagpole.AD = false;
                packet.flagpole.CD = false;
                packet.flagpole.RCode = RCodes.NOERROR;

                byte[] responseData = packet.ToBytes(isTCP);
                logger.Debug($"Response packet size: {responseData.Length} bytes");

                logger.Info("Sending response to client");
                client.Send(responseData);
                logger.Debug("Response sent successfully");

                client.Close();
                logger.Debug("Client connection closed");
            }
            catch (Exception ex)
            {
                logger.Error($"Error sending response: {ex}", ex);
                throw;
            }
        }

        private static void SendNXDomainResponse(UniversalClient client, Packet packet, bool isTCP)
        {
            try
            {
                logger.Debug("Preparing NXDOMAIN response");
                packet.flagpole.QR = true;
                packet.flagpole.RCode = RCodes.NXDOMAIN;

                byte[] responseData = packet.ToBytes(isTCP);
                logger.Debug($"NXDOMAIN response size: {responseData.Length} bytes");

                client.Send(responseData);
                logger.Debug("NXDOMAIN response sent");

                client.Close();
            }
            catch (Exception ex)
            {
                logger.Error($"Error sending NXDOMAIN response: {ex}");
                throw;
            }
        }

        private static void SendErrorResponse(UniversalClient client, byte[] originalPacket, bool isTCP)
        {
            try
            {
                logger.Debug("Preparing error response");
                Packet errorPacket = new Packet(settings);
                errorPacket.Load(originalPacket, isTCP);
                errorPacket.flagpole.QR = true;
                errorPacket.flagpole.RCode = RCodes.SERVFAIL;

                byte[] responseData = errorPacket.ToBytes(isTCP);
                logger.Debug($"Error response size: {responseData.Length} bytes");

                client.Send(responseData);
                logger.Debug("Error response sent");
            }
            catch (Exception ex)
            {
                logger.Error($"Error sending error response: {ex}");
            }
            finally
            {
                try
                {
                    client.Close();
                    logger.Debug("Client connection closed");
                }
                catch (Exception ex)
                {
                    logger.Error($"Error closing client connection: {ex}");
                }
            }
        }

        private static void ForwardToUpstreamDNS(Packet packet, RecordRequester recordRequester,
            Cache cache, UniversalClient client, bool isTCP)
        {
            try
            {
                string upstreamIP = settings.GetSetting("requester", "server", "1.1.1.1");
                logger.Info($"Forwarding to upstream DNS server: {upstreamIP}");

                var upstreamServer = new IPEndPoint(IPAddress.Parse(upstreamIP), 53);

                recordRequester.RequestRecord(packet, upstreamServer, (Packet response) =>
                {
                    try
                    {
                        logger.Info("Received upstream DNS response");
                        response.Print();

                        byte[] responseData = response.ToBytes(isTCP);
                        logger.Debug($"Upstream response size: {responseData.Length} bytes");

                        client.Send(responseData);
                        logger.Debug("Upstream response forwarded to client");

                        cache.AddRecord(response);
                        logger.Debug("Response cached");

                        client.Close();
                        logger.Debug("Client connection closed");
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error processing upstream response: {ex}");
                        SendErrorResponse(client, packet.packet, isTCP);
                    }
                });
            }
            catch (Exception ex)
            {
                logger.Error($"Error forwarding to upstream DNS: {ex}");
                throw;
            }
        }
    }
}