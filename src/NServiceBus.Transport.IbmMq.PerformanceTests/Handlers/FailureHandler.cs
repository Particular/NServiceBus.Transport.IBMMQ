namespace NServiceBus.Transport.IbmMq.PerformanceTests.Handlers;

using NServiceBus.Transport.IbmMq.PerformanceTests.Messages;

class FailureHandler : IHandleMessages<PerfTestMessage>
{
    public Task Handle(PerfTestMessage message, IMessageHandlerContext context)
    {
        throw new InvalidOperationException("Simulated failure for perf test");
    }
}
