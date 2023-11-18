namespace GoodDns.DNS
{
    class Answer {
        public string? domainName;
        public RTypes answerType;
        public RClasses answerClass;
        public uint ttl;
        public ushort dataLength;
        public byte[]? data;

        public void Parse(byte[] anwser) {
            
        }
    }
}