namespace GoodDns.Tests;
using System;
using System.Net;
using System.Net.Sockets;

using GoodDns;

[TestFixture]
public class Tests
{

    [SetUp]
    public void Setup()
    {
        
    }

    private DnsPacket GenerateDnsPacket() {
        //generate a dns packet
        DnsPacket packet = new DnsPacket();
        DnsQuestion question = new DnsQuestion("google.com", DnsQuestion.QType.A, DnsQuestion.QClass.IN);
        packet.AddQuestion(question);
        packet.SetTransactionId(0x1234);
        packet.AddFlag(DnsPacket.Flags.AA);

        return packet;
    }

    [Test]
    public void TestUdp()
    {
        ManualResetEvent callbackCalled = new ManualResetEvent(false);

        //create a server
        Server server = new Server((byte[] packet, bool isTCP) => {
            try {
                DnsPacket _packet = new DnsPacket();
                _packet.Load(packet, isTCP);
                _packet.Print();

                Assert.That(_packet.GetTransactionId(), Is.EqualTo(0x1234));
                Assert.That(_packet.GetFlags(), Is.EqualTo((ushort)DnsPacket.Flags.AA));
                Assert.That(_packet.GetQuestions().Count, Is.EqualTo(1));
                Assert.That(_packet.GetQuestions()[0].GetDomainName(), Is.EqualTo("google.com."));
                Assert.That(_packet.GetQuestions()[0].GetQType(), Is.EqualTo((ushort)DnsQuestion.QType.A));
                Assert.That(_packet.GetQuestions()[0].GetQClass(), Is.EqualTo((ushort)DnsQuestion.QClass.IN));

                callbackCalled.Set();
            } catch(Exception e) {
                Console.WriteLine(e);
            }
        });
        server.Start();

        //create a new UDP client
        UdpClient client = new UdpClient();
        //create a new IPEndPoint
        IPEndPoint ip = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 54321);

        DnsPacket packet = GenerateDnsPacket();

        //convert the packet to a byte array
        byte[] packetBytes = packet.ToBytes();

        //send the packet to the server
        client.Send(packetBytes, packetBytes.Length, ip);

        bool success = callbackCalled.WaitOne(10000);

        //close the client
        client.Close();
        server.Stop();

        Assert.IsTrue(success, "Callback was not called");
    }

    [Test]
    public void TestTcp() {
        ManualResetEvent callbackCalled = new ManualResetEvent(false);

        //create a server
        Server server = new Server((byte[] packet, bool isTCP) => {
            try {
                DnsPacket _packet = new DnsPacket();
                _packet.Load(packet, isTCP);
                _packet.Print();

                Assert.That(_packet.GetTransactionId(), Is.EqualTo(0x1234));
                Assert.That(_packet.GetFlags(), Is.EqualTo((ushort)DnsPacket.Flags.AA));
                Assert.That(_packet.GetQuestions().Count, Is.EqualTo(1));
                Assert.That(_packet.GetQuestions()[0].GetDomainName(), Is.EqualTo("google.com."));
                Assert.That(_packet.GetQuestions()[0].GetQType(), Is.EqualTo((ushort)DnsQuestion.QType.A));
                Assert.That(_packet.GetQuestions()[0].GetQClass(), Is.EqualTo((ushort)DnsQuestion.QClass.IN));

                callbackCalled.Set();
            } catch(Exception e) {
                Console.WriteLine(e);
            }
        });
        server.Start();

        //create a new TCP client
        TcpClient client = new TcpClient();
        //create a new IPEndPoint
        IPEndPoint ip = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 54321);

        DnsPacket packet = GenerateDnsPacket();

        //convert the packet to a byte array
        byte[] packetBytes = packet.ToBytes(isTCP: true);
        //send the packet to the server

        client.Connect(ip);
        NetworkStream stream = client.GetStream();
        stream.Write(packetBytes, 0, packetBytes.Length);
        
        //check if the packet was sent
        bool success = callbackCalled.WaitOne(10000);

        //close the client and server
        client.Close();
        server.Stop();

        Assert.IsTrue(success, "Callback was not called");
    }
}