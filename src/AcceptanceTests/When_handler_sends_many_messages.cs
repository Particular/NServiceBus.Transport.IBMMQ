namespace NServiceBus.AcceptanceTests;

using System;
using System.Threading;
using System.Threading.Tasks;
using AcceptanceTesting;
using AcceptanceTesting.Customization;
using EndpointTemplates;
using NUnit.Framework;

/// <summary>
/// Verifies that a handler can send a batch of messages within a single handler
/// invocation across all transaction modes. For SendsAtomicWithReceive, the batch
/// is dispatched atomically on the receive connection. For None/ReceiveOnly, the
/// batch is dispatched through the connection pool using cached handles.
/// </summary>
public class When_handler_sends_many_messages : NServiceBusAcceptanceTest
{
    const int MessageCount = 20;

    [TestCase(TransportTransactionMode.None)]
    [TestCase(TransportTransactionMode.ReceiveOnly)]
    [TestCase(TransportTransactionMode.SendsAtomicWithReceive)]
    public async Task Should_deliver_all_messages(TransportTransactionMode transactionMode)
    {
        var context = await Scenario.Define<MyContext>(c => c.ExpectedCount = MessageCount)
            .WithEndpoint<Sender>(b => b
                .CustomConfig(c =>
                {
                    c.ConfigureTransport().TransportTransactionMode = transactionMode;
                    c.ConfigureRouting().RouteToEndpoint(typeof(MyMessage), typeof(Receiver));
                })
                .When((session, ctx) => session.SendLocal(new TriggerMessage { CorrelationId = ctx.TestRunId })))
            .WithEndpoint<Receiver>(b => b
                .CustomConfig(c => c.ConfigureTransport().TransportTransactionMode = transactionMode))
            .Run().ConfigureAwait(false);

        Assert.That(context.ReceivedCount, Is.EqualTo(MessageCount));
    }

    class MyContext : ScenarioContext
    {
        public int ExpectedCount { get; set; }
        public int ReceivedCount => Volatile.Read(ref receivedCount);
        int receivedCount;

        public void MessageReceived()
        {
            if (Interlocked.Increment(ref receivedCount) >= ExpectedCount)
            {
                MarkAsCompleted();
            }
        }
    }

    class Sender : EndpointConfigurationBuilder
    {
        public Sender() => EndpointSetup<DefaultServer>();

        class TriggerHandler(MyContext testContext) : IHandleMessages<TriggerMessage>
        {
            public async Task Handle(TriggerMessage message, IMessageHandlerContext context)
            {
                if (message.CorrelationId != testContext.TestRunId)
                {
                    return;
                }

                for (int i = 0; i < MessageCount; i++)
                {
                    await context.Send(new MyMessage { CorrelationId = testContext.TestRunId }).ConfigureAwait(false);
                }
            }
        }
    }

    class Receiver : EndpointConfigurationBuilder
    {
        public Receiver() => EndpointSetup<DefaultServer>();

        class MyMessageHandler(MyContext testContext) : IHandleMessages<MyMessage>
        {
            public Task Handle(MyMessage message, IMessageHandlerContext context)
            {
                if (message.CorrelationId == testContext.TestRunId)
                {
                    testContext.MessageReceived();
                }
                return Task.CompletedTask;
            }
        }
    }

    public class TriggerMessage : ICommand
    {
        public Guid CorrelationId { get; set; }
    }

    public class MyMessage : IMessage
    {
        public Guid CorrelationId { get; set; }
    }
}
