using IBM.WMQ;
using NServiceBus.Logging;

namespace NServiceBus.Transport.IbmMq;

sealed class MessagePumpWorker(
    MQConnectionPool connectionPool,
    string queueName,
    OnMessage onMessage,
    OnError onError,
    int workerIndex
) : IAsyncDisposable
{
    const int WaitInterval = 5000; // TODO : Use injected settings when WIP PR is merged
    readonly ILog Log = LogManager.GetLogger<MessagePumpWorker>();
    readonly MQQueueManager _connection = connectionPool.Lease();
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

        await stopCts.CancelAsync();

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
        connectionPool.Return(_connection);

        Log.DebugFormat("Worker {0} disposed", workerIndex);
    }

    async Task PumpMessages(CancellationToken cancellationToken)
    {
        var helper = new IbmMqHelper(_connection);
        MQQueue queue = helper.EnsureQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF);

        Log.DebugFormat("Worker {0} started pumping messages from {1}", workerIndex, queueName);

        while (!stopCts.IsCancellationRequested)
        {
            MQMessage receivedMessage = new();
            MQGetMessageOptions getOptions = new()
            {
                Options = MQC.MQGMO_WAIT
                          | MQC.MQGMO_SYNCPOINT
                          | MQC.MQGMO_FAIL_IF_QUIESCING
                          | MQC.MQGMO_PROPERTIES_IN_HANDLE,
                WaitInterval = WaitInterval
            };

            string messageId = string.Empty;
            byte[] messageBody = [];
            Dictionary<string, string> messageHeaders = [];
            Dictionary<string, string> originalHeaders = [];

            // Create ContextBag once to share between MessageContext and ErrorContext
            var contextBag = new Extensibility.ContextBag(); // TODO: Compare with other transports if this is really what we want

            try
            {
                queue.Get(receivedMessage, getOptions);

                messageBody = IbmMqMessageConverter.FromNative(receivedMessage, messageHeaders, ref messageId);

                // Snapshot headers before onMessage, which may mutate the dictionary
                originalHeaders = new Dictionary<string, string>(messageHeaders);

                Log.DebugFormat("Worker {0} received message {1}", workerIndex, messageId);

                var messageContext = new MessageContext(
                    messageId,
                    messageHeaders,
                    messageBody,
                    new TransportTransaction(),
                    queueName,
                    contextBag
                );

                // Pass messageProcessingCts token, not pump's cancellation token
                await onMessage(messageContext, cancellationToken).ConfigureAwait(false);

                _connection.Commit();
            }
            catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                await Task.Yield();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancellation was requested, exit the loop ungracefully to terminate ASAP
                break;
            }
            catch (Exception ex)
            {
                Log.DebugFormat("Worker {0} error processing message from {1}\n{2}", workerIndex, queueName, ex);

                var errorContext = new ErrorContext(
                    ex,
                    originalHeaders, // Use snapshot, not mutated headers
                    messageId,
                    messageBody,
                    new TransportTransaction(),
                    receivedMessage.BackoutCount + 1,
                    queueName,
                    contextBag
                );

                try
                {
                    var result = await onError.Invoke(errorContext, cancellationCts.Token).ConfigureAwait(false);

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
                    // Cancellation was requested during error handling, exit gracefully
                    break;
                }
                catch (Exception onErrorEx)
                {
                    // onError threw — backout so the message is retried
                    Log.DebugFormat("Worker {0} exception in error handling path: {1}", workerIndex, onErrorEx);
                    _connection.Backout();
                }
            }
        }
    }
}