using System;
using System.Net;
using System.Net.Sockets;
using PeakDNS.DNS;
using System.Threading.Tasks;

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
        Transaction[] transactions = new Transaction[100];
        Settings settings;
        Task updateTask;

        public RecordRequester(Settings settings)
        {
            this.settings = settings;
        }

        public void RequestRecord(Packet packet, IPEndPoint server, Action<Packet> callback)
        {
            //create a new transaction
            Transaction transaction = new Transaction(packet, server, callback, settings);
            //add the transaction to an empty slot in the transactions array
            for (int i = 0; i < transactions.Length; i++)
            {
                if (transactions[i] == null || transactions[i].isComplete)
                {
                    transactions[i] = transaction;
                    break;
                }
            }
        }

        public void Update()
        {
            //listen for responses from the server
            for (int i = 0; i < transactions.Length; i++)
            {
                if (transactions[i] != null && !transactions[i].isComplete)
                {
                    transactions[i].ReceiveResponse();
                }
            }
        }

        public void Start()
        {
            updateTask = Task.Run(() => {
                while(true) {
                    Update();
                }
            });
        }

        public void Stop()
        {
            updateTask.Dispose();
        }
    }
}