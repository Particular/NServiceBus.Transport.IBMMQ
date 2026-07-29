namespace NServiceBus.Transport.IBMMQ;

sealed class ScopeFactory
{
    readonly Func<MqConnection> connectionFactory;
    readonly IBMMQMessageConverter messageConverter;
    readonly IFailureInfoStorage? failureInfoStorage;
    readonly TransportTransactionMode transactionMode;

    public ScopeFactory(
        Func<MqConnection> connectionFactory,
        IBMMQMessageConverter messageConverter,
        IFailureInfoStorage? failureInfoStorage,
        TransportTransactionMode transactionMode)
    {
        this.connectionFactory = connectionFactory;
        this.messageConverter = messageConverter;
        this.failureInfoStorage = failureInfoStorage;
        this.transactionMode = transactionMode;
    }

    public MessagePumpScope CreateScope() =>
        new(connectionFactory(), messageConverter, failureInfoStorage, transactionMode);
}
