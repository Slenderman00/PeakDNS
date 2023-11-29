//include regex
using System.Text.RegularExpressions;
using System.Text;

namespace GoodDns.DNS.Server
{
    public class Record
    {
        Settings settings;
        Logging<Record> logger;
        public string name;
        public int ttl;
        public RClasses _class = RClasses.IN;
        public RTypes type;
        public ushort priority;
        public byte[] data;

        public Record(Settings settings) {
            this.settings = settings;
            logger = new Logging<Record>(settings.GetSetting("logging", "path", "./log.txt"), logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5")));
        }

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

        public void parseTXTRecord(string line) {
            //split " into parts
            string[] TXTparts = line.Split("\"");
            string dataPart = TXTparts[0];
            //remove trailing and leading whitespace
            dataPart = dataPart.Trim();
            //remove whitespace leaving only one space between each word
            dataPart = Regex.Replace(dataPart, @"\s+", " ");
            //split the line into parts
            string[] parts = dataPart.Split(' ');

            ttl = int.Parse(parts[0]);

            type = getTypeByName(parts[1]);
            if(type != RTypes.TXT) {
                logger.Error("Invalid TXT record: " + line);
                return;
            }
            data = Utility.StringToBytes($"\"{TXTparts[1]}\"");
        }

        public void parseLine(string line)
        {
            //check if the line is a txt record
            if(line.Contains("\"")) {
                parseTXTRecord(line);
                return;
            }

            //remove trailing and leading whitespace
            line = line.Trim();

            //remove whitespace leaving only one space between each word
            line = Regex.Replace(line, @"\s+", " ");

            //split the line into parts
            string[] parts = line.Split(' ');

            if (parts.Length < 3 || parts.Length > 4)
            {
                logger.Error("Invalid record: " + line);
                return;
            }

            if (parts.Length == 3)
            {
                ttl = int.Parse(parts[0]);
                type = getTypeByName(parts[1]);
                //make sure data is not an ip address
                if (type == RTypes.A)
                {
                    data = Utility.ParseIP(parts[2]);
                    return;
                }

                if(type == RTypes.NS) {
                    data = Utility.GenerateDomainName(parts[2]);
                    //add the null byte
                    data = Utility.addNullByte(data);
                    return;
                }

                if (type == RTypes.MX)
                {
                    logger.Debug("three bytes MX");
                    return;
                }

                return;
            }

            if(parts.Length == 4) {
                if (getTypeByName(parts[1]) != RTypes.MX)
                {
                    name = parts[0];
                    ttl = int.Parse(parts[1]);
                    type = getTypeByName(parts[2]);
                    //check if data is an ip address
                    if (type == RTypes.A)
                    {
                        data = Utility.ParseIP(parts[3]);
                        return;
                    }

                    if(type == RTypes.AAAA) {
                        data = Utility.ParseIPv6(parts[3]);
                        return;
                    }

                    if (type == RTypes.CNAME || type == RTypes.PTR) {
                        data = Utility.GenerateDomainName(parts[3]);
                        //add the null byte
                        data = Utility.addNullByte(data);
                        return;
                    }

                    data = Utility.StringToBytes(parts[3]);

                    return;
                }

                if (getTypeByName(parts[1]) == RTypes.MX)
                {
                    ttl = int.Parse(parts[0]);
                    type = getTypeByName(parts[1]);

                    data = new byte[parts[3].Length + 3];

                    //parts 2 must be an ushort
                    ushort priority = ushort.Parse(parts[2]);
                    this.priority = priority;

                    data[0] = (byte)(priority >> 8);
                    data[1] = (byte)(priority & 0xFF);

                    byte[] domainName = Utility.GenerateDomainName(parts[3]);
                    for (int i = 0; i < domainName.Length; i++)
                    {
                        data[i + 2] = domainName[i];
                    }

                    return;
                }

                return;
            }
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

    public class BIND
    {
        Settings settings;
        Logging<BIND> logger;

        public string origin;
        public string primaryNameserver;
        public string hostmaster;
        public string serial;
        public string refresh;
        public string retry;
        public string expire;
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
                Record record = new Record(settings);
                record.parseLine(line);
                records.Add(record);
            }
        }

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
            //check if ttl is set
            if (TTL == null)
            {
                TTL = Int32.Parse(parts[0]);
                primaryNameserver = parts[2];
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
            }
            if (parts.Length == 0) return;
            //check if serial
            if (serial == null)
            {
                serial = parts[0];
                //remove this data from the parts array
                parts = parts.Skip(1).ToArray();
            }
            if (parts.Length == 0) return;
            //check if refresh is set
            if (refresh == null)
            {
                refresh = parts[0];
                //remove this data from the parts array
                parts = parts.Skip(1).ToArray();
            }
            if (parts.Length == 0) return;
            //check if retry is set
            if (retry == null)
            {
                retry = parts[0];
                //remove this data from the parts array
                parts = parts.Skip(1).ToArray();
            }
            if (parts.Length == 0) return;
            //check if expire is set
            if (expire == null)
            {
                expire = parts[0];
                //remove this data from the parts array
                parts = parts.Skip(1).ToArray();
            }
            if (parts.Length == 0) return;
            //check if minimumTTL is set
            if (minimumTTL == null)
            {
                minimumTTL = Int32.Parse(parts[0]);
                //remove this data from the parts array
                parts = parts.Skip(1).ToArray();
            }
            if (parts.Length == 0) return;
            if (line.Contains(')'))
            {
                parsingSOA = false;

                //add the SOA record
                Record record = new Record(settings);
                record.name = origin;
                record.ttl = (int)TTL;
                record.type = RTypes.SOA;
                
                //create a bytes list
                byte[] nameserver = Utility.GenerateDomainName(primaryNameserver);
                byte[] hostmaster = Utility.GenerateDomainName(this.hostmaster);
                
                byte[] data = new byte[primaryNameserver.Length + hostmaster.Length + 20];

                int currentPosition = 0;
                //add the primaryNameserver
                for (int i = 0; i < nameserver.Length; i++)
                {
                    data[currentPosition] = nameserver[i];
                    currentPosition++;
                }

                //add null byte
                data[currentPosition] = 0;
                currentPosition++;

                //add the hostmaster
                for (int i = 0; i < hostmaster.Length; i++)
                {
                    data[currentPosition] = hostmaster[i];
                    currentPosition++;
                }

                //add null byte
                data[currentPosition] = 0;
                currentPosition++;

                //add the serial
                data[currentPosition] = (byte)(int.Parse(serial) >> 24);
                data[currentPosition + 1] = (byte)(int.Parse(serial) >> 16);
                data[currentPosition + 2] = (byte)(int.Parse(serial) >> 8);
                data[currentPosition + 3] = (byte)(int.Parse(serial) & 0xFF);
                currentPosition += 4;

                //add the refresh
                data[currentPosition] = (byte)(int.Parse(refresh) >> 24);
                data[currentPosition + 1] = (byte)(int.Parse(refresh) >> 16);
                data[currentPosition + 2] = (byte)(int.Parse(refresh) >> 8);
                data[currentPosition + 3] = (byte)(int.Parse(refresh) & 0xFF);
                currentPosition += 4;

                //add the retry
                data[currentPosition] = (byte)(int.Parse(retry) >> 24);
                data[currentPosition + 1] = (byte)(int.Parse(retry) >> 16);
                data[currentPosition + 2] = (byte)(int.Parse(retry) >> 8);
                data[currentPosition + 3] = (byte)(int.Parse(retry) & 0xFF);
                currentPosition += 4;

                //add the expire
                data[currentPosition] = (byte)(int.Parse(expire) >> 24);
                data[currentPosition + 1] = (byte)(int.Parse(expire) >> 16);
                data[currentPosition + 2] = (byte)(int.Parse(expire) >> 8);
                data[currentPosition + 3] = (byte)(int.Parse(expire) & 0xFF);

                record.data = data;

                records.Add(record);
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

        public BIND(string path, Settings settings)
        {
            this.settings = settings;
            logger = new Logging<BIND>(settings.GetSetting("logging", "path", "./log.txt"), logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5")));
            
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