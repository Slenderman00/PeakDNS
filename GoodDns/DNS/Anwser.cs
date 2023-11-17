namespace GoodDns.DNS
{
    class Answer {
        string? domainName;
        RTypes answerType;
        RClasses answerClass;
        uint ttl;
        ushort dataLength;
        byte[]? data;
    }
}