namespace NServiceBus.Transport.IbmMq.PerformanceTests.Handlers;

using NServiceBus.Transport.IbmMq.PerformanceTests.Infrastructure;
using NServiceBus.Transport.IbmMq.PerformanceTests.Messages;

class PublishHandler : IHandleMessages<PerfTestEvent>
{
    public async Task Handle(PerfTestEvent message, IMessageHandlerContext context)
    {
        await HandlerCompletion.WaitForGate(context.CancellationToken).ConfigureAwait(false);
        await context.Publish(new PerfTestEvent { Index = message.Index }).ConfigureAwait(false);
        HandlerCompletion.SignalOne();
    }
}
