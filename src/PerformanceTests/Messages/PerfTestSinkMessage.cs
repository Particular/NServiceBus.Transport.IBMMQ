namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Messages;

class PerfTestSinkMessage : IMessage
{
    public int Index { get; set; }
}
