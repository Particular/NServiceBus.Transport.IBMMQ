namespace NServiceBus.Transport.IbmMq.PerformanceTests.Messages;

class PerfTestFailureMessage : IMessage
{
    public int Index { get; set; }
}
