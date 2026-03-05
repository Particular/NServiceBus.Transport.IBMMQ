namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Handlers;

using Infrastructure;
using Messages;

class ReceiveAndSendHandler : IHandleMessages<PerfTestMessage>
{
    public async Task Handle(PerfTestMessage message, IMessageHandlerContext context)
    {
        await HandlerCompletion.WaitForGate(context.CancellationToken).ConfigureAwait(false);
        await context.Send(new PerfTestSinkMessage { Index = message.Index }).ConfigureAwait(false);
        HandlerCompletion.SignalOne();
    }
}
