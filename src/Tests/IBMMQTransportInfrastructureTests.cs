namespace NServiceBus.Transport.IBMMQ.Tests;

using NServiceBus.Logging;
using NUnit.Framework;

[TestFixture]
class TransportInfrastructureTests
{
    [Test]
    public void TransactionScope_is_rejected_during_construction()
    {
        Assert.That(
            () => CreateInfrastructure(TransportTransactionMode.TransactionScope),
            Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void Unknown_transaction_mode_is_rejected_during_construction()
    {
        var unsupportedMode = (TransportTransactionMode)int.MaxValue;

        Assert.That(
            () => CreateInfrastructure(unsupportedMode),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public async Task Receiver_disposal_is_idempotent()
    {
        var receiveSettings = new ReceiveSettings(
            "receiver",
            new QueueAddress("input"),
            false,
            false,
            "error");
        var infrastructure = CreateInfrastructure(
            TransportTransactionMode.ReceiveOnly,
            [receiveSettings]);
        var receiver = (IBMMQMessageReceiver)infrastructure.Receivers[receiveSettings.Id];

        await receiver.DisposeAsync().ConfigureAwait(false);
        await receiver.DisposeAsync().ConfigureAwait(false);
        await infrastructure.Shutdown().ConfigureAwait(false);
    }

    [Test]
    public async Task Shutdown_disposes_receivers()
    {
        var receiveSettings = new ReceiveSettings(
            "receiver",
            new QueueAddress("input"),
            false,
            false,
            "error");
        var infrastructure = CreateInfrastructure(
            TransportTransactionMode.ReceiveOnly,
            [receiveSettings]);
        var receiver = infrastructure.Receivers[receiveSettings.Id];

        await infrastructure.Shutdown().ConfigureAwait(false);

        Assert.That(
            async () => await receiver.StartReceive().ConfigureAwait(false),
            Throws.TypeOf<ObjectDisposedException>());
    }

    static IBMMQTransportInfrastructure CreateInfrastructure(
        TransportTransactionMode transactionMode,
        params ReceiveSettings[] receiverSettings)
    {
        var transport = new IBMMQTransport();
        return new IBMMQTransportInfrastructure(
            LogManager.GetLogger<IBMMQTransportInfrastructure>(),
            transport,
            new ConnectionConfiguration(transport),
            receiverSettings,
            transactionMode,
            (_, _, _) => { });
    }
}
