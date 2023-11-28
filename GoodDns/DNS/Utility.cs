namespace GoodDns.DNS {
    public static class Utility {
        public static string GetDomainName(byte[] packet, ref int currentPosition) {
            //read the domain name
            string domainName = "";
            while (packet[currentPosition] != 0)
            {
                int domainNameLength = packet[currentPosition];

                if(domainNameLength == 0x01) {
                    //domainName += (char)packet[currentPosition];
                    //domainName += (char)packet[currentPosition + 1];
                    currentPosition++;
                    break;
                }

                currentPosition++;
                for (int i = 0; i < domainNameLength; i++)
                {
                    domainName += (char)packet[currentPosition];
                    //Console.WriteLine($"{currentPosition} : {(char)packet[currentPosition]}");
                    currentPosition++;
                }
                domainName += ".";
            }
            currentPosition++;
            return domainName;
        }

        public static byte[] GenerateDomainName(string domainName) {
            List<byte> bytes = new List<byte>();
            string[] domainNameParts = domainName.Split(".");
            foreach(string domainNamePart in domainNameParts) {
                if(domainNamePart.Length == 0x00) {
                    break;
                }
                bytes.Add((byte)domainNamePart.Length);
                foreach(char c in domainNamePart) {
                    bytes.Add((byte)c);
                }
            }

            //bytes.Add(0x00);
            return bytes.ToArray();
        }

        public static string GetDomainNameFromBytes(byte[] bytes) {
            int currentPosition = 0;
            return Utility.GetDomainName(bytes, ref currentPosition);
        }

        public static byte[] addNullByte(byte[] bytes) {
            List<byte> newBytes = new List<byte>();
            foreach(byte b in bytes) {
                newBytes.Add(b);
            }
            newBytes.Add(0x00);
            return newBytes.ToArray();
        }

        public static byte[] StringToBytes(string str)
        {
            return System.Text.Encoding.ASCII.GetBytes(str);
        }

        public static byte[] ParseIP(string ip)
        {
            string[] parts = ip.Split('.');
            byte[] bytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                bytes[i] = byte.Parse(parts[i]);
            }
            return bytes;
        }

        public static byte[] ParseIPv6(string ip)
        {
            string[] parts = ip.Split(':');
            byte[] bytes = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                bytes[i] = byte.Parse(parts[i]);
            }
            return bytes;
        }
    }
}