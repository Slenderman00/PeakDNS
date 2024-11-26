//include regex
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace PeakDNS.DNS.Server
{
    /// <summary>
    /// Represents a DNS resource record.
    /// </summary>
    public class Record
    {
        private Settings settings;
        private Logging<Record> logger;
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        // Public fields maintained for interface compatibility
        public string name;
        public int ttl;
        public RClasses _class = RClasses.IN;
        public RTypes type;
        public ushort priority;
        public byte[] data;

        // Cache of DNS record type mappings
        private static readonly Dictionary<string, RTypes> TypeMap = new Dictionary<string, RTypes>(StringComparer.OrdinalIgnoreCase)
        {
            { "A", RTypes.A },
            { "AAAA", RTypes.AAAA },
            { "CNAME", RTypes.CNAME },
            { "MX", RTypes.MX },
            { "NS", RTypes.NS },
            { "PTR", RTypes.PTR },
            { "SOA", RTypes.SOA },
            { "TXT", RTypes.TXT }
        };

        /// <summary>
        /// Initializes a new instance of the Record class for parsing from a zone file.
        /// </summary>
        /// <param name="settings">The DNS server settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public Record(Settings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            logger = InitializeLogger(settings);
        }

        /// <summary>
        /// Initializes a new instance of the Record class with specific values.
        /// </summary>
        /// <param name="settings">The DNS server settings.</param>
        /// <param name="name">The domain name.</param>
        /// <param name="ttl">The time to live value.</param>
        /// <param name="type">The DNS record type.</param>
        /// <param name="data">The record data.</param>
        /// <param name="priority">The priority (used for MX records).</param>
        public Record(Settings settings, string name, int ttl, RTypes type, byte[] data, ushort priority = 0)
            : this(settings)
        {
            ValidateRecordParameters(name, ttl, data);

            this.name = name;
            this.ttl = ttl;
            this.type = type;
            this.data = data;
            this.priority = priority;
        }

        private Logging<Record> InitializeLogger(Settings settings)
        {
            string logPath = settings.GetSetting("logging", "path", "./log.txt");
            int logLevel = int.Parse(settings.GetSetting("logging", "logLevel", "5"));
            return new Logging<Record>(logPath, logLevel: logLevel);
        }

        private void ValidateRecordParameters(string name, int ttl, byte[] data)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name cannot be empty", nameof(name));
            if (ttl < 0)
                throw new ArgumentException("TTL cannot be negative", nameof(ttl));
            if (data != null && data.Length > 65535)
                throw new ArgumentException("Data exceeds maximum length of 65535 bytes", nameof(data));
        }

        #region Static Factory Methods

        /// <summary>
        /// Creates an A record.
        /// </summary>
        public static Record CreateARecord(Settings settings, string name, int ttl, string ipAddress)
        {
            try
            {
                var data = Utility.ParseIP(ipAddress);
                return new Record(settings, name, ttl, RTypes.A, data);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid IP address format: {ipAddress}", nameof(ipAddress), ex);
            }
        }

        /// <summary>
        /// Creates an AAAA record.
        /// </summary>
        public static Record CreateAAAARecord(Settings settings, string name, int ttl, string ipv6Address)
        {
            try
            {
                var data = Utility.ParseIPv6(ipv6Address);
                return new Record(settings, name, ttl, RTypes.AAAA, data);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Invalid IPv6 address format: {ipv6Address}", nameof(ipv6Address), ex);
            }
        }

        /// <summary>
        /// Creates a CNAME record.
        /// </summary>
        public static Record CreateCNAMERecord(Settings settings, string name, int ttl, string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                throw new ArgumentException("Target cannot be empty", nameof(target));

            var data = Utility.GenerateDomainName(target);
            return new Record(settings, name, ttl, RTypes.CNAME, Utility.addNullByte(data));
        }

        /// <summary>
        /// Creates an MX record.
        /// </summary>
        public static Record CreateMXRecord(Settings settings, string name, int ttl, ushort priority, string mailServer)
        {
            if (string.IsNullOrWhiteSpace(mailServer))
                throw new ArgumentException("Mail server cannot be empty", nameof(mailServer));

            var domainName = Utility.GenerateDomainName(mailServer);
            var data = new byte[domainName.Length + 3];

            data[0] = (byte)(priority >> 8);
            data[1] = (byte)(priority & 0xFF);
            Buffer.BlockCopy(domainName, 0, data, 2, domainName.Length);
            data[^1] = 0;

            return new Record(settings, name, ttl, RTypes.MX, data, priority);
        }

        /// <summary>
        /// Creates a TXT record.
        /// </summary>
        public static Record CreateTXTRecord(Settings settings, string name, int ttl, string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            return new Record(settings, name, ttl, RTypes.TXT, Utility.StringToBytes($"\"{text}\""));
        }

        /// <summary>
        /// Creates an NS record.
        /// </summary>
        public static Record CreateNSRecord(Settings settings, string name, int ttl, string nameServer)
        {
            if (string.IsNullOrWhiteSpace(nameServer))
                throw new ArgumentException("Nameserver cannot be empty", nameof(nameServer));

            var data = Utility.GenerateDomainName(nameServer);
            return new Record(settings, name, ttl, RTypes.NS, Utility.addNullByte(data));
        }

        #endregion

        #region Parsing Methods

        /// <summary>
        /// Gets the DNS record type from its string representation.
        /// </summary>
        public RTypes getTypeByName(string name)
        {
            return TypeMap.TryGetValue(name, out var type) ? type : RTypes.A;
        }

        /// <summary>
        /// Parses a TXT record from a zone file line.
        /// </summary>
        public void parseTXTRecord(string line)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(line))
                    throw new ArgumentException("Line cannot be empty", nameof(line));

                var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    logger.Error($"Invalid TXT record format: {line}");
                    return;
                }

                var headerParts = WhitespaceRegex.Replace(parts[0].Trim(), " ").Split(' ');
                if (headerParts.Length < 2)
                {
                    logger.Error($"Invalid TXT record header: {parts[0]}");
                    return;
                }

                if (!int.TryParse(headerParts[0], out ttl))
                {
                    logger.Error($"Invalid TTL in TXT record: {headerParts[0]}");
                    return;
                }

                type = getTypeByName(headerParts[1]);
                if (type != RTypes.TXT)
                {
                    logger.Error($"Invalid record type for TXT record: {headerParts[1]}");
                    return;
                }

                data = Utility.StringToBytes($"\"{parts[1]}\"");
            }
            catch (Exception ex)
            {
                logger.Error($"Error parsing TXT record: {line}", ex);
            }
        }

        /// <summary>
        /// Parses a DNS record from a zone file line.
        /// </summary>
        public void parseLine(string line)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;

                if (line.Contains('"'))
                {
                    parseTXTRecord(line);
                    return;
                }

                var normalizedLine = WhitespaceRegex.Replace(line.Trim(), " ");
                var parts = normalizedLine.Split(' ');

                if (parts.Length < 3 || parts.Length > 4)
                {
                    logger.Error($"Invalid record format: {line}");
                    return;
                }

                ParseRecordParts(parts);
            }
            catch (Exception ex)
            {
                logger.Error($"Error parsing line: {line}", ex);
            }
        }

        private void ParseRecordParts(string[] parts)
        {
            try
            {
                if (parts.Length == 3)
                {
                    ParseThreePartRecord(parts);
                }
                else if (parts.Length == 4)
                {
                    ParseFourPartRecord(parts);
                }
            }
            catch (Exception ex)
            {
                logger.Error("Error parsing record parts", ex);
                throw;
            }
        }

        private void ParseThreePartRecord(string[] parts)
        {
            if (!int.TryParse(parts[0], out ttl))
            {
                logger.Error($"Invalid TTL: {parts[0]}");
                return;
            }

            type = getTypeByName(parts[1]);

            switch (type)
            {
                case RTypes.A:
                    data = Utility.ParseIP(parts[2]);
                    break;
                case RTypes.NS:
                    data = Utility.addNullByte(Utility.GenerateDomainName(parts[2]));
                    break;
                case RTypes.MX:
                    logger.Debug("Three-part MX record not supported");
                    break;
                default:
                    logger.Error($"Unsupported record type: {parts[1]}");
                    break;
            }
        }

        private void ParseFourPartRecord(string[] parts)
        {
            try
            {
                if (getTypeByName(parts[1]) == RTypes.MX)
                {
                    ParseMXRecord(parts);
                }
                else
                {
                    ParseStandardFourPartRecord(parts);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error parsing four-part record: {string.Join(' ', parts)}", ex);
                throw;
            }
        }

        private void ParseMXRecord(string[] parts)
        {
            if (!int.TryParse(parts[0], out ttl))
            {
                logger.Error($"Invalid TTL in MX record: {parts[0]}");
                return;
            }

            type = RTypes.MX;

            if (!ushort.TryParse(parts[2], out priority))
            {
                logger.Error($"Invalid priority in MX record: {parts[2]}");
                return;
            }

            var domainName = Utility.GenerateDomainName(parts[3]);
            data = new byte[domainName.Length + 3];
            data[0] = (byte)(priority >> 8);
            data[1] = (byte)(priority & 0xFF);
            Buffer.BlockCopy(domainName, 0, data, 2, domainName.Length);
        }

        private void ParseStandardFourPartRecord(string[] parts)
        {
            name = parts[0];

            if (!int.TryParse(parts[1], out ttl))
            {
                logger.Error($"Invalid TTL: {parts[1]}");
                return;
            }

            type = getTypeByName(parts[2]);

            try
            {
                switch (type)
                {
                    case RTypes.A:
                        data = Utility.ParseIP(parts[3]);
                        break;
                    case RTypes.AAAA:
                        data = Utility.ParseIPv6(parts[3]);
                        break;
                    case RTypes.CNAME:
                    case RTypes.PTR:
                        data = Utility.addNullByte(Utility.GenerateDomainName(parts[3]));
                        break;
                    default:
                        data = Utility.StringToBytes(parts[3]);
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error parsing record data: {parts[3]}", ex);
                throw;
            }
        }

        #endregion

        /// <summary>
        /// Prints the record details to the log.
        /// </summary>
        public void Print()
        {
            logger.Debug($"Record details:");
            logger.Debug($"  Name: {name}");
            logger.Debug($"  TTL: {ttl}");
            logger.Debug($"  Class: {_class}");
            logger.Debug($"  Type: {type}");
            logger.Debug($"  Priority: {priority}");

            if (data != null)
            {
                logger.Debug($"  Data: {BitConverter.ToString(data).Replace("-", " ")}");
            }
        }
    }

    /// <summary>
    /// Represents a DNS zone with its records and SOA information.
    /// </summary>
    public class BIND
    {
        private readonly Settings settings;
        private Logging<BIND> logger;
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private readonly ReaderWriterLockSlim recordsLock = new ReaderWriterLockSlim();
        private readonly ConcurrentDictionary<RTypes, HashSet<Record>> recordsByType;

        // Public fields maintained for interface compatibility
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

        private bool parsingSOA;

        /// <summary>
        /// Initializes a new instance of the BIND class from a zone file.
        /// </summary>
        public BIND(string path, Settings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            recordsByType = new ConcurrentDictionary<RTypes, HashSet<Record>>();

            InitializeLogging();
            InitializeRecords();

            try
            {
                ParseZoneFile(path);
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to parse zone file: {path}", ex);
                throw;
            }
        }

        /// <summary>
        /// Initializes a new instance of the BIND class for programmatic use.
        /// </summary>
        public BIND(Settings settings, string origin)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.origin = origin ?? throw new ArgumentNullException(nameof(origin));

            recordsByType = new ConcurrentDictionary<RTypes, HashSet<Record>>();

            InitializeLogging();
            InitializeRecords();
        }

        private void InitializeLogging()
        {
            string logPath = settings.GetSetting("logging", "path", "./log.txt");
            int logLevel = int.Parse(settings.GetSetting("logging", "logLevel", "5"));
            logger = new Logging<BIND>(logPath, logLevel: logLevel);
        }

        private void InitializeRecords()
        {
            records = new List<Record>();
            foreach (RTypes type in Enum.GetValues(typeof(RTypes)))
            {
                recordsByType[type] = new HashSet<Record>();
            }
        }

        /// <summary>
        /// Adds a record to the zone.
        /// </summary>
        public void AddRecord(Record record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            try
            {
                recordsLock.EnterWriteLock();
                records.Add(record);

                if (!recordsByType.TryGetValue(record.type, out var typeRecords))
                {
                    typeRecords = new HashSet<Record>();
                    recordsByType[record.type] = typeRecords;
                }
                typeRecords.Add(record);
            }
            finally
            {
                recordsLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Sets up the SOA record for the zone.
        /// </summary>
        public void SetSOARecord(string primaryNameserver, string hostmaster, string serial,
            string refresh, string retry, string expire, int ttl, int minimumTTL)
        {
            ValidateSOAParameters(primaryNameserver, hostmaster, serial, refresh, retry, expire, ttl, minimumTTL);

            this.primaryNameserver = primaryNameserver;
            this.hostmaster = hostmaster;
            this.serial = serial;
            this.refresh = refresh;
            this.retry = retry;
            this.expire = expire;
            this.TTL = ttl;
            this.minimumTTL = minimumTTL;

            CreateAndAddSOARecord(ttl);
        }

        private void ValidateSOAParameters(string primaryNameserver, string hostmaster, string serial,
            string refresh, string retry, string expire, int ttl, int minimumTTL)
        {
            if (string.IsNullOrWhiteSpace(primaryNameserver))
                throw new ArgumentException("Primary nameserver cannot be empty", nameof(primaryNameserver));
            if (string.IsNullOrWhiteSpace(hostmaster))
                throw new ArgumentException("Hostmaster cannot be empty", nameof(hostmaster));
            if (string.IsNullOrWhiteSpace(serial))
                throw new ArgumentException("Serial cannot be empty", nameof(serial));
            if (ttl < 0)
                throw new ArgumentException("TTL cannot be negative", nameof(ttl));
            if (minimumTTL < 0)
                throw new ArgumentException("Minimum TTL cannot be negative", nameof(minimumTTL));

            // Validate that all numeric parameters can be parsed
            if (!int.TryParse(serial, out _))
                throw new ArgumentException("Invalid serial number format", nameof(serial));
            if (!int.TryParse(refresh, out _))
                throw new ArgumentException("Invalid refresh value", nameof(refresh));
            if (!int.TryParse(retry, out _))
                throw new ArgumentException("Invalid retry value", nameof(retry));
            if (!int.TryParse(expire, out _))
                throw new ArgumentException("Invalid expire value", nameof(expire));
        }

        private void CreateAndAddSOARecord(int ttl)
        {
            try
            {
                byte[] nameserver = Utility.GenerateDomainName(primaryNameserver);
                byte[] hostmasterBytes = Utility.GenerateDomainName(hostmaster);

                int dataLength = nameserver.Length + hostmasterBytes.Length + 22; // 20 bytes for numbers + 2 null terminators
                byte[] data = new byte[dataLength];
                int position = 0;

                // Add nameserver
                Buffer.BlockCopy(nameserver, 0, data, position, nameserver.Length);
                position += nameserver.Length;
                data[position++] = 0;

                // Add hostmaster
                Buffer.BlockCopy(hostmasterBytes, 0, data, position, hostmasterBytes.Length);
                position += hostmasterBytes.Length;
                data[position++] = 0;

                // Add numeric fields
                WriteInt32ToBuffer(data, ref position, int.Parse(serial));
                WriteInt32ToBuffer(data, ref position, int.Parse(refresh));
                WriteInt32ToBuffer(data, ref position, int.Parse(retry));
                WriteInt32ToBuffer(data, ref position, int.Parse(expire));

                Record soaRecord = new Record(settings, origin, ttl, RTypes.SOA, data);
                AddRecord(soaRecord);
            }
            catch (Exception ex)
            {
                logger.Error("Failed to create SOA record", ex);
                throw;
            }
        }

        private static void WriteInt32ToBuffer(byte[] buffer, ref int position, int value)
        {
            buffer[position++] = (byte)(value >> 24);
            buffer[position++] = (byte)(value >> 16);
            buffer[position++] = (byte)(value >> 8);
            buffer[position++] = (byte)(value);
        }

        private bool MatchesDomain(string recordName, string questionDomain)
        {
            // If record name is null/empty, use zone origin
            if (string.IsNullOrEmpty(recordName))
                return string.Equals(questionDomain, origin, StringComparison.OrdinalIgnoreCase);

            // Direct match
            if (string.Equals(recordName, questionDomain, StringComparison.OrdinalIgnoreCase))
                return true;

            // If record doesn't end with a dot, append the origin
            if (!recordName.EndsWith("."))
                return string.Equals($"{recordName}.{origin}", questionDomain, StringComparison.OrdinalIgnoreCase);

            return false;
        }

        /// <summary>
        /// Gets the answers for a DNS question.
        /// </summary>
        public Answer[] getAnswers(Question question)
        {
            try
            {
                string questionDomain = question.GetDomainName().ToLowerInvariant();

                recordsLock.EnterReadLock();
                try
                {
                    logger.Debug($"Looking for records matching domain: {questionDomain}, type: {question.type}");

                    var matchingRecords = records
                        .Where(record =>
                        {
                            bool typeMatches = record.type == question.type;
                            bool domainMatches = MatchesDomain(record.name, questionDomain);

                            logger.Debug($"Checking record - Name: '{record.name}', Type: {record.type}, " +
                                       $"TypeMatch: {typeMatches}, DomainMatch: {domainMatches}");

                            return typeMatches && domainMatches;
                        })
                        .ToList();

                    logger.Debug($"Found {matchingRecords.Count} matching records");

                    if (!matchingRecords.Any())
                    {
                        logger.Debug("No matching records found");
                        return null;
                    }

                    var answers = matchingRecords
                        .Select(record => new Answer
                        {
                            domainName = record.name ?? questionDomain,
                            answerType = record.type,
                            answerClass = record._class,
                            ttl = record.ttl,
                            dataLength = (uint)(record.data?.Length ?? 0),
                            rData = record.data ?? Array.Empty<byte>()
                        })
                        .ToArray();

                    logger.Debug($"Created {answers.Length} answer records");
                    return answers;
                }
                finally
                {
                    recordsLock.ExitReadLock();
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error getting answers: {ex}");
                return null;
            }
        }

        private Answer CreateAnswer(Record record)
        {
            if (record.data == null && record.type != RTypes.SOA)
                return null;

            return new Answer
            {
                domainName = record.name,
                answerType = record.type,
                answerClass = record._class,
                ttl = record.ttl,
                dataLength = (uint)record.data.Length,
                rData = record.data ?? new byte[0]
            };
        }

        /// <summary>
        /// Removes a record from the zone.
        /// </summary>
        public void RemoveRecord(Record record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            try
            {
                recordsLock.EnterWriteLock();
                records.Remove(record);

                if (recordsByType.TryGetValue(record.type, out var typeRecords))
                {
                    typeRecords.Remove(record);
                }
            }
            finally
            {
                recordsLock.ExitWriteLock();
            }
        }


        /// <summary>
        /// Checks if this zone can answer the given question.
        /// </summary>
        public bool canAnwser(Question question)
        {
            try
            {
                string questionDomain = question.GetDomainName().ToLowerInvariant();
                string zoneOrigin = origin.ToLowerInvariant();

                // Log the comparison
                logger.Debug($"Comparing question domain '{questionDomain}' with zone origin '{zoneOrigin}'");

                // Exact match
                if (questionDomain == zoneOrigin)
                {
                    logger.Debug("Exact match found");
                    return HasRecordsOfType(question.type);
                }

                // Check if question is a subdomain of our zone
                if (questionDomain.EndsWith("." + zoneOrigin))
                {
                    logger.Debug("Subdomain match found");
                    return HasRecordsOfType(question.type);
                }

                // Wildcard match (if zone origin starts with *)
                if (zoneOrigin.StartsWith("*."))
                {
                    string baseDomain = zoneOrigin.Substring(2);
                    if (questionDomain.EndsWith(baseDomain))
                    {
                        logger.Debug("Wildcard match found");
                        return HasRecordsOfType(question.type);
                    }
                }

                logger.Debug("No match found");
                return false;
            }
            catch (Exception ex)
            {
                logger.Error($"Error in canAnwser: {ex}");
                return false;
            }
        }

        private bool HasRecordsOfType(RTypes type)
        {
            try
            {
                recordsLock.EnterReadLock();
                if (recordsByType.TryGetValue(type, out var typeRecords))
                {
                    bool hasRecords = typeRecords.Any();
                    logger.Debug($"Found {typeRecords.Count} records of type {type}");
                    return hasRecords;
                }
                logger.Debug($"No records found of type {type}");
                return false;
            }
            finally
            {
                recordsLock.ExitReadLock();
            }
        }

        private void ParseZoneFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Zone file not found", path);

            string[] lines = File.ReadAllLines(path);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
                    continue;

                try
                {
                    parseLine(line);
                }
                catch (Exception ex)
                {
                    logger.Error($"Error parsing line: {line}", ex);
                }
            }
        }

        private void parseLine(string line)
        {
            try
            {
                if (line.StartsWith("@"))
                {
                    parsingSOA = true;
                    parseSOA(line);
                }
                else if (!parsingSOA)
                {
                    parseRecord(line);
                }
                else
                {
                    parseSOA(line);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error parsing line: {line}", ex);
                throw;
            }
        }

        private void parseRecord(string line)
        {
            string[] parts = WhitespaceRegex.Replace(line.Trim(), " ").Split(' ');

            if (parts[0] == "$ORIGIN")
            {
                if (parts.Length < 2)
                {
                    logger.Error("Invalid $ORIGIN directive");
                    return;
                }
                origin = parts[1];
            }
            else
            {
                Record record = new Record(settings);
                record.parseLine(line);
                AddRecord(record);
            }
        }

        private void parseSOA(string line)
        {
            try
            {
                // Remove comments
                line = line.Split(';')[0];

                // Clean up the line
                line = line.Replace("(", "").Trim();
                line = WhitespaceRegex.Replace(line, " ");

                if (line.StartsWith("@"))
                {
                    line = line.Substring(1);
                }

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return;

                ParseSOAParts(parts);

                if (line.Contains(')'))
                {
                    FinishSOAParsing();
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Error parsing SOA: {line}", ex);
                throw;
            }
        }

        private void ParseSOAParts(string[] parts)
        {
            if (TTL == null && parts.Length >= 3)
            {
                TTL = int.Parse(parts[0]);
                primaryNameserver = parts[2];
                return;
            }

            int index = 0;
            if (hostmaster == null && parts.Length > index)
            {
                hostmaster = parts[index++];
            }
            if (serial == null && parts.Length > index)
            {
                serial = parts[index++];
            }
            if (refresh == null && parts.Length > index)
            {
                refresh = parts[index++];
            }
            if (retry == null && parts.Length > index)
            {
                retry = parts[index++];
            }
            if (expire == null && parts.Length > index)
            {
                expire = parts[index++];
            }
            if (minimumTTL == null && parts.Length > index)
            {
                minimumTTL = int.Parse(parts[index]);
            }
        }

        private void FinishSOAParsing()
        {
            parsingSOA = false;
            CreateAndAddSOARecord((int)TTL);
        }

        /// <summary>
        /// Prints the zone information to the log.
        /// </summary>
        public void Print()
        {
            logger.Debug($"=== Zone {origin} Records ===");
            foreach (var record in records)
            {
                logger.Debug($"Record:");
                logger.Debug($"  Name: '{record.name}'");
                logger.Debug($"  Type: {record.type}");
                logger.Debug($"  TTL: {record.ttl}");
                if (record.data != null)
                {
                    logger.Debug($"  Data: {BitConverter.ToString(record.data)}");
                }
            }
            logger.Debug("=== End Records ===");
        }

        public void Dispose()
        {
            recordsLock?.Dispose();
        }
    }
}