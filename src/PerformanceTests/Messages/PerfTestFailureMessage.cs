namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Messages;

class PerfTestFailureMessage : IMessage
{
    public int Index { get; set; }
}
