namespace NServiceBus.Transport.IbmMq;

using IBM.WMQ;
using NServiceBus.Logging;

sealed class MessagePumpWorker(
    int messageWaitInterval,
    MQConnectionPool connectionPool,
    string queueName,
    OnMessage onMessage,
    OnError onError,
    int workerIndex
) : IAsyncDisposable
{
    const int ReconnectBaseDelayMs = 1000;
    const int ReconnectMaxDelayMs = 60_000;
    readonly ILog Log = LogManager.GetLogger<MessagePumpWorker>();
    MQQueueManager? _connection = connectionPool.Lease();
    Task? pumpTask;
    CancellationTokenSource stopCts = new();
    CancellationTokenSource cancellationCts = new();

    public void Start()
    {
        Log.DebugFormat("Worker {0} starting for queue {1}", workerIndex, queueName);
        // Don't pass cancellation token to Task.Run to avoid race condition
        pumpTask = Task.Run(() => PumpMessages(cancellationCts.Token));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Log.DebugFormat("Worker {0} stopping for queue {1}", workerIndex, queueName);

        // If a cancellation token is provided, link it to message processing cancellation
        // This allows StopReceive to control whether in-flight messages are cancelled
        if (cancellationToken.CanBeCanceled)
        {
            var cancellationCtsClone = cancellationCts; // Capture to avoid closure issues
            cancellationToken.Register(() => cancellationCtsClone.Cancel());
        }

        await stopCts.CancelAsync().ConfigureAwait(false);

        if (pumpTask != null)
        {
            await pumpTask.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        cancellationCts.Dispose();
        stopCts.Dispose();

        if (_connection != null)
        {
            connectionPool.Return(_connection);
        }

        Log.DebugFormat("Worker {0} disposed", workerIndex);
    }

    async Task PumpMessages(CancellationToken cancellationToken)
    {
        MQQueue? queue = null;

        try
        {
            Log.DebugFormat("Worker {0} started pumping messages from {1}", workerIndex, queueName);

            MQGetMessageOptions getOptions = new()
            {
                Options = MQC.MQGMO_WAIT
                          | MQC.MQGMO_SYNCPOINT
                          | MQC.MQGMO_FAIL_IF_QUIESCING
                          | MQC.MQGMO_PROPERTIES_IN_HANDLE,
                WaitInterval = messageWaitInterval
            };

            int reconnectAttempt = 0;

            while (!stopCts.IsCancellationRequested)
            {
                MQMessage receivedMessage = new();

                try
                {
                    if (_connection == null)
                    {
                        Log.DebugFormat("Worker {0} creating queue connection {1}", workerIndex, queueName);
                        _connection = connectionPool.Lease();
                    }

                    queue ??= _connection.AccessQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF);

                    string messageId = string.Empty;
                    byte[] messageBody = [];
                    Dictionary<string, string> messageHeaders = [];
                    Dictionary<string, string> originalHeaders = [];

                    // TODO: Compare with other transports if ContextBag is shared once between MessageContext and ErrorContext
                    var contextBag = new Extensibility.ContextBag();

                    try
                    {
                        queue.Get(receivedMessage, getOptions);
                        messageBody = IbmMqMessageConverter.FromNative(receivedMessage, messageHeaders, ref messageId);
                        originalHeaders = new Dictionary<string, string>(messageHeaders); // Snapshot headers before onMessage, which may mutate the dictionary

                        Log.DebugFormat("Worker {0} received message {1}", workerIndex, messageId);

                        var messageContext = new MessageContext(
                            messageId,
                            messageHeaders,
                            messageBody,
                            new TransportTransaction(),
                            queueName,
                            contextBag
                        );

                        await onMessage(messageContext, cancellationToken).ConfigureAwait(false);

                        _connection.Commit();
                        reconnectAttempt = 0;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is not MQException)
                    {
                        Log.DebugFormat("Worker {0} error processing message from {1}\n{2}", workerIndex, queueName, ex);

                        var errorContext = new ErrorContext(
                            ex,
                            originalHeaders,
                            messageId,
                            messageBody,
                            new TransportTransaction(),
                            receivedMessage.BackoutCount + 1,
                            queueName,
                            contextBag
                        );

                        try
                        {
                            var result = await onError.Invoke(errorContext, cancellationToken).ConfigureAwait(false);

                            if (result is ErrorHandleResult.RetryRequired)
                            {
                                _connection.Backout();
                            }
                            else
                            {
                                _connection.Commit();
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception onErrorEx)
                        {
                            Log.DebugFormat("Worker {0} exception in error handling path: {1}", workerIndex, onErrorEx);
                            _connection.Backout();
                        }
                    }
                }
                catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
                {
                    Log.Debug("MQRC_NO_MSG_AVAILABLE");
                    await Task.Yield();
                }
                catch (MQException ex) // when (ex.ReasonCode is MQC.MQRC_CONNECTION_BROKEN or MQC.MQRC_Q_MGR_NOT_AVAILABLE)
                {
                    Log.ErrorFormat("Worker {0} MQ error processing message from {1} - Reason: {2}, CompCode: {3}, Message: {4}", workerIndex, queueName, ex.Reason, ex.CompCode, ex.Message);

                    if (queue != null)
                    {
                        ((IDisposable)queue).Dispose();
                        queue = null;
                    }

                    if (_connection != null)
                    {
                        connectionPool.Return(_connection, true);
                        _connection = null;
                    }

                    var maxDelay = Math.Min(ReconnectMaxDelayMs, ReconnectBaseDelayMs * (1 << Math.Min(reconnectAttempt, 30)));
                    var delay = Random.Shared.Next(0, maxDelay);
                    reconnectAttempt++;

                    Log.WarnFormat("Worker {0} reconnecting to {1} in {2}ms (attempt {3})", workerIndex, queueName, delay, reconnectAttempt);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Log.Info("Cancelled");
        }
        catch (Exception e)
        {
            Log.Fatal("Message pump worker failure", e);
            // TODO: Signal critical error
            throw;
        }
        finally
        {
            if (queue != null)
            {
                ((IDisposable)queue).Dispose();
            }
        }
    }
}