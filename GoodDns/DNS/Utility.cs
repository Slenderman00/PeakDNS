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
    }
}