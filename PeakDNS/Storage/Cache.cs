using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using PeakDNS.DNS;

namespace PeakDNS.Storage
{
    public class CacheKey
    {
        public string DomainName { get; }
        public RTypes Type { get; }
        public RClasses Class { get; }

        public CacheKey(Question question)
        {
            DomainName = question.GetDomainName().ToLowerInvariant();
            Type = question.type;
            Class = question._class;
        }

        public override bool Equals(object obj)
        {
            if (obj is not CacheKey other)
                return false;

            return DomainName == other.DomainName &&
                   Type == other.Type &&
                   Class == other.Class;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(DomainName, Type, Class);
        }
    }

    public class CacheEntry
    {
        public Question Question { get; }
        public Answer[] Answers { get; }
        private readonly long expirationTime;
        private readonly Logging<CacheEntry> logger;

        public bool IsExpired => DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expirationTime;

        public CacheEntry(Packet packet, Settings settings)
        {
            logger = new Logging<CacheEntry>(
                settings.GetSetting("logging", "path", "./log.txt"),
                logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5"))
            );

            if (packet.answerCount < 1 || packet.questionCount < 1)
                throw new ArgumentException("Packet must contain at least one question and answer");

            Question = packet.questions[0];
            Answers = packet.answers;

            // Find minimum TTL from all answers
            uint minTTL = uint.MaxValue;
            foreach (var answer in Answers)
            {
                if (answer.ttl < minTTL)
                    minTTL = (uint)answer.ttl;
            }

            // Set expiration time (convert TTL to Unix timestamp)
            expirationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + minTTL;
            
            logger.Debug($"Created cache entry for {Question.GetDomainName()} with TTL {minTTL}s");
        }
    }

    public class Cache : IDisposable
    {
        private readonly ConcurrentDictionary<CacheKey, CacheEntry> entries;
        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly Settings settings;
        private readonly Logging<Cache> logger;
        private Task cleanupTask;
        private readonly int cleanupIntervalMs;
        private readonly int maxEntries;

        public Cache(Settings settings)
        {
            this.settings = settings;
            logger = new Logging<Cache>(
                settings.GetSetting("logging", "path", "./log.txt"),
                logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5"))
            );

            entries = new ConcurrentDictionary<CacheKey, CacheEntry>();
            cancellationTokenSource = new CancellationTokenSource();
            
            // Get configuration from settings
            cleanupIntervalMs = int.Parse(settings.GetSetting("cache", "cleanupInterval", "1000"));
            maxEntries = int.Parse(settings.GetSetting("cache", "maxEntries", "10000"));
            
            logger.Info($"Cache initialized with cleanup interval {cleanupIntervalMs}ms and max entries {maxEntries}");
        }

        public void Clear()
        {
            entries.Clear();
            logger.Info("Cache cleared");
        }

        public void AddRecord(Packet packet)
        {
            try
            {
                if (packet.answers.Length < 1 || packet.questions.Length < 1)
                {
                    logger.Debug("Skipping cache addition - packet has no answers or questions");
                    return;
                }

                var key = new CacheKey(packet.questions[0]);
                var entry = new CacheEntry(packet, settings);

                // If we're at capacity, remove some entries
                if (entries.Count >= maxEntries)
                {
                    RemoveExpiredEntries();
                    // If still at capacity, remove random entries
                    while (entries.Count >= maxEntries)
                    {
                        var randomKey = entries.Keys.FirstOrDefault();
                        if (randomKey != null)
                        {
                            entries.TryRemove(randomKey, out _);
                        }
                    }
                }

                entries.AddOrUpdate(key, entry, (_, __) => entry);
                logger.Debug($"Added/Updated cache entry for {key.DomainName}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error adding record to cache: {ex}");
            }
        }

        public bool HasAnswer(Packet packet)
        {
            if (packet.questions.Length < 1)
                return false;

            var key = new CacheKey(packet.questions[0]);
            return entries.TryGetValue(key, out var entry) && !entry.IsExpired;
        }

        public Answer[] GetAnswers(Packet packet)
        {
            try
            {
                if (packet.questions.Length < 1)
                    return Array.Empty<Answer>();

                var key = new CacheKey(packet.questions[0]);
                if (entries.TryGetValue(key, out var entry))
                {
                    if (!entry.IsExpired)
                    {
                        logger.Debug($"Cache hit for {key.DomainName}");
                        return entry.Answers;
                    }
                    else
                    {
                        logger.Debug($"Cache entry expired for {key.DomainName}");
                        entries.TryRemove(key, out _);
                    }
                }

                logger.Debug($"Cache miss for {key.DomainName}");
                return Array.Empty<Answer>();
            }
            catch (Exception ex)
            {
                logger.Error($"Error getting answers from cache: {ex}");
                return Array.Empty<Answer>();
            }
        }

        private void RemoveExpiredEntries()
        {
            try
            {
                var expiredKeys = entries
                    .Where(kvp => kvp.Value.IsExpired)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    entries.TryRemove(key, out _);
                    logger.Debug($"Removed expired entry for {key.DomainName}");
                }

                if (expiredKeys.Count != 0) logger.Debug($"Removed {expiredKeys.Count} expired entries. Current cache size: {entries.Count}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error removing expired entries: {ex}");
            }
        }

        public void Start()
        {
            cleanupTask = Task.Run(async () =>
            {
                logger.Info("Cache cleanup task started");
                while (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        RemoveExpiredEntries();
                        await Task.Delay(cleanupIntervalMs, cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Error in cache cleanup task: {ex}");
                    }
                }
                logger.Info("Cache cleanup task stopped");
            }, cancellationTokenSource.Token);
        }

        public void Stop()
        {
            try
            {
                cancellationTokenSource.Cancel();
                cleanupTask?.Wait();
                logger.Info("Cache stopped");
            }
            catch (Exception ex)
            {
                logger.Error($"Error stopping cache: {ex}");
            }
        }

        public void Dispose()
        {
            Stop();
            cancellationTokenSource.Dispose();
            cleanupTask?.Dispose();
        }

        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics
            {
                TotalEntries = entries.Count,
                ExpiredEntries = entries.Count(kvp => kvp.Value.IsExpired),
                CacheSize = entries.Sum(kvp => 
                    kvp.Key.DomainName.Length + 
                    kvp.Value.Answers.Sum(a => a.rData?.Length ?? 0))
            };
        }
    }

    public class CacheStatistics
    {
        public int TotalEntries { get; set; }
        public int ExpiredEntries { get; set; }
        public long CacheSize { get; set; }
    }
}