
namespace GoodDns.DNS 
{
    public class Flagpole {

        public Settings settings;
        static Logging<Flagpole> logger;

        public bool AA = false;
        public bool TC = false;
        public bool RD = false;
        public bool RA = false;
        public bool AD = false;
        public bool CD = false;
        public bool QR = false; //true = response, false = query
        public bool QD = false;
        public bool AN = false;
        public bool NS = false;
        public bool AR = false;

        public OPCodes OPcode = OPCodes.QUERY;
        public RCodes RCode = RCodes.NOERROR;

        public Flagpole(Settings settings) {
            this.settings = settings;
            logger = new Logging<Flagpole>(settings.GetSetting("logging", "path", "./log.txt"), logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5")));
        }



        public void Parse(ushort flags) {
            AA = (flags & 0x0400) != 0;
            TC = (flags & 0x0200) != 0;
            RD = (flags & 0x0100) != 0;
            RA = (flags & 0x0080) != 0;
            AD = (flags & 0x0020) != 0;
            CD = (flags & 0x0010) != 0;
            QR = (flags & 0x8000) != 0;

            QD = (flags & 0x1000) != 0;
            AN = (flags & 0x0800) != 0;
            NS = (flags & 0x0400) != 0;
            AR = (flags & 0x0200) != 0;

            OPcode = (OPCodes)((flags & 0x7800) >> 11);
            RCode = (RCodes)(flags & 0x000F);
        }

        public ushort Generate() {
            ushort flags = 0;
            if(AA) flags |= 0x0400;
            if(TC) flags |= 0x0200;
            if(RD) flags |= 0x0100;
            if(RA) flags |= 0x0080;
            if(AD) flags |= 0x0020;
            if(CD) flags |= 0x0010;
            if(QR) flags |= 0x8000;

            if(QD) flags |= 0x1000;
            if(AN) flags |= 0x0800;
            if(NS) flags |= 0x0400;
            if(AR) flags |= 0x0200;

            flags |= (ushort)((ushort)OPcode << 11);
            flags |= (ushort)RCode;

            return flags;
        }

        public void Print() {
            logger.Debug("AA: " + AA);
            logger.Debug("TC: " + TC);
            logger.Debug("RD: " + RD);
            logger.Debug("RA: " + RA);
            logger.Debug("AD: " + AD);
            logger.Debug("CD: " + CD);
            logger.Debug("QR: " + QR);
            logger.Debug("QD: " + QD);
            logger.Debug("AN: " + AN);
            logger.Debug("NS: " + NS);
            logger.Debug("AR: " + AR);

            logger.Debug("Opcode: " + Enum.GetName(typeof(OPCodes), OPcode));
            logger.Debug("Rcode: " + Enum.GetName(typeof(RCodes), RCode));
        }
    }
}