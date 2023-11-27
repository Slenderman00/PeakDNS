//include regex
using System.Text.RegularExpressions;
using System.Text;

namespace GoodDns.DNS.Server
{
    public class Record
    {
        Logging<Record> logger = new Logging<Record>("./log.txt", logLevel: 5);
        public string name;
        public int ttl;
        public RClasses _class = RClasses.IN;
        public RTypes type;
        public ushort priority;
        public byte[] data;

        public RTypes getTypeByName(string name)
        {
            switch (name)
            {
                case "A":
                    return RTypes.A;
                case "AAAA":
                    return RTypes.AAAA;
                case "CNAME":
                    return RTypes.CNAME;
                case "MX":
                    return RTypes.MX;
                case "NS":
                    return RTypes.NS;
                case "PTR":
                    return RTypes.PTR;
                case "SOA":
                    return RTypes.SOA;
                case "TXT":
                    return RTypes.TXT;
                default:
                    return RTypes.A;
            }
        }

        public void parseLine(string line)
        {
            //remove trailing and leading whitespace
            line = line.Trim();
            //remove whitespace leaving only one space between each word
            line = Regex.Replace(line, @"\s+", " ");
            //logger.Debug("line: " + line);

            //Allocate as much data as the length, this is a dumb way of doing it...
            //data = new byte[line.Length];    

            //split the line into parts
            string[] parts = line.Split(' ');

            if (parts.Length < 3 || parts.Length > 4)
            {
                logger.Error("Invalid record: " + line);
                return;
            }

            //logger.Debug("parts: " + string.Join(", ", parts));

            if (parts.Length == 3)
            {
                ttl = int.Parse(parts[0]);
                type = getTypeByName(parts[1]);
                //make sure data is not an ip address
                if (type == RTypes.A)
                {
                    data = ParseIP(parts[2]);
                    return;
                }

                if (type == RTypes.MX)
                {
                    logger.Debug("three bytes MX");
                }

                data = StringToBytes(parts[2]);
                logger.Debug("--------------------");
                this.Print();
                //log all parts in the parts array
                logger.Debug("parts: " + string.Join(", ", parts));
                logger.Debug("--------------------");
                return;
            }

            if (parts.Length == 4 && getTypeByName(parts[1]) != RTypes.MX)
            {
                //logger.Debug("type: " + getTypeByName(parts[2]));
                //logger.Debug("name: " + parts[0]);
                this.name = parts[0];
                ttl = int.Parse(parts[1]);
                type = getTypeByName(parts[2]);
                //check if data is an ip address
                if (type == RTypes.A)
                {
                    data = ParseIP(parts[3]);
                    return;
                }

                data = StringToBytes(parts[3]);
                logger.Debug("--------------------");
                this.Print();
                //log all parts in the parts array
                logger.Debug("parts: " + string.Join(", ", parts));
                logger.Debug("--------------------");
                return;
            }

            if (parts.Length == 4 && getTypeByName(parts[1]) == RTypes.MX)
            {
                ttl = int.Parse(parts[0]);
                type = getTypeByName(parts[1]);
                //this.name = "example.com";

                //relative domain name
                data = new byte[parts[3].Length + 3];

                //parts 2 must be an ushort
                ushort priority = ushort.Parse(parts[2]);
                this.priority = priority;

                data[0] = (byte)(priority >> 8);
                data[1] = (byte)(priority & 0xFF);

                //logger.Debug($"Priority 1: {(ushort)data[0]}");
                //the rest of the data must be the exchange name
                byte[] domainName = Utility.GenerateDomainName(parts[3]);
                //parts[3] = parts[3].Remove(parts[3].Length - 1);
                //byte[] domainName = StringToBytes(parts[3]);
                for (int i = 0; i < domainName.Length; i++)
                {
                    logger.Debug($"domainName[{i}]: {domainName[i]}");
                    data[i + 2] = domainName[i];
                }
                logger.Debug("--------------------");
                this.Print();
                //log all parts in the parts array
                logger.Debug("parts: " + string.Join(", ", parts));
                logger.Debug("--------------------");

                return;
            }
        }

        public byte[] ParseIP(string ip)
        {
            string[] parts = ip.Split('.');
            byte[] bytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                bytes[i] = byte.Parse(parts[i]);
            }
            return bytes;
        }
        public byte[] StringToBytes(string str)
        {
            return System.Text.Encoding.ASCII.GetBytes(str);
            //return new byte[0];
        }

        public void Print()
        {
            logger.Debug("name: " + this.name);
            logger.Debug("ttl: " + ttl);
            logger.Debug("class: " + _class);
            logger.Debug("type: " + type);
            logger.Debug("priority: " + priority);
            //if not null
            if (data != null)
            {
                //print data as hex
                logger.Debug("data: " + BitConverter.ToString(data).Replace("-", " "));
            }
        }
    }

    //This class represents a BIND zone file
    //This dns server will base it anwsers on what is in these files and what is in the cache
    //Items that are not in the cache will be requested from a list of other specified dns servers

    //example zone file
    /*
    $ORIGIN example.com.
    @                      3600 SOA   ns1.p30.dynect.net. (
                                zone-admin.dyndns.com.     ; address of responsible party
                                2016072701                 ; serial number
                                3600                       ; refresh period
                                600                        ; retry period
                                604800                     ; expire time
                                1800                     ) ; minimum ttl
                        86400 NS    ns1.p30.dynect.net.
                        86400 NS    ns2.p30.dynect.net.
                        86400 NS    ns3.p30.dynect.net.
                        86400 NS    ns4.p30.dynect.net.
                        3600 MX    10 mail.example.com.
                        3600 MX    20 vpn.example.com.
                        3600 MX    30 mail.example.com.
                            60 A     204.13.248.106
                        3600 TXT   "v=spf1 includespf.dynect.net ~all"
    mail                  14400 A     204.13.248.106
    vpn                      60 A     216.146.45.240
    webapp                   60 A     216.146.46.10
    webapp                   60 A     216.146.46.11
    www                   43200 CNAME example.com.
    */

    public class BIND
    {
        Logging<BIND> logger = new Logging<BIND>("./log.txt", logLevel: 5);

        public string? origin = null;
        public string? primaryNameserver = null;
        public string? hostmaster = null;
        public string? serial = null;
        public string? refresh = null;
        public string? retry = null;
        public string? expire = null;
        public int? TTL = null;
        public int? minimumTTL = null;
        public List<Record> records;

        bool parsingSOA = false;

        private void parseRecord(string line)
        {
            string[] parts = line.Split(' ');
            //if line starts with $ORIGIN
            if (parts[0] == "$ORIGIN")
            {
                origin = parts[1];
            }
            else
            {
                Record record = new Record();
                record.parseLine(line);
                records.Add(record);
            }
        }

        /*
    @                      3600 SOA   ns1.p30.dynect.net. (
                                zone-admin.dyndns.com.     ; address of responsible party
                                2016072701                 ; serial number
                                3600                       ; refresh period
                                600                        ; retry period
                                604800                     ; expire time
                                1800                     ) ; minimum ttl

        */

        private void parseSOA(string line)
        {
            //remove )
            line = line.Replace("(", "");
            //remove whitespace leaving only one space between each word
            line = Regex.Replace(line, @"\s+", " ");
            //remove the @
            line = line.Substring(1);
            //remove everything after ;
            line = line.Split(';')[0];
            //remove trailing and leading whitespace
            line = line.Trim();
            string[] parts = line.Split(' ');
            //logger.Debug("line: " + line);
            //logger.Debug("parts: " + string.Join(", ", parts));
            //check if ttl is set
            if (TTL == null)
            {
                TTL = Int32.Parse(parts[0]);
                primaryNameserver = parts[2];
                //logger.Debug("TTL: " + TTL);
                //logger.Debug("primaryNameserver: " + primaryNameserver);
                return;
            }
            //check if array is empty
            if (parts.Length == 0) return;
            //check if hostmaster is set
            if (hostmaster == null)
            {
                hostmaster = parts[0];
                //strip these parts from the parts array
                parts = parts.Skip(1).ToArray();
                //logger.Debug("hostmaster: " + hostmaster);
            }
            if (parts.Length == 0) return;
            //check if serial
            if (serial == null)
            {
                serial = parts[0];
                //remove this data from the parts array
                parts = parts.Skip(1).ToArray();
                //logger.Debug("serial: " + serial);
            }
            if (parts.Length == 0) return;
            //check if refresh is set
            if (refresh == null)
            {
                refresh = parts[0];
                //remove this data from the parts array
                parts = parts.Skip(1).ToArray();
                //logger.Debug("refresh: " + refresh);
            }
            if (parts.Length == 0) return;
            //check if retry is set
            if (retry == null)
            {
                retry = parts[0];
                //remove this data from the parts array
                parts = parts.Skip(1).ToArray();
                //logger.Debug("retry: " + retry);
            }
            if (parts.Length == 0) return;
            //check if expire is set
            if (expire == null)
            {
                expire = parts[0];
                //remove this data from the parts array
                parts = parts.Skip(1).ToArray();
                //logger.Debug("expire: " + expire);
            }
            if (parts.Length == 0) return;
            //check if minimumTTL is set
            if (minimumTTL == null)
            {
                minimumTTL = Int32.Parse(parts[0]);
                //remove this data from the parts array
                parts = parts.Skip(1).ToArray();
                //logger.Debug("minimumTTL: " + minimumTTL);
            }
            if (parts.Length == 0) return;
            if (line.Contains(')'))
            {
                parsingSOA = false;
            }

        }

        public Answer[] getAnswers(Question question)
        {
            if (!canAnwser(question)) return null;
            List<Answer> answers = new List<Answer>();
            foreach (Record record in records)
            {
                if (record.type == question.type)
                {
                    Answer answer = new Answer();
                    answer.domainName = record.name;
                    answer.answerType = record.type;
                    answer.answerClass = record._class;
                    answer.ttl = record.ttl;

                    //check if there is any data
                    if (record.data == null)
                    {
                        answer.dataLength = 0;
                        answer.rData = new byte[0];
                        answers.Add(answer);
                        continue;
                    }

                    answer.dataLength = (uint)record.data.Length;
                    answer.rData = record.data;
                    answers.Add(answer);
                }
            }
            return answers.ToArray();
        }

        public bool canAnwser(Question question)
        {
            //check if the origin matches the question
            if (origin != question.GetDomainName()) return false;
            //check if the question type is in the records
            foreach (Record record in records)
            {
                if (record.type == question.type) return true;
            }
            return false;
        }

        private void parseLine(string line)
        {
            //if line starts with @
            if (line.StartsWith("@"))
            {
                parsingSOA = true;
            }
            if (!parsingSOA)
            {
                parseRecord(line);
            }
            else
            {
                parseSOA(line);
            }
        }

        public BIND(string path)
        {
            records = new List<Record>();
            string[] lines = File.ReadAllLines(path);
            foreach (string line in lines)
            {
                parseLine(line);
            }
        }

        public void Print()
        {
            logger.Debug("origin: " + origin);
            logger.Debug("primaryNameserver: " + primaryNameserver);
            logger.Debug("hostmaster: " + hostmaster);
            logger.Debug("serial: " + serial);
            logger.Debug("refresh: " + refresh);
            logger.Debug("retry: " + retry);
            logger.Debug("expire: " + expire);
            logger.Debug("TTL: " + TTL);
            logger.Debug("minimumTTL: " + minimumTTL);
            int i = 1;
            foreach (Record record in records)
            {
                logger.Debug($"record: {i++}");
                record.Print();
            }
        }
    }
}