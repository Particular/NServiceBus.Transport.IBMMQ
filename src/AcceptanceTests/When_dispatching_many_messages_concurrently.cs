namespace NServiceBus.AcceptanceTests;

using AcceptanceTesting;
using AcceptanceTesting.Customization;
using EndpointTemplates;
using NUnit.Framework;

/// <summary>
/// Verifies that concurrent handler dispatches to the same destination work correctly.
/// Two handlers run simultaneously (enforced via CountdownEvent), each sending to the
/// same queue. For SendsAtomicWithReceive, each send must use the handler's own receive
/// connection — not a cached handle from a different connection.
/// </summary>
public class When_dispatching_many_messages_concurrently : NServiceBusAcceptanceTest
{
    [TestCase(TransportTransactionMode.None)]
    [TestCase(TransportTransactionMode.ReceiveOnly)]
    [TestCase(TransportTransactionMode.SendsAtomicWithReceive)]
    public async Task Should_deliver_all_messages(TransportTransactionMode transactionMode)
    {
        var context = await Scenario.Define<MyContext>()
            .WithEndpoint<Dispatcher>(b => b
                .CustomConfig(c =>
                {
                    c.ConfigureTransport().TransportTransactionMode = transactionMode;
                    c.ConfigureRouting().RouteToEndpoint(typeof(ResultMessage), typeof(Collector));
                })
                .When(async (session, ctx) =>
                {
                    await session.SendLocal(new TriggerMessage { CorrelationId = ctx.TestRunId }).ConfigureAwait(false);
                    await session.SendLocal(new TriggerMessage { CorrelationId = ctx.TestRunId }).ConfigureAwait(false);
                }))
            .WithEndpoint<Collector>(b => b
                .CustomConfig(c => c.ConfigureTransport().TransportTransactionMode = transactionMode))
            .Run().ConfigureAwait(false);

        Assert.That(context.ReceivedCount, Is.EqualTo(2));
    }

    class MyContext : ScenarioContext
    {
        public readonly CountdownEvent Barrier = new(2);
        public int ReceivedCount => Volatile.Read(ref receivedCount);
        int receivedCount;

        public void MessageReceived()
        {
            if (Interlocked.Increment(ref receivedCount) >= 2)
            {
                MarkAsCompleted();
            }
        }
    }

    class Dispatcher : EndpointConfigurationBuilder
    {
        public Dispatcher() => EndpointSetup<DefaultServer>();

        class TriggerHandler(MyContext testContext) : IHandleMessages<TriggerMessage>
        {
            public async Task Handle(TriggerMessage message, IMessageHandlerContext context)
            {
                if (message.CorrelationId != testContext.TestRunId)
                {
                    return;
                }

                // Ensure both handlers are active concurrently before sending.
                // This forces two simultaneous dispatches to the same destination,
                // each of which must use its own connection's handle.
                // NOTE: Blocking Wait is intentional — requires endpoint concurrency >= 2
                // to avoid deadlock (both handlers must reach the barrier simultaneously).
                testContext.Barrier.Signal();
                testContext.Barrier.Wait(TimeSpan.FromSeconds(10), CancellationToken.None);

                await context.Send(new ResultMessage { CorrelationId = testContext.TestRunId }).ConfigureAwait(false);
            }
        }
    }

    class Collector : EndpointConfigurationBuilder
    {
        public Collector() => EndpointSetup<DefaultServer>();

        class ResultHandler(MyContext testContext) : IHandleMessages<ResultMessage>
        {
            public Task Handle(ResultMessage message, IMessageHandlerContext context)
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

    public class ResultMessage : IMessage
    {
        public Guid CorrelationId { get; set; }
    }
}
