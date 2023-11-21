using System.Text;

namespace GoodDns.DNS
{
    public class Answer {
        public string? domainName;
        public RTypes answerType;
        public RClasses answerClass;
        public uint ttl;
        public ushort dataLength;
        public byte[]? rData;

        public Answer(string domainName="", RTypes answerType=RTypes.A, RClasses answerClass=RClasses.IN, uint ttl=0, ushort dataLength=0, byte[] rData=null) {
            this.domainName = domainName;
            this.answerType = answerType;
            this.answerClass = answerClass;
            this.ttl = ttl;
            this.dataLength = dataLength;
            this.rData = rData;
        }

        public void Parse(ref byte[] answer, ref int currentPosition) {
            int pointer = (answer[currentPosition] << 8) | answer[currentPosition + 1];

            bool isPointer = (pointer & 0xC000) == 0xC000;

            //check if the current label is a compression pointer
            if (isPointer) {
                // Compression pointer found
                int offset = pointer & 0x3FFF; // Extract offset from the pointer
                Console.WriteLine("Compression Pointer Found: " + offset);
                domainName = Utility.GetDomainName(answer, ref offset); // Get the domain name from the offset
            } else {
                // No compression pointer found
                domainName = Utility.GetDomainName(answer, ref currentPosition);
            }

            currentPosition += 2;

            answerType = (RTypes)((answer[currentPosition] << 8) | answer[currentPosition + 1]);
            currentPosition += 2;

            answerClass = (RClasses)((answer[currentPosition] << 8) | answer[currentPosition + 1]);
            currentPosition += 2;

            ttl = (uint)((answer[currentPosition] << 24) | (answer[currentPosition + 1] << 16) | (answer[currentPosition + 2] << 8) | answer[currentPosition + 3]);
            currentPosition += 4;

            dataLength = (ushort)((answer[currentPosition] << 8) | answer[currentPosition + 1]);
            currentPosition += 2;

            // Ensure that there is enough data in the array before trying to copy
            if (currentPosition + dataLength <= answer.Length) {
                rData = new byte[dataLength];
                for (int i = 0; i < dataLength; i++) {
                    rData[i] = answer[currentPosition++];
                }
            } else {
                // Handle the case where there is not enough data in the array
                Console.WriteLine("Error: Insufficient data in the array to read.");
            }
        }


        //re-implement in the same way as the question class
        public byte[] Generate() {
            List<byte> bytes = new List<byte>();
            bytes.AddRange(Utility.GenerateDomainName(domainName));
            bytes.AddRange(BitConverter.GetBytes((ushort)answerType));
            bytes.AddRange(BitConverter.GetBytes((ushort)answerClass));
            bytes.AddRange(BitConverter.GetBytes(ttl));
            bytes.AddRange(BitConverter.GetBytes(dataLength));
            bytes.AddRange(rData);
            return bytes.ToArray();
        }

        public void Print() {
            Console.WriteLine("Domain Name: " + domainName);
            Console.WriteLine("Answer Type: " + Enum.GetName(typeof(RTypes), answerType));
            Console.WriteLine("Answer Class: " + Enum.GetName(typeof(RClasses), answerClass));
            Console.WriteLine("TTL: " + ttl);
            Console.WriteLine("Data Length: " + dataLength);
            printData();
        }

        public void printData() {
            switch (answerType) {
                case RTypes.A:
                    Console.WriteLine("IP Address: " + rData[0] + "." + rData[1] + "." + rData[2] + "." + rData[3]);
                    break;
                case RTypes.NS:
                    Console.WriteLine("Name Server: " + Encoding.ASCII.GetString(rData));
                    break;
                case RTypes.CNAME:
                    Console.WriteLine("Canonical Name: " + Encoding.ASCII.GetString(rData));
                    break;
                case RTypes.SOA:
                    Console.WriteLine("Primary Name Server: " + Encoding.ASCII.GetString(rData));
                    break;
                case RTypes.MX:
                    Console.WriteLine("Mail Exchange: " + Encoding.ASCII.GetString(rData));
                    break;
                case RTypes.TXT:
                    Console.WriteLine("Text: " + Encoding.ASCII.GetString(rData));
                    break;
                case RTypes.AAAA:
                    Console.WriteLine("IPv6 Address: " + rData[0] + "." + rData[1] + "." + rData[2] + "." + rData[3]);
                    break;
                case RTypes.SRV:
                    Console.WriteLine("Service: " + Encoding.ASCII.GetString(rData));
                    break;
                default:
                    Console.WriteLine("Unknown Answer Type: " + answerType);
                    break;
            }
        }
    }
}