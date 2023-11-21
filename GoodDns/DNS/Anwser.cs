namespace GoodDns.DNS
{
    public class Answer {
        public string? domainName;
        public RTypes answerType;
        public RClasses answerClass;
        public uint ttl;
        public ushort dataLength;
        public byte[]? data;

        public Answer(string domainName="", RTypes answerType=RTypes.A, RClasses answerClass=RClasses.IN, uint ttl=0, ushort dataLength=0, byte[] data=null) {
            this.domainName = domainName;
            this.answerType = answerType;
            this.answerClass = answerClass;
            this.ttl = ttl;
            this.dataLength = dataLength;
            this.data = data;
        }

        public void Parse(ref byte[] answer, ref int currentPosition) {
            int domainNamePointer = (answer[currentPosition] << 8) | answer[currentPosition + 1];

            // Check if the current label is a compression pointer
            if ((domainNamePointer & 0xC000) == 0xC000) {
                // Compression pointer found
                int offset = domainNamePointer & 0x3FFF; // Extract offset from the pointer
                domainNamePointer = offset; // Update domainNamePointer to the offset
            } else {
                // Not a compression pointer, move currentPosition to the next label
                currentPosition += 2;
            }

            domainName = Utility.GetDomainName(answer, ref domainNamePointer);

            answerType = (RTypes)((answer[domainNamePointer] << 8) | answer[domainNamePointer + 1]);
            domainNamePointer += 2;

            answerClass = (RClasses)((answer[domainNamePointer] << 8) | answer[domainNamePointer + 1]);
            domainNamePointer += 2;

            ttl = (uint)((answer[domainNamePointer] << 24) | (answer[domainNamePointer + 1] << 16) | (answer[domainNamePointer + 2] << 8) | answer[domainNamePointer + 3]);
            domainNamePointer += 4;

            dataLength = (ushort)((answer[domainNamePointer] << 8) | answer[domainNamePointer + 1]);
            domainNamePointer += 2;

            // Ensure that there is enough data in the array before trying to copy
            if (domainNamePointer + dataLength <= answer.Length) {
                data = new byte[dataLength];
                for (int i = 0; i < dataLength; i++) {
                    data[i] = answer[domainNamePointer];
                    domainNamePointer++;
                }
            } else {
                // Handle the case where there is not enough data in the array
                Console.WriteLine("Error: Insufficient data in the array to read.");
            }
        }


        public byte[] Generate() {
            List<byte> bytes = new List<byte>();
            bytes.AddRange(Utility.GenerateDomainName(domainName));
            bytes.AddRange(BitConverter.GetBytes((ushort)answerType));
            bytes.AddRange(BitConverter.GetBytes((ushort)answerClass));
            bytes.AddRange(BitConverter.GetBytes(ttl));
            bytes.AddRange(BitConverter.GetBytes(dataLength));
            bytes.AddRange(data);
            return bytes.ToArray();
        }

        public void Print() {
            Console.WriteLine("Domain Name: " + domainName);
            Console.WriteLine("Answer Type: " + Enum.GetName(typeof(RTypes), answerType));
            Console.WriteLine("Answer Class: " + Enum.GetName(typeof(RClasses), answerClass));
            Console.WriteLine("TTL: " + ttl);
            Console.WriteLine("Data Length: " + dataLength);
            Console.WriteLine("Data: " + data);
        }
    }
}