namespace NServiceBus.Transport.IBMMQ.Benchmarks;

using BenchmarkDotNet.Attributes;
using IBM.WMQ;

[MemoryDiagnoser]
public class MqMessageBenchmarks
{
    [Benchmark(Baseline = true)]
    public MQMessage NewMessage()
    {
        return new MQMessage();
    }

    readonly MQMessage reusableMessage = new();

    [Benchmark]
    public MQMessage ClearMessage()
    {
        reusableMessage.ClearMessage();
        return reusableMessage;
    }
}
