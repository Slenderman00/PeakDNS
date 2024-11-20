using System;
using System.Net;
using System.Net.Sockets;
using PeakDNS.DNS;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

namespace PeakDNS.DNS.Server
{

    class Transaction
    {
        Settings settings;
        Logging<Transaction> logger;
        public Packet packet;
        public IPEndPoint server;
        public DateTime lastUpdated;
        public bool isTCP = false;
        public ushort transactionId;
        UdpClient udpClient;
        
        public bool isComplete = false;

        //callback for when the transaction is complete
        public Action<Packet> callback;
        public Transaction(Packet packet, IPEndPoint server, Action<Packet> callback, Settings settings)
        {
            this.settings = settings;
            this.logger = new Logging<Transaction>(settings.GetSetting("logging", "path", "./log.txt"), logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5")));

            //set the packet
            this.packet = packet;
            //set the transaction id to the packet's transaction id
            this.transactionId = packet.GetTransactionId();
            this.server = server;
            this.lastUpdated = DateTime.Now;
            this.callback = callback;

            //log the transaction
            logger.Info($"Created transaction with id {transactionId} with endpoint {server}");

            //create a new udp client
            this.udpClient = new UdpClient();

            //send the packet
            this.packet.ToBytes();
            //logger.Debug(BitConverter.ToString(this.packet.packet).Replace("-", " "));
            udpClient.Send(packet.packet, packet.packet.Length, server);
        }

        //add a method to receive responses
        public void ReceiveResponse()
        {
            IPEndPoint responseEndPoint = null;
            byte[] responseData = udpClient.Receive(ref responseEndPoint);
            //print response data

            //process the response data, create a new packet, etc.
            Packet responsePacket = new Packet(settings);
    
            responsePacket.Load(responseData, false);

            //re-Generate packet, remove this, this is only for illustrative purposes
            responsePacket.ToBytes();
            responsePacket.Load(responsePacket.packet, false);

            //log the response
            logger.Success($"Received response with id {responsePacket.GetTransactionId()} from endpoint {responseEndPoint}");

            //invoke the callback with the response packet
            callback(responsePacket);

            //close the UDP client after receiving the response
            udpClient.Close();

            //set the transaction to complete
            isComplete = true;
        }

    }

    public class RecordRequester
    {
        private List<Transaction> transactions;
        private Settings settings;
        private Thread updateThread;
        private volatile bool shouldStop;
        private SemaphoreSlim concurrencyLimit;

        public RecordRequester(Settings settings, int maxConcurrentRequests = 50)
        {
            this.settings = settings;
            transactions = new List<Transaction>();
            concurrencyLimit = new SemaphoreSlim(maxConcurrentRequests, maxConcurrentRequests);
        }

        public void RequestRecord(Packet packet, IPEndPoint server, Action<Packet> callback)
        {
            // Acquire a permit from the concurrency limit
            concurrencyLimit.Wait();

            try
            {
                // Create a new transaction
                Transaction transaction = new Transaction(packet, server, callback, settings);

                // Add the transaction to the list
                lock (transactions)
                {
                    transactions.Add(transaction);
                }

                // Start the transaction
                transaction.Start();
            }
            finally
            {
                // Release the permit back to the concurrency limit
                concurrencyLimit.Release();
            }
        }

        private void Update()
        {
            while (!shouldStop)
            {
                // Check for completed transactions
                lock (transactions)
                {
                    for (int i = 0; i < transactions.Count; i++)
                    {
                        if (transactions[i].IsComplete)
                        {
                            transactions.RemoveAt(i);
                            i--;
                        }
                    }
                }

                // Sleep for a short time (e.g., 10 milliseconds)
                Thread.Sleep(10);
            }
        }

        public void Start()
        {
            shouldStop = false;
            updateThread = new Thread(Update);
            updateThread.Start();
        }

        public void Stop()
        {
            shouldStop = true;
            updateThread.Join();
        }
    }
}