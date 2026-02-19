namespace NServiceBus.Transport.IbmMq.PerformanceTests.Handlers;

using NServiceBus.Transport.IbmMq.PerformanceTests.Infrastructure;
using NServiceBus.Transport.IbmMq.PerformanceTests.Messages;

class SendLocalHandler : IHandleMessages<PerfTestMessage>
{
    public async Task Handle(PerfTestMessage message, IMessageHandlerContext context)
    {
        await HandlerCompletion.WaitForGate(context.CancellationToken).ConfigureAwait(false);
        await context.SendLocal(new PerfTestMessage { Index = message.Index }).ConfigureAwait(false);
        HandlerCompletion.SignalOne();
    }
}
