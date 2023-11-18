namespace GoodDns.DNS {
    public static class Utility {
        public static string GetDomainName(byte[] packet, ref int currentPosition) {
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
    }
}