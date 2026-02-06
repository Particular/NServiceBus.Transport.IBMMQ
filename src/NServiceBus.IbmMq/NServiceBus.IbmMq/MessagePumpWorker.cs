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
    CancellationTokenSource? cts;

    public void Start()
    {
        Log.DebugFormat("Worker {0} starting for queue {1}", workerIndex, queueName);
        cts = new CancellationTokenSource();
        pumpTask = Task.Run(() => PumpMessages(cts.Token), cts.Token);
    }

    public async Task StopAsync()
    {
        Log.DebugFormat("Worker {0} stopping for queue {1}", workerIndex, queueName);
        if (cts != null)
        {
            await cts.CancelAsync();
        }

        if (pumpTask != null)
        {
            try
            {
                await pumpTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);

        cts?.Dispose();
        connectionPool.Return(_connection);

        Log.DebugFormat("Worker {0} disposed", workerIndex);
    }

    async Task PumpMessages(CancellationToken cancellationToken)
    {
        var helper = new IbmMqHelper(_connection);
        MQQueue queue = helper.EnsureQueue(queueName, MQC.MQOO_INPUT_AS_Q_DEF);

        Log.DebugFormat("Worker {0} started pumping messages from {1}", workerIndex, queueName);

        while (!cancellationToken.IsCancellationRequested)
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

            try
            {
                queue.Get(receivedMessage, getOptions);

                messageBody = IbmMqMessageConverter.FromNative(receivedMessage, messageHeaders, ref messageId);

                Log.DebugFormat("Worker {0} received message {1}", workerIndex, messageId);

                var messageContext = new MessageContext(
                    messageId,
                    messageHeaders,
                    messageBody,
                    new TransportTransaction(),
                    queueName,
                    new Extensibility.ContextBag());

                await onMessage(messageContext, cancellationToken).ConfigureAwait(false);

                _connection.Commit();
            }
            catch (MQException ex) when (ex.ReasonCode == MQC.MQRC_NO_MSG_AVAILABLE)
            {
                await Task.Yield();
            }
            catch (Exception ex)
            {
                Log.DebugFormat("Worker {0} error processing message from {1}\n{2}", workerIndex, queueName, ex);

                var errorContext = new ErrorContext(
                    ex,
                    messageHeaders,
                    messageId,
                    messageBody,
                    new TransportTransaction(),
                    0,
                    queueName,
                    new Extensibility.ContextBag());

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
        }
    }
}