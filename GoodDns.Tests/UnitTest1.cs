namespace GoodDns.Tests;
using System;
using System.Net;
using System.Net.Sockets;

using GoodDns;
using GoodDns.DNS;
using GoodDns.DNS.Server;

[TestFixture]
public class Tests
{

    [SetUp]
    public void Setup()
    {
        
    }

    private Packet GenerateDnsPacket() {
        //generate a dns packet
        Packet packet = new Packet();
        Question question = new Question("example.com", RTypes.A, RClasses.IN);
        packet.AddQuestion(question);
        packet.SetTransactionId(0x1234);
        packet.flagpole.AA = true;

        return packet;
    }

    [Test]
    public void TestUdp()
    {
        ManualResetEvent callbackCalled = new ManualResetEvent(false);

        //create a server
        Server server = new Server((byte[] packet, bool isTCP) => {
            try {
                Packet _packet = new Packet();
                _packet.Load(packet, isTCP);
                _packet.Print();

                Assert.That(_packet.GetTransactionId(), Is.EqualTo(0x1234));
                Assert.That(_packet.flagpole.AA, Is.EqualTo(true));
                Assert.That(_packet.GetQuestions().Count, Is.EqualTo(1));
                Assert.That(_packet.GetQuestions()[0].GetDomainName(), Is.EqualTo("example.com."));
                Assert.That(_packet.GetQuestions()[0].GetQType(), Is.EqualTo((ushort)RTypes.A));
                Assert.That(_packet.GetQuestions()[0].GetQClass(), Is.EqualTo((ushort)RClasses.IN));

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

        Packet packet = GenerateDnsPacket();

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
                Packet _packet = new Packet();
                _packet.Load(packet, isTCP);
                _packet.Print();

                Assert.That(_packet.GetTransactionId(), Is.EqualTo(0x1234));
                Assert.That(_packet.flagpole.AA, Is.EqualTo(true));
                Assert.That(_packet.GetQuestions().Count, Is.EqualTo(1));
                Assert.That(_packet.GetQuestions()[0].GetDomainName(), Is.EqualTo("example.com."));
                Assert.That(_packet.GetQuestions()[0].GetQType(), Is.EqualTo((ushort)RTypes.A));
                Assert.That(_packet.GetQuestions()[0].GetQClass(), Is.EqualTo((ushort)RClasses.IN));

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

        Packet packet = GenerateDnsPacket();

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

    [Test]
    public void TestBIND() {
        BIND bind = new BIND("/../../../../test.zone");
        Assert.That(bind.records.Count, Is.EqualTo(13));
    }
}