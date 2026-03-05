namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Handlers;

using Infrastructure;
using Messages;

class ReceiveThroughputHandler : IHandleMessages<PerfTestMessage>
{
    public async Task Handle(PerfTestMessage message, IMessageHandlerContext context)
    {
        await HandlerCompletion.WaitForGate(context.CancellationToken).ConfigureAwait(false);
        HandlerCompletion.SignalOne();
    }
}
