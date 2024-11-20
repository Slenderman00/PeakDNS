namespace PeakDNS.DNS
{
    public enum OPCodes
    {
        QUERY = 0,
        IQUERY = 1,
        STATUS = 2,
        NOTIFY = 4,
        UPDATE = 5
    }

    public enum RCodes
    {
        NOERROR = 0,
        FORMERR = 1,
        SERVFAIL = 2,
        NXDOMAIN = 3,
        NOTIMP = 4,
        REFUSED = 5,
        YXDOMAIN = 6,
        YXRRSET = 7,
        NXRRSET = 8,
        NOTAUTH = 9,
        NOTZONE = 10,
        BADVERS = 16,
        BADSIG = 16,
        BADKEY = 17,
        BADTIME = 18,
        BADMODE = 19,
        BADNAME = 20,
        BADALG = 21,
        BADTRUNC = 22
    }

    //add question types:
    //A: 1
    //AAAA: 28
    //CNAME: 5
    //MX: 15
    //NS: 2
    //PTR: 12
    //SOA: 6
    //SRV: 33
    //TXT: 16
    public enum RTypes : ushort
    {
        A = 1,
        AAAA = 28,
        CNAME = 5,
        MX = 15,
        NS = 2,
        PTR = 12,
        SOA = 6,
        SRV = 33,
        TXT = 16
    }


    //add question classes:
    //IN: 1
    //CS: 2
    //CH: 3
    //HS: 4
    public enum RClasses : ushort
    {
        IN = 1,
        CS = 2,
        CH = 3,
        HS = 4
    }
}