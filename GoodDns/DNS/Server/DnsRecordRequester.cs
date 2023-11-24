using System;
using System.Net;
using System.Net.Sockets;
using GoodDns.DNS;

namespace GoodDns.DNS.Server
{

    class Transaction
    {
        Logging<Transaction> logger = new Logging<Transaction>("./log.txt", logLevel: 5);
        public Packet packet;
        public IPEndPoint server;
        public DateTime lastUpdated;
        public bool isTCP = false;
        public ushort transactionId;
        UdpClient udpClient;

        //callback for when the transaction is complete
        public Action<Packet> callback;
        public Transaction(Packet packet, IPEndPoint server, Action<Packet> callback)
        {
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
            Packet responsePacket = new Packet();
    
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
        }

    }

    public class RecordRequester
    {
        Transaction[] transactions = new Transaction[10];

        public void RequestRecord(Packet packet, IPEndPoint server, Action<Packet> callback)
        {
            //create a new transaction
            Transaction transaction = new Transaction(packet, server, callback);
            //add the transaction to the transaction list
            transactions[0] = transaction;
        }

        public void Update()
        {
            //listen for responses
            foreach (Transaction transaction in transactions)
            {
                if (transaction != null)
                {
                    transaction.ReceiveResponse();
                }
            }
        }
    }
}