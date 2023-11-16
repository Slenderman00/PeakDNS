using System.Net;

namespace GoodDns {
    public class DnsQuestion {
        public string domainName;
        public ushort questionType;
        public ushort questionClass;

        //add question types:
        //A: 1
        //AAAA: 28
        //CNAME: 5
        //MX: 15
        //NS: 2
        //PTR: 12
        //SOA: 6
        //SRV: 33
        //TXT: 16
        public enum QType : ushort {
            A = 1,
            AAAA = 28,
            CNAME = 5,
            MX = 15,
            NS = 2,
            PTR = 12,
            SOA = 6,
            SRV = 33,
            TXT = 16
        }

        //add question classes:
        //IN: 1
        //CS: 2
        //CH: 3
        //HS: 4
        public enum QClass : ushort {
            IN = 1,
            CS = 2,
            CH = 3,
            HS = 4
        }

        public DnsQuestion(string domainName, QType qType, QClass qClass) {
            this.domainName = domainName;
            this.questionType = (ushort)qType;
            this.questionClass = (ushort)qClass;
        }

        public void Print() {
            //print the packet
            Console.WriteLine("Domain Name: " + domainName);
            Console.WriteLine("Question Type: " + questionType);
            Console.WriteLine("Question Class: " + questionClass);
        }
        public string GetDomainName() {
            //get the domain name
            return domainName;
        }

        public ushort GetQType() {
            //get the question type
            return questionType;
        }

        public ushort GetQClass() {
            //get the question class
            return questionClass;
        }
    }
    public class DnsPacket {
        public byte[] packet;

        ushort transactionId;

        ushort flags;

        ushort questionCount = 0;

        ushort answerCount = 0;

        string[]? answers;
        DnsQuestion[]? questions;

        //add flags:
        //QR: bit 15
        //opcode: bits 14-11
        //AA: bit 10
        //TC: bit 9
        //RD: bit 8
        //RA: bit 7
        //AD: bit 5
        //CD: bit 4
        //RCODE: bits 3-0

        public enum Flags : ushort {
            QR = 1 << 15,
            AA = 1 << 10,
            TC = 1 << 9,
            RD = 1 << 8,
            RA = 1 << 7,
            AD = 1 << 5,
            CD = 1 << 4
        }

        public DnsPacket() {

        }

        public void Load(byte[] packet, bool isTCP) {
            this.packet = packet;
            
            if(!isTCP) {
                ParseUDP();
                Console.WriteLine("UDP");
            } else {
                ParseTCP();
                Console.WriteLine("TCP");
            }
        }

        private void ParseUDP() {
            int currentPosition = 0;
            transactionId = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            currentPosition += 2;

            flags = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            currentPosition += 2;

            questionCount = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            currentPosition += 2;

            answerCount = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            currentPosition += 2;

            currentPosition += 4;

            //get questions and answers
            GetQuestions(ref currentPosition);
            GetAnwsers(ref currentPosition);
        }

        private void ParseTCP() {
            //length is stored in the first two bytes of the tcp packet
            int DNSPacketLength = (packet[0] << 8) | packet[1];

            //merging the first two bytes into a ushort
            int currentPosition = 2;
            transactionId = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            currentPosition += 2;

            flags = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            currentPosition += 2;

            questionCount = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            currentPosition += 2;

            answerCount = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            currentPosition += 2;

            currentPosition += 4;
            //get questions and answers
            GetQuestions(ref currentPosition);
            GetAnwsers(ref currentPosition);
        }

        private void GetQuestions(ref int currentPosition) {
            //get questions and answers
            questions = new DnsQuestion[questionCount];
            for(int i = 0; i < questionCount; i++) {
                //get domain name offset
                string domainName = ReadDomainName(ref currentPosition);

                //parse the question type
                ushort questionType = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
                currentPosition += 2;

                //parse the question class
                ushort questionClass = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
                currentPosition += 2;

                questions[i] = new DnsQuestion(domainName, (DnsQuestion.QType)questionType, (DnsQuestion.QClass)questionClass);
            }
        }

        private void GetAnwsers(ref int currentPosition) {
            //get answers
            answers = new string[answerCount];
            for(int i = 0; i < answerCount; i++) {
                //get domain name offset
                string domainName = ReadDomainName(ref currentPosition);

                //parse the question type
                ushort questionType = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
                currentPosition += 2;

                //parse the question class
                ushort questionClass = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
                currentPosition += 2;

                //parse the time to live
                uint timeToLive = (uint)((packet[currentPosition] << 24) | (packet[currentPosition + 1] << 16) | (packet[currentPosition + 2] << 8) | packet[currentPosition + 3]);
                currentPosition += 4;

                //parse the data length
                ushort dataLength = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
                currentPosition += 2;

                //parse the data
                string data = "";
                for(int j = 0; j < dataLength; j++) {
                    data += (char)packet[currentPosition];
                    currentPosition++;
                }

                answers[i] = data;
            }
        }

        private string ReadDomainName(ref int currentPosition)
        {
            //read the domain name
            string domainName = "";
            while (packet[currentPosition] != 0)
            {
                int domainNameLength = packet[currentPosition];
                currentPosition++;
                for (int i = 0; i < domainNameLength; i++)
                {
                    domainName += (char)packet[currentPosition];
                    currentPosition++;
                }
                domainName += ".";
            }
            currentPosition++;
            return domainName;
        }

        public void AddQuestion(DnsQuestion question) {
            //add a question to the packet
            questionCount++;
            //if questions is null, create a new array
            if(questions == null) {
                questions = new DnsQuestion[questionCount];
                questions[0] = question;
            } else {
                //create a new array with the new size
                DnsQuestion[] newQuestions = new DnsQuestion[questionCount];
                //copy the old array into the new array
                for(int i = 0; i < questions.Length; i++) {
                    newQuestions[i] = questions[i];
                }
                //set the new array to the old array
                questions = newQuestions;
            }
        }

        public DnsQuestion[] GetQuestions() {
            //get the questions
            return questions;
        }

        public ushort GetQuestionCount() {
            //get the question count
            return questionCount;
        }

        public void AddAnswer(string answer) {
            //add an answer to the packet

            answerCount++;
            //if answers is null, create a new array
        }

        public void AddFlag(Flags flag) {
            //add a flag to the packet
            flags |= (ushort)flag;
        }

        public ushort GetFlags() {
            //get the flags
            return flags;
        }

        public void SetTransactionId(ushort transactionId) {
            //set the transaction id

            this.transactionId = transactionId;
        }

        public ushort GetTransactionId() {
            //get the transaction id

            return transactionId;
        }

        private byte[] ToBytesUDP() {
            byte[] packet = new byte[512];

            int currentPosition = 0;

            //add the transaction id
            packet[currentPosition] = (byte)(transactionId >> 8);
            packet[currentPosition + 1] = (byte)(transactionId & 0xFF);
            currentPosition += 2;

            //add the flags
            packet[currentPosition] = (byte)(flags >> 8);
            packet[currentPosition + 1] = (byte)(flags & 0xFF);
            currentPosition += 2;

            //add the question count
            packet[currentPosition] = (byte)(questionCount >> 8);
            packet[currentPosition + 1] = (byte)(questionCount & 0xFF);
            currentPosition += 2;

            //add the answer count
            packet[currentPosition] = (byte)(answerCount >> 8);
            packet[currentPosition + 1] = (byte)(answerCount & 0xFF);
            currentPosition += 6;

            //add the questions
            for(int i = 0; i < questionCount; i++) {
                AddQuestion(ref packet, ref currentPosition, ref questions[i]);
            }

            //add the answers
            for(int i = 0; i < answerCount; i++) {
                AddAnswer();
            }

            byte[] shortenedPacket = new byte[currentPosition];
            for(int i = 0; i < currentPosition; i++) {
                shortenedPacket[i] = packet[i];
            }

            this.packet = shortenedPacket;

            return shortenedPacket;
        }

        private byte[] ToBytesTCP() {
            //generate a packet
            byte[] packet = new byte[1024];

            int currentPosition = 2;

            //add the transaction id
            packet[currentPosition] = (byte)(transactionId >> 8);
            packet[currentPosition + 1] = (byte)(transactionId & 0xFF);
            currentPosition += 2;

            //add the flags
            packet[currentPosition] = (byte)(flags >> 8);
            packet[currentPosition + 1] = (byte)(flags & 0xFF);
            currentPosition += 2;

            //add the question count
            packet[currentPosition] = (byte)(questionCount >> 8);
            packet[currentPosition + 1] = (byte)(questionCount & 0xFF);
            currentPosition += 2;

            //add the answer count
            packet[currentPosition] = (byte)(answerCount >> 8);
            packet[currentPosition + 1] = (byte)(answerCount & 0xFF);
            currentPosition += 6;

            //add the questions
            for(int i = 0; i < questionCount; i++) {
                AddQuestion(ref packet, ref currentPosition, ref questions[i]);
            }

            //add the answers
            for(int i = 0; i < answerCount; i++) {
                AddAnswer();
            }

            this.packet = packet;

            return packet;
        }

        public byte[] ToBytes(bool isTCP = false) {
            //call the correct function to convert the packet to bytes
            if(isTCP) {
                return ToBytesTCP();
            } else {
                return ToBytesUDP();
            }
        }

        private void AddQuestion(ref byte[] packet, ref int currentPosition, ref DnsQuestion question) {
            //add a question to the packet

            //add the domain name
            string domainName = question.domainName;
            string[] domainNameParts = domainName.Split('.');
            for(int j = 0; j < domainNameParts.Length; j++) {
                packet[currentPosition] = (byte)domainNameParts[j].Length;
                currentPosition++;
                for(int k = 0; k < domainNameParts[j].Length; k++) {
                    packet[currentPosition] = (byte)domainNameParts[j][k];
                    currentPosition++;
                }
            }
            packet[currentPosition] = 0;
            currentPosition++;

            //add the question type
            packet[currentPosition] = (byte)(question.questionType >> 8);
            packet[currentPosition + 1] = (byte)(question.questionType & 0xFF);
            currentPosition += 2;

            //add the question class
            packet[currentPosition] = (byte)(question.questionClass >> 8);
            packet[currentPosition + 1] = (byte)(question.questionClass & 0xFF);
            currentPosition += 2;
        }

        private void AddAnswer() {
            //add an answer to the packet
        }

        public void Print() {
            //print the packet
            Console.WriteLine("Transaction ID: " + transactionId);
            Console.WriteLine("Flags: " + flags);
            Console.WriteLine("Question Count: " + questionCount);
            Console.WriteLine("Answer Count: " + answerCount);
            for(int i = 0; i < questionCount; i++) {
                Console.WriteLine("Question: " + questions[i].domainName);
            }
            for(int i = 0; i < answerCount; i++) {
                Console.WriteLine("Answer: " + answers[i]);
            }
        }
    }
}