using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PeakDNS.DNS;

namespace PeakDNS.DNS.Server
{
    /// <summary>
    /// Represents a DNS transaction with timeout and retry capabilities
    /// </summary>
    public class Transaction : IDisposable
    {
        private readonly Settings settings;
        private readonly Logging<Transaction> logger;
        private readonly UdpClient udpClient;
        private readonly CancellationTokenSource cancellationTokenSource;
        private readonly int timeoutMs;
        private readonly int maxRetries;
        private int retryCount;
        private bool disposed;

        public Packet Packet { get; }
        public IPEndPoint Server { get; }
        public ushort TransactionId { get; }
        public DateTime LastAttempt { get; private set; }
        public bool IsComplete { get; private set; }
        public Action<Packet> Callback { get; }

        public Transaction(Packet packet, IPEndPoint server, Action<Packet> callback, Settings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.Packet = packet ?? throw new ArgumentNullException(nameof(packet));
            this.Server = server ?? throw new ArgumentNullException(nameof(server));
            this.Callback = callback ?? throw new ArgumentNullException(nameof(callback));

            logger = new Logging<Transaction>(
                settings.GetSetting("logging", "path", "./log.txt"),
                logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5"))
            );

            TransactionId = packet.GetTransactionId();
            LastAttempt = DateTime.UtcNow;
            timeoutMs = int.Parse(settings.GetSetting("requester", "timeoutMs", "2000"));
            maxRetries = int.Parse(settings.GetSetting("requester", "maxRetries", "3"));

            udpClient = new UdpClient();
            udpClient.Client.ReceiveTimeout = timeoutMs;
            cancellationTokenSource = new CancellationTokenSource();

            logger.Info($"Created transaction {TransactionId} for {server}");
        }

        public async Task SendRequestAsync()
        {
            try
            {
                var requestData = Packet.ToBytes();
                await udpClient.SendAsync(requestData, requestData.Length, Server);
                LastAttempt = DateTime.UtcNow;
                retryCount++;

                logger.Debug($"Sent request {TransactionId} (attempt {retryCount}/{maxRetries})");
            }
            catch (Exception ex)
            {
                logger.Error($"Error sending request {TransactionId}: {ex.Message}");
                throw;
            }
        }

        public async Task ProcessResponseAsync()
        {
            try
            {
                using var timeoutCts = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    timeoutCts.Token, cancellationTokenSource.Token);

                var receiveTask = udpClient.ReceiveAsync();
                if (await Task.WhenAny(receiveTask, Task.Delay(timeoutMs, linkedCts.Token)) == receiveTask)
                {
                    var result = await receiveTask;
                    var responsePacket = new Packet(settings);
                    responsePacket.Load(result.Buffer, false);

                    if (responsePacket.GetTransactionId() == TransactionId)
                    {
                        logger.Success($"Received response for {TransactionId} from {result.RemoteEndPoint}");
                        IsComplete = true;
                        Callback(responsePacket);
                    }
                }
                else if (retryCount < maxRetries)
                {
                    logger.Warning($"Timeout for {TransactionId}, retrying ({retryCount}/{maxRetries})");
                    await SendRequestAsync();
                }
                else
                {
                    logger.Error($"Transaction {TransactionId} failed after {maxRetries} attempts");
                    IsComplete = true;
                    // Create error response packet
                    var errorPacket = new Packet(settings);
                    errorPacket.Load(Packet.packet, false);
                    errorPacket.flagpole.RCode = RCodes.SERVFAIL;
                    Callback(errorPacket);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.Error($"Error processing response for {TransactionId}: {ex.Message}");
                throw;
            }
        }

        public void Cancel()
        {
            cancellationTokenSource.Cancel();
        }

        public void Dispose()
        {
            if (!disposed)
            {
                cancellationTokenSource.Dispose();
                udpClient.Dispose();
                disposed = true;
            }
        }
    }

    public class RecordRequester : IDisposable
    {
        private readonly Settings settings;
        private readonly Logging<RecordRequester> logger;
        private readonly ConcurrentDictionary<ushort, Transaction> transactions;
        private readonly CancellationTokenSource cancellationTokenSource;
        private Task processingTask;
        private bool disposed;

        private readonly int maxConcurrentTransactions;
        private readonly SemaphoreSlim concurrencyLimiter;

        public RecordRequester(Settings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

            logger = new Logging<RecordRequester>(
                settings.GetSetting("logging", "path", "./log.txt"),
                logLevel: int.Parse(settings.GetSetting("logging", "logLevel", "5"))
            );

            transactions = new ConcurrentDictionary<ushort, Transaction>();
            cancellationTokenSource = new CancellationTokenSource();

            maxConcurrentTransactions = int.Parse(settings.GetSetting("requester", "maxConcurrent", "100"));
            concurrencyLimiter = new SemaphoreSlim(maxConcurrentTransactions);

            logger.Info($"Record requester initialized with max {maxConcurrentTransactions} concurrent transactions");
        }

        public async Task RequestRecordAsync(Packet packet, IPEndPoint server, Action<Packet> callback)
        {
            await concurrencyLimiter.WaitAsync();

            try
            {
                // Create the callback first
                Action<Packet> wrappedCallback = null;
                wrappedCallback = response =>
                {
                    try
                    {
                        callback(response);
                    }
                    finally
                    {
                        if (transactions.TryGetValue(packet.GetTransactionId(), out var trans))
                        {
                            CleanupTransaction(trans);
                        }
                    }
                };

                // Create transaction with the wrapped callback
                var transaction = new Transaction(packet, server, wrappedCallback, settings);

                if (!transactions.TryAdd(transaction.TransactionId, transaction))
                {
                    throw new InvalidOperationException($"Transaction ID {transaction.TransactionId} already exists");
                }

                await transaction.SendRequestAsync();
            }
            catch (Exception ex)
            {
                logger.Error($"Error creating transaction: {ex.Message}");
                concurrencyLimiter.Release();
                throw;
            }
        }

        // For backward compatibility
        public void RequestRecord(Packet packet, IPEndPoint server, Action<Packet> callback)
        {
            RequestRecordAsync(packet, server, callback).GetAwaiter().GetResult();
        }

        private void CleanupTransaction(Transaction transaction)
        {
            if (transactions.TryRemove(transaction.TransactionId, out _))
            {
                transaction.Dispose();
                concurrencyLimiter.Release();
                logger.Debug($"Cleaned up transaction {transaction.TransactionId}");
            }
        }

        private async Task ProcessTransactionsAsync()
        {
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    var processingTasks = transactions.Values
                        .Where(t => !t.IsComplete)
                        .Select(t => ProcessTransactionAsync(t))
                        .ToList();

                    if (processingTasks.Any())
                    {
                        await Task.WhenAll(processingTasks);
                    }
                    else
                    {
                        await Task.Delay(10, cancellationTokenSource.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.Error($"Error processing transactions: {ex.Message}");
                }
            }
        }

        private async Task ProcessTransactionAsync(Transaction transaction)
        {
            try
            {
                await transaction.ProcessResponseAsync();
            }
            catch (Exception ex)
            {
                logger.Error($"Error processing transaction {transaction.TransactionId}: {ex.Message}");
                CleanupTransaction(transaction);
            }
        }

        public void Start()
        {
            processingTask = Task.Run(ProcessTransactionsAsync);
            logger.Info("Record requester started");
        }

        public async Task StopAsync()
        {
            if (!disposed)
            {
                cancellationTokenSource.Cancel();

                if (processingTask != null)
                {
                    await processingTask;
                }

                foreach (var transaction in transactions.Values)
                {
                    transaction.Cancel();
                    transaction.Dispose();
                }

                transactions.Clear();
                logger.Info("Record requester stopped");
            }
        }

        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (!disposed)
            {
                Stop();
                cancellationTokenSource.Dispose();
                concurrencyLimiter.Dispose();
                disposed = true;
            }
        }

        public RecordRequesterStatistics GetStatistics()
        {
            return new RecordRequesterStatistics
            {
                ActiveTransactions = transactions.Count,
                AvailableSlots = concurrencyLimiter.CurrentCount
            };
        }

        public class RecordRequesterStatistics
        {
            public int ActiveTransactions { get; set; }
            public int AvailableSlots { get; set; }
        }
    }
}