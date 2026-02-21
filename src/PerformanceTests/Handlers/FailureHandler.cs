namespace NServiceBus.Transport.IbmMq.PerformanceTests.Handlers;

using Messages;

class FailureHandler : IHandleMessages<PerfTestFailureMessage>
{
    public Task Handle(PerfTestFailureMessage message, IMessageHandlerContext context)
    {
        throw new InvalidOperationException("Simulated failure for perf test");
    }
}
