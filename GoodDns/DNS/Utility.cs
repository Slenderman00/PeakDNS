namespace GoodDns.DNS {
    public static class Utility {
        public static string GetDomainName(byte[] packet, ref int currentPosition) {
            Console.WriteLine("Current Position: " + currentPosition);
            //read the domain name
            string domainName = "";
            while (packet[currentPosition] != 0)
            {
                int domainNameLength = packet[currentPosition];

                if(domainNameLength == 0x01) {
                    currentPosition++;
                    break;
                }

                currentPosition++;
                for (int i = 0; i < domainNameLength; i++)
                {
                    domainName += (char)packet[currentPosition];
                    //Console.WriteLine((char)packet[currentPosition]);
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
                bytes.Add((byte)domainNamePart.Length);
                foreach(char c in domainNamePart) {
                    bytes.Add((byte)c);
                }
            }
            bytes.Add(0);
            return bytes.ToArray();
        }
    }
}