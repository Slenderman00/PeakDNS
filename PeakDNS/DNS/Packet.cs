using System.Net;

namespace PeakDNS.DNS
{
    public class Packet
    {
        Settings settings;
        static Logging<Packet> logger;

        public byte[] packet;

        ushort transactionId;

        public ushort questionCount = 0;

        public ushort answerCount = 0;

        public Answer[]? answers;
        public Question[]? questions;

        public Flagpole flagpole;

        public Packet(Settings settings)
        {
            this.settings = settings;
            logger = new Logging<Packet>(settings.GetSetting("logging", "path", "./log.txt"), logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5")));
            flagpole = new Flagpole(settings);
        }

        public void Load(byte[] packet, bool isTCP)
        {
            this.packet = packet;

            if (!isTCP)
            {
                ParseUDP();
            }
            else
            {
                ParseTCP();
            }
        }

        private void ParseUDP()
        {
            int currentPosition = 0;
            transactionId = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            currentPosition += 2;

            ushort flags = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            flagpole.Parse(flags);
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

        private void ParseTCP()
        {
            //length is stored in the first two bytes of the tcp packet
            int DNSPacketLength = (packet[0] << 8) | packet[1];

            //merging the first two bytes into a ushort
            int currentPosition = 2;
            transactionId = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            currentPosition += 2;

            ushort flags = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            flagpole.Parse(flags);
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

        private void GetQuestions(ref int currentPosition)
        {
            //get questions and answers
            questions = new Question[questionCount];
            for (int i = 0; i < questionCount; i++)
            {
                questions[i] = new Question(settings: settings);
                questions[i].Load(ref packet, ref currentPosition);
            }
        }

        private void GetAnwsers(ref int currentPosition)
        {
            //get answers
            answers = new Answer[answerCount];
            for (int i = 0; i < answerCount; i++)
            {
                //Console.WriteLine("reading anwser at position: " + currentPosition);
                answers[i] = new Answer();
                answers[i].Parse(ref packet, ref currentPosition);
            }
        }

        public void AddQuestion(Question question)
        {
            //add a question to the packet
            questionCount++;
            //if questions is null, create a new array
            if (questions == null)
            {
                questions = new Question[questionCount];
                questions[0] = question;
            }
            else
            {
                //create a new array with the new size
                Question[] newQuestions = new Question[questionCount];
                //copy the old array into the new array
                for (int i = 0; i < questions.Length; i++)
                {
                    newQuestions[i] = questions[i];
                }
                //set the new array to the old array
                questions = newQuestions;
            }
        }

        public Question[] GetQuestions()
        {
            //get the questions
            return questions;
        }

        public ushort GetQuestionCount()
        {
            //get the question count
            return questionCount;
        }

        public void AddAnswer(string answer)
        {
            //add an answer to the packet

            answerCount++;
            //if answers is null, create a new array
        }

        public void SetTransactionId(ushort transactionId)
        {
            //set the transaction id

            this.transactionId = transactionId;
        }

        public ushort GetTransactionId()
        {
            //get the transaction id

            return transactionId;
        }

        private byte[] ToBytesUDP()
        {
            byte[] packet = new byte[512];

            int currentPosition = 0;

            //add the transaction id
            packet[currentPosition] = (byte)(transactionId >> 8);
            packet[currentPosition + 1] = (byte)(transactionId & 0xFF);
            currentPosition += 2;

            //add the flags
            ushort flags = flagpole.Generate();
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
            for (int i = 0; i < questionCount; i++)
            {
                questions[i]?.Generate(ref packet, ref currentPosition);
            }

            //add the answers
            for (int i = 0; i < answerCount; i++)
            {
                answers[i]?.Generate(ref packet, ref currentPosition);
            }


            byte[] shortenedPacket = new byte[currentPosition];
            for (int i = 0; i < currentPosition; i++)
            {
                shortenedPacket[i] = packet[i];
            }

            this.packet = shortenedPacket;

            return shortenedPacket;
        }

        private byte[] ToBytesTCP()
        {
            //generate a packet
            byte[] packet = new byte[1024];

            int currentPosition = 2;

            //add the transaction id
            packet[currentPosition] = (byte)(transactionId >> 8);
            packet[currentPosition + 1] = (byte)(transactionId & 0xFF);
            currentPosition += 2;

            //add the flags
            ushort flags = flagpole.Generate();
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
            for (int i = 0; i < questionCount; i++)
            {
                questions[i]?.Generate(ref packet, ref currentPosition);
            }

            //add the answers
            for (int i = 0; i < answerCount; i++)
            {
                answers[i]?.Generate(ref packet, ref currentPosition);
            }

            //add the length to the packet and remove the first two bytes
            ushort length = (ushort)(currentPosition);

            //logger.Debug("Length: " + length);

            packet[0] = (byte)(length >> 8);
            packet[1] = (byte)(length & 0xFF);

            this.packet = packet;

            return packet;
        }

        public byte[] ToBytes(bool isTCP = false)
        {
            //call the correct function to convert the packet to bytes
            if (isTCP)
            {
                return ToBytesTCP();
            }
            else
            {
                return ToBytesUDP();
            }
        }

        private void AddAnswer()
        {
            //add an answer to the packet
        }

        public void Print()
        {
            //print the packet
            logger.Debug("Transaction ID: " + transactionId);

            //print the flags
            flagpole.Print();

            logger.Debug("Question Count: " + questionCount);
            logger.Debug("Answer Count: " + answerCount);
            for (int i = 1; i <= questionCount; i++)
            {
                logger.Debug("Question: " + i);
                questions[i-1].Print();
            }
            for (int i = 1; i <= answerCount; i++)
            {
                logger.Debug("Answer: " + i);
                answers[i-1].Print();
            }

            //print packet data as hex
            //logger.Debug(BitConverter.ToString(packet).Replace("-", " "));
        }
    }
}