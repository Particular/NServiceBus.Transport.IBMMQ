namespace NServiceBus.Transport.IBMMQ.PerformanceTests.Messages;

class PerfTestMessage : IMessage
{
    public int Index { get; set; }
}
