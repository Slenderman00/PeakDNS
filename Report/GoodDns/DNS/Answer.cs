using System.Text;

namespace GoodDns.DNS
{
    public class Answer {
        Logging<Answer> logger = new Logging<Answer>("./log.txt", logLevel: 5);
        public string? domainName;
        public RTypes answerType;
        public RClasses answerClass;
        public int ttl;
        public uint dataLength;
        public byte[]? rData;

        public Answer(string domainName="", RTypes answerType=RTypes.A, RClasses answerClass=RClasses.IN, int ttl=0, ushort dataLength=0, byte[]? rData=null) {
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
                //compression pointer found
                int offset = pointer & 0x3FFF; //extract offset from the pointer
                domainName = Utility.GetDomainName(answer, ref offset); //get the domain name from the offset
                currentPosition += 2;
            } else {
                logger.Debug("No Compression Pointer Found");
                //no compression pointer found
                domainName = Utility.GetDomainName(answer, ref currentPosition);
            }

            answerType = (RTypes)((answer[currentPosition] << 8) | answer[currentPosition + 1]);
            currentPosition += 2;

            answerClass = (RClasses)((answer[currentPosition] << 8) | answer[currentPosition + 1]);
            currentPosition += 2;

            ttl = (int)((answer[currentPosition] << 24) | (answer[currentPosition + 1] << 16) | (answer[currentPosition + 2] << 8) | answer[currentPosition + 3]);
            currentPosition += 4;

            dataLength = (ushort)((answer[currentPosition] << 8) | answer[currentPosition + 1]);
            currentPosition += 2;

            //ensure that there is enough data in the array before trying to copy
            if (currentPosition + dataLength <= answer.Length) {
                rData = new byte[dataLength];
                for (int i = 0; i < dataLength; i++) {
                    rData[i] = answer[currentPosition++];
                }
            } else {
                //handle the case where there is not enough data in the array
                logger.Warning("Error: Insufficient data in the array to read.");
            }
        }

        public void Generate(ref byte[] packet, ref int currentPosition) {

            if(answerType != RTypes.MX && answerType != RTypes.NS && answerType != RTypes.CNAME) {
                //add an answer to the packet
                //add the domain name
                if(domainName != null) {
                    byte[] domainNameBytes = Utility.GenerateDomainName(domainName);
                    for (int j = 0; j < domainNameBytes.Length; j++) {
                        packet[currentPosition] = domainNameBytes[j];
                        currentPosition++;
                    }
                }
            };

            packet[currentPosition] = 0;
            currentPosition += 1;

            //add the answer type
            packet[currentPosition] = (byte)(((ushort)answerType) >> 8);
            packet[currentPosition+ 1] = (byte)(((ushort)answerType) & 0xFF);
            currentPosition += 2;

            //add the answer class
            packet[currentPosition] = (byte)(((ushort)answerClass) >> 8);
            packet[currentPosition + 1] = (byte)(((ushort)answerClass) & 0xFF);
            currentPosition += 2;

            //add the ttl
            packet[currentPosition] = (byte)(ttl >> 24);
            packet[currentPosition + 1] = (byte)(ttl >> 16);
            packet[currentPosition + 2] = (byte)(ttl >> 8);
            packet[currentPosition + 3] = (byte)(ttl & 0xFF);
            currentPosition += 4;

            //add the data length
            packet[currentPosition] = (byte)(dataLength >> 8);
            packet[currentPosition + 1] = (byte)(dataLength & 0xFF);
            currentPosition += 2;

            //add the rData
            for (int j = 0; j < dataLength; j++) {
                packet[currentPosition] = rData[j];
                currentPosition++;
            }
        }

        public void Print() {
            logger.Debug("Domain Name: " + domainName);
            logger.Debug("Answer Type: " + Enum.GetName(typeof(RTypes), answerType));
            logger.Debug("Answer Class: " + Enum.GetName(typeof(RClasses), answerClass));
            logger.Debug("TTL: " + ttl);
            logger.Debug("Data Length: " + dataLength);

            printData();
        }

        public void printData() {
            switch (answerType) {
                case RTypes.A:
                    //check if the data length is 4
                    if (dataLength == 4) {
                        logger.Debug("IP Address: " + rData[0] + "." + rData[1] + "." + rData[2] + "." + rData[3]);
                    } else {
                        logger.Warning("Error: Invalid data length for IPv4 address.");
                    }
                    break;
                case RTypes.NS:
                    logger.Debug("Name Server: " + Encoding.ASCII.GetString(rData));
                    break;
                case RTypes.CNAME:
                    logger.Debug("Canonical Name: " + Utility.GetDomainNameFromBytes(rData));
                    break;
                case RTypes.SOA:
                    logger.Debug("Primary Name Server: " + Encoding.ASCII.GetString(rData));
                    break;
                case RTypes.MX:
                    //to first ushorts are the priority
                    //the rest is the domain name
                    int priority = (rData[0] << 8) | rData[1];
                    string domainName = Encoding.ASCII.GetString(rData[2..]);
                    logger.Debug($"Mail Exchange: {domainName} (Priority: {priority})");
                    break;
                case RTypes.TXT:
                    logger.Debug("Text: " + Encoding.ASCII.GetString(rData));
                    break;
                case RTypes.AAAA:
                    string ipv6 = "";
                    for (int i = 0; i < rData.Length; i += 2) {
                        ipv6 += rData[i].ToString("X2") + rData[i + 1].ToString("X2");
                        if (i != rData.Length - 2) {
                            ipv6 += ":";
                        }
                    }
                    logger.Debug("IPv6 Address: " + ipv6);
                    break;
                case RTypes.SRV:
                    logger.Debug("Service: " + Utility.GetDomainNameFromBytes(rData));
                    break;
                default:
                    logger.Warning("Unknown Answer Type: " + answerType);
                    break;
            }
        }
    }
}