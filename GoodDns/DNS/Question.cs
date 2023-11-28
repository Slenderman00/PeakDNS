
namespace GoodDns.DNS
{
    public class Question
    {
        Settings settings;
        static Logging<Question> logger;
        public string domainName;
        public RTypes type;
        public RClasses _class;

        public Question(string domainName = "", RTypes RType = RTypes.A, RClasses RClass = RClasses.IN, Settings settings = null)
        {
            this.settings = settings;
            logger = new Logging<Question>(settings.GetSetting("logging", "path", "./log.txt"), logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5")));

            //create a new question
            this.domainName = domainName;
            this.type = RType;
            this._class = RClass;
        }

        public void Print()
        {
            //print the packet
            logger.Debug("Domain Name: " + domainName);
            logger.Debug("Question Type: " + Enum.GetName(typeof(RTypes), type));
            logger.Debug("Question Class: " + Enum.GetName(typeof(RClasses), _class));
        }
        public string GetDomainName()
        {
            //get the domain name
            return domainName;
        }

        public ushort GetQType()
        {
            //get the question type
            return (ushort)type;
        }

        public ushort GetQClass()
        {
            //get the question class
            return (ushort)_class;
        }

        public void Load(ref byte[] packet, ref int currentPosition)
        {
            //load a question from the packet
            //load the domain name
            this.domainName = Utility.GetDomainName(packet, ref currentPosition);

            // Load the question type
            ushort questionTypeValue = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            this.type = (RTypes)questionTypeValue;
            currentPosition += 2;

            // Load the question class
            ushort questionClassValue = (ushort)((packet[currentPosition] << 8) | packet[currentPosition + 1]);
            this._class = (RClasses)questionClassValue;
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

            //this fixes the test but breaks the program
            //currentPosition++;

            //add the question type
            packet[currentPosition] = (byte)((ushort)this.type >> 8);
            packet[currentPosition + 1] = (byte)((ushort)this.type & 0xFF);
            currentPosition += 2;

            //add the question class
            packet[currentPosition] = (byte)((ushort)this._class >> 8);
            packet[currentPosition + 1] = (byte)((ushort)this._class & 0xFF);
            currentPosition += 2;
        }
    }
}