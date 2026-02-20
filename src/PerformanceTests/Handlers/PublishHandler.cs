namespace NServiceBus.Transport.IbmMq.PerformanceTests.Handlers;

using Infrastructure;
using Messages;

class PublishHandler : IHandleMessages<PerfTestEvent>
{
    public async Task Handle(PerfTestEvent message, IMessageHandlerContext context)
    {
        await HandlerCompletion.WaitForGate(context.CancellationToken).ConfigureAwait(false);
        await context.Publish(new PerfTestEvent { Index = message.Index }).ConfigureAwait(false);
        HandlerCompletion.SignalOne();
    }
}
