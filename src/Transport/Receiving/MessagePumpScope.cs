namespace NServiceBus.Transport.IBMMQ;

using Logging;

sealed class MessagePumpScope(
    MqConnection connection,
    IBMMQMessageConverter messageConverter,
    IFailureInfoStorage? failureInfoStorage,
    TransportTransactionMode transactionMode)
    : IAsyncDisposable
{
    public ReceiveStrategy CreateStrategy(ReceiveContext ctx)
    {
        var strategyLog = LogManager.GetLogger<ReceiveStrategy>();
        return transactionMode switch
        {
            TransportTransactionMode.None =>
                new NoTransactionReceiveStrategy(strategyLog, connection, messageConverter, ctx),
            TransportTransactionMode.ReceiveOnly =>
                new ReceiveOnlyReceiveStrategy(strategyLog, connection, messageConverter, ctx),
            TransportTransactionMode.SendsAtomicWithReceive =>
                new AtomicReceiveStrategy(strategyLog, connection, messageConverter, failureInfoStorage!, ctx),
            TransportTransactionMode.TransactionScope =>
                throw new NotSupportedException("TransactionScope is not supported"),
            _ => throw new ArgumentOutOfRangeException(nameof(transactionMode), transactionMode, "Unsupported transaction mode")
        };
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}