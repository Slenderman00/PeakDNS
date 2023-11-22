
namespace GoodDns.DNS
{
    public class Question
    {
        static Logging<Question> logger = new Logging<Question>("./log.txt", logLevel: 5);
        public string domainName;
        public RTypes questionType;
        public RClasses questionClass;

        public Question(string domainName = "", RTypes RType = RTypes.A, RClasses RClass = RClasses.IN)
        {
            //create a new question
            this.domainName = domainName;
            this.questionType = RType;
            this.questionClass = RClass;
        }

        public void Print()
        {
            //print the packet
            logger.Debug("Domain Name: " + domainName);
            logger.Debug("Question Type: " + Enum.GetName(typeof(RTypes), questionType));
            logger.Debug("Question Class: " + Enum.GetName(typeof(RClasses), questionClass));
        }
        public string GetDomainName()
        {
            //get the domain name
            return domainName;
        }

        public ushort GetQType()
        {
            //get the question type
            return (ushort)questionType;
        }

        public ushort GetQClass()
        {
            //get the question class
            return (ushort)questionClass;
        }

        public void Load(ref byte[] packet, ref int currentPosition)
        {
            //load a question from the packet
            //load the domain name
            this.domainName = Utility.GetDomainName(packet, ref currentPosition);

            //print bytes
            logger.Debug(BitConverter.ToString(packet).Replace("-", " "));
            logger.Info($"currentPosition: {currentPosition}");

            // Load the question type
            //print the 2 relevant bytes
            logger.Debug($"Type Bytes: {BitConverter.ToString(packet, currentPosition, 2).Replace("-", " ")}");
            ushort questionTypeValue = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            this.questionType = (RTypes)questionTypeValue;
            logger.Info($"Question Type Value: {questionTypeValue}, Enum: {Enum.GetName(typeof(RTypes), this.questionType)}");
            currentPosition += 2;

            // Load the question class
            //print the 2 relevant bytes
            logger.Debug($"Class Bytes: {BitConverter.ToString(packet, currentPosition, 2).Replace("-", " ")}");
            ushort questionClassValue = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            this.questionClass = (RClasses)questionClassValue;
            logger.Info($"Question Class Value: {questionClassValue}, Enum: {Enum.GetName(typeof(RClasses), this.questionClass)}");
            currentPosition += 2;
        }

        public void Generate(ref byte[] packet, ref int currentPosition)
        {
            //add a question to the packet
            //add the domain name
            string[] domainNameParts = this.domainName.Split('.');
            for (int j = 0; j < domainNameParts.Length; j++)
            {
                packet[currentPosition] = (byte)domainNameParts[j].Length;
                currentPosition++;
                for (int k = 0; k < domainNameParts[j].Length; k++)
                {
                    packet[currentPosition] = (byte)domainNameParts[j][k];
                    currentPosition++;
                }
            }

            packet[currentPosition] = 0;

            //add the question type
            packet[currentPosition] = (byte)((ushort)this.questionType >> 8);
            packet[currentPosition + 1] = (byte)((ushort)this.questionType & 0xFF);
            currentPosition += 2;

            //add the question class
            packet[currentPosition] = (byte)((ushort)this.questionClass >> 8);
            packet[currentPosition + 1] = (byte)((ushort)this.questionClass & 0xFF);
            currentPosition += 2;
        }
    }
}