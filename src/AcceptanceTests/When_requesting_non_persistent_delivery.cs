namespace NServiceBus.AcceptanceTests;

using AcceptanceTesting;
using EndpointTemplates;
using NUnit.Framework;

public class When_requesting_non_persistent_delivery : NServiceBusAcceptanceTest
{
    [Test]
    public async Task Should_flag_message_as_non_durable()
    {
        var context = await Scenario.Define<Context>()
            .WithEndpoint<Receiver>(b => b.When((session, ctx) =>
            {
                var options = new SendOptions();

                options.RouteToThisEndpoint();
                options.UseNonPersistentDeliveryMode();

                return session.Send(new Message(), options);
            }))
            .Done(c => c.MessageReceived)
            .Run().ConfigureAwait(false);

        Assert.That(context.NonDurableHeaderPresent, Is.True);
    }

    [Test]
    public async Task Should_not_flag_message_as_non_durable_by_default()
    {
        var context = await Scenario.Define<Context>()
            .WithEndpoint<Receiver>(b => b.When((session, ctx) =>
            {
                var options = new SendOptions();

                options.RouteToThisEndpoint();

                return session.Send(new Message(), options);
            }))
            .Done(c => c.MessageReceived)
            .Run().ConfigureAwait(false);

        Assert.That(context.NonDurableHeaderPresent, Is.False);
    }

    class Receiver : EndpointConfigurationBuilder
    {
        public Receiver() => EndpointSetup<DefaultServer>();

        class MyMessageHandler(Context testContext) : IHandleMessages<Message>
        {
            public Task Handle(Message message, IMessageHandlerContext context)
            {
                testContext.NonDurableHeaderPresent = context.MessageHeaders.ContainsKey(Headers.NonDurableMessage);
                testContext.MessageReceived = true;
                return Task.CompletedTask;
            }
        }
    }

    public class Message : IMessage
    {
    }

    class Context : ScenarioContext
    {
        public bool MessageReceived { get; set; }

        public bool NonDurableHeaderPresent { get; set; }
    }
}
