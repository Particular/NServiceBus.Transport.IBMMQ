namespace NServiceBus.Transport.IbmMq;

using System.Collections.Concurrent;
using Logging;
using IBM.WMQ;

sealed record MessagePumpSettings(TimeSpan MessageWaitInterval, TransportTransactionMode TransactionMode);

sealed class MessagePumpWorker(
    ILog log,
    MessagePumpSettings settings,
    CreateQueueManager createConnection,
    Action<string, Exception, CancellationToken> criticalError
) : IAsyncDisposable
{
    const int ReconnectBaseDelayMs = 1000;
    const int ReconnectMaxDelayMs = 60_000;
    readonly int messageWaitInterval = (int)settings.MessageWaitInterval.TotalMilliseconds;
    readonly TransportTransactionMode transactionMode = settings.TransactionMode;
    readonly CancellationTokenSource stopCts = new();
    readonly CancellationTokenSource cancellationCts = new();
    MQQueueManager? _connection = createConnection();
    Task? pumpTask;

    string queueName = null!;
    OnMessage onMessage = null!;
    OnError onError = null!;
    int workerIndex;

    // Shared across all workers for the same receiver to track failed messages
    // for SendsAtomicWithReceive error handling
    ConcurrentDictionary<string, (Exception Exception, Extensibility.ContextBag ContextBag)>? failedMessages;

    // Shared across all workers to track per-message failure counts for ReceiveOnly/None modes.
    // Mirrors MSMQ transport's MsmqFailureInfoStorage pattern.
    ConcurrentDictionary<string, int>? failureCounts;

    public void Initialize(
        string queueName, OnMessage onMessage, OnError onError, int workerIndex,
        ConcurrentDictionary<string, (Exception Exception, Extensibility.ContextBag ContextBag)>? failedMessages = null,
        ConcurrentDictionary<string, int>? failureCounts = null)
    {
        this.queueName = queueName;
        this.onMessage = onMessage;
        this.onError = onError;
        this.workerIndex = workerIndex;
        this.failedMessages = failedMessages;
        this.failureCounts = failureCounts;
    }

    public void Start()
    {
        log.DebugFormat("Worker {0} starting for queue {1}", workerIndex, queueName);
        // Don't pass cancellation token to Task.Run to avoid race condition
        pumpTask = Task.Run(() => PumpMessages(cancellationCts.Token));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        log.DebugFormat("Worker {0} stopping for queue {1}", workerIndex, queueName);

        // If a cancellation token is provided, link it to message processing cancellation
        // This allows StopReceive to control whether in-flight messages can be cancelled
        CancellationTokenRegistration registration = default;
        try
        {
            if (cancellationToken.CanBeCanceled)
            {
                var cancellationCtsClone = cancellationCts; // Capture to avoid closure issues
                registration = cancellationToken.Register(() => cancellationCtsClone.Cancel());
            }

            await stopCts.CancelAsync()
                .ConfigureAwait(false);

            if (pumpTask != null)
            {
                await pumpTask
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await registration.DisposeAsync()
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        cancellationCts.Dispose();
        stopCts.Dispose();
        DisconnectAndDispose(_connection);

        log.DebugFormat("Worker {0} disposed", workerIndex);
    }

    async Task PumpMessages(CancellationToken cancellationToken)
    {
        MQQueue? queue = null;

        // Log when cancellation is requested while blocked on a non-cancellable IBM MQ operation
        var cancellationLogging = cancellationToken.Register(() =>
            log.WarnFormat("Worker {0} cancellation requested, waiting for IBM MQ operation to complete on {1}", workerIndex, queueName));

        try
        {
            log.DebugFormat("Worker {0} started pumping messages from {1}", workerIndex, queueName);

            var getOptionsFlags = MQC.MQGMO_WAIT
                                 | MQC.MQGMO_FAIL_IF_QUIESCING
                                 | MQC.MQGMO_PROPERTIES_IN_HANDLE;

            if (transactionMode != TransportTransactionMode.None)
            {
                getOptionsFlags |= MQC.MQGMO_SYNCPOINT;
            }

            MQGetMessageOptions getOptions = new()
            {
                Options = getOptionsFlags,
                WaitInterval = messageWaitInterval
            };

            int reconnectAttempt = 0;

            while (!stopCts.IsCancellationRequested)
            {
                MQMessage receivedMessage = new();

                var transportTransaction = new TransportTransaction();

                try
                {
                    if (_connection == null)
                    {
                        log.DebugFormat("Worker {0} creating queue connection {1}", workerIndex, queueName);
                        _connection = createConnection();
                    }

                    queue ??= _connection.AccessQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF);

                    string messageId = string.Empty;
                    byte[] messageBody = [];
                    Dictionary<string, string> messageHeaders = [];
                    Dictionary<string, string> originalHeaders = [];

                    var contextBag = new Extensibility.ContextBag();

                    try
                    {
                        queue.Get(receivedMessage, getOptions);
                        messageBody = IbmMqMessageConverter.FromNative(receivedMessage, messageHeaders, ref messageId);
                        originalHeaders = new Dictionary<string, string>(messageHeaders);

                        log.DebugFormat("Worker {0} received message {1}", workerIndex, messageId);

                        // For SendsAtomicWithReceive, check if this message previously failed
                        // and needs error handling instead of reprocessing
                        if (transactionMode == TransportTransactionMode.SendsAtomicWithReceive
                            && failedMessages != null
                            && failedMessages.TryRemove(messageId, out var failedEntry))
                        {
                            log.DebugFormat("Worker {0} handling previously failed message {1}", workerIndex, messageId);

                            int failures = (receivedMessage.BackoutCount + 1) / 2;
                            await HandleSendsAtomicWithReceiveOnError(
                                failedEntry.Exception, originalHeaders, messageId, messageBody,
                                failures, failedEntry.ContextBag, cancellationToken
                            ).ConfigureAwait(false);

                            reconnectAttempt = 0;
                            continue;
                        }

                        if (transactionMode == TransportTransactionMode.SendsAtomicWithReceive)
                        {
                            transportTransaction.Set(_connection);
                        }

                        var messageContext = new MessageContext(
                            messageId,
                            messageHeaders,
                            messageBody,
                            transportTransaction,
                            queueName,
                            contextBag
                        );

                        await onMessage(messageContext, cancellationToken).ConfigureAwait(false);

                        if (transactionMode != TransportTransactionMode.None)
                        {
                            _connection.Commit();
                        }

                        failureCounts?.TryRemove(messageId, out _);
                        reconnectAttempt = 0;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is not MQException)
                    {
                        log.DebugFormat("Worker {0} error processing message from {1}\n{2}", workerIndex, queueName, ex);

                        if (transactionMode == TransportTransactionMode.SendsAtomicWithReceive
                            && failedMessages != null)
                        {
                            // Store the exception and context for when the message is re-delivered after backout.
                            // Backout rolls back both the receive and any sends from onMessage.
                            // Failure count is derived from BackoutCount on re-delivery.
                            failedMessages[messageId] = (ex, contextBag);
                            _connection.Backout();
                        }
                        else
                        {
                            await HandleReceiveOnlyError(
                                ex, originalHeaders, messageId, messageBody,
                                transportTransaction, contextBag, cancellationToken
                            ).ConfigureAwait(false);
                        }
                    }
                }
                catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
                {
                    //log.Debug("MQRC_NO_MSG_AVAILABLE");
                    await Task.Yield();
                }
                catch (MQException ex)
                {
                    log.ErrorFormat("Worker {0} MQ error processing message from {1} - Reason: {2}, CompCode: {3}, Message: {4}", workerIndex, queueName, ex.Reason, ex.CompCode, ex.Message);

                    if (queue != null)
                    {
                        ((IDisposable)queue).Dispose();
                        queue = null;
                    }

                    DisconnectAndDispose(_connection);
                    _connection = null;

                    var maxDelay = Math.Min(ReconnectMaxDelayMs, ReconnectBaseDelayMs * (1 << Math.Min(reconnectAttempt, 30)));
                    var delay = Random.Shared.Next(0, maxDelay);
                    reconnectAttempt++;

                    log.WarnFormat("Worker {0} reconnecting to {1} in {2}ms (attempt {3})", workerIndex, queueName, delay, reconnectAttempt);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            log.Info("Cancelled");
        }
        catch (Exception e)
        {
            log.Fatal("Message pump worker failure", e);
            throw;
        }
        finally
        {
            if (queue != null)
            {
                ((IDisposable)queue).Dispose();
            }

            await cancellationLogging.DisposeAsync().ConfigureAwait(false);
        }
    }

    async Task HandleSendsAtomicWithReceiveOnError(
        Exception ex, Dictionary<string, string> originalHeaders,
        string messageId, byte[] messageBody,
        int failures, Extensibility.ContextBag contextBag,
        CancellationToken cancellationToken)
    {
        // Use a clean transport transaction (no MQQueueManager) so any sends
        // from onError go through the independent send connection
        var errorTransaction = new TransportTransaction();

        var errorContext = new ErrorContext(
            ex,
            originalHeaders,
            messageId,
            messageBody,
            errorTransaction,
            failures,
            queueName,
            contextBag
        );

        try
        {
            var result = await onError.Invoke(errorContext, cancellationToken).ConfigureAwait(false);

            if (result is ErrorHandleResult.RetryRequired)
            {
                _connection!.Backout();
            }
            else
            {
                _connection!.Commit();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception onErrorEx)
        {
            criticalError($"Failed to execute recoverability policy for message with native ID: `{messageId}`", onErrorEx, cancellationToken);
            _connection!.Backout();
        }
    }

    async Task HandleReceiveOnlyError(
        Exception ex, Dictionary<string, string> originalHeaders,
        string messageId, byte[] messageBody,
        TransportTransaction transportTransaction,
        Extensibility.ContextBag contextBag, CancellationToken cancellationToken)
    {
        int failures = failureCounts?.AddOrUpdate(messageId, 1, (_, count) => count + 1) ?? 1;

        var errorContext = new ErrorContext(
            ex,
            originalHeaders,
            messageId,
            messageBody,
            transportTransaction,
            failures,
            queueName,
            contextBag
        );

        try
        {
            var result = await onError.Invoke(errorContext, cancellationToken).ConfigureAwait(false);

            if (transactionMode == TransportTransactionMode.ReceiveOnly)
            {
                if (result is ErrorHandleResult.RetryRequired)
                {
                    _connection!.Backout();
                }
                else
                {
                    failureCounts?.TryRemove(messageId, out _);
                    _connection!.Commit();
                }
            }
            else
            {
                failureCounts?.TryRemove(messageId, out _);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception onErrorEx)
        {
            criticalError($"Failed to execute recoverability policy for message with native ID: `{messageId}`", onErrorEx, cancellationToken);

            if (transactionMode == TransportTransactionMode.ReceiveOnly)
            {
                _connection!.Backout();
            }
        }
    }

    static void DisconnectAndDispose(MQQueueManager? connection)
    {
        if (connection == null)
        {
            return;
        }

        using (connection)
        {
            connection.Disconnect();
        }
    }
}
