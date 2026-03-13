namespace NServiceBus.Transport.IBMMQ.Benchmarks;

using BenchmarkDotNet.Attributes;

[MemoryDiagnoser]
public class PropertyNameEncoderBenchmarks
{
    static readonly string[] HeaderNames =
    [
        "NServiceBus.MessageId",
        "NServiceBus.CorrelationId",
        "NServiceBus.ConversationId",
        "NServiceBus.ContentType",
        "NServiceBus.EnclosedMessageTypes",
        "NServiceBus.MessageIntent",
        "NServiceBus.OriginatingEndpoint",
        "NServiceBus.OriginatingMachine",
        "NServiceBus.ReplyToAddress",
        "NServiceBus.TimeSent",
        "NServiceBus.Version",
        "NServiceBus.NonDurableMessage",
        "NServiceBus.OpenTelemetry.StartNewTrace",
        "traceparent",
        "$.diagnostics.originating.hostid"
    ];

    static readonly string[] EncodedNames;

    static PropertyNameEncoderBenchmarks()
    {
        EncodedNames = new string[HeaderNames.Length];
        for (var i = 0; i < HeaderNames.Length; i++)
        {
            EncodedNames[i] = MqPropertyNameEncoder.Encode(HeaderNames[i]);
        }
    }

    [Benchmark]
    public string[] EncodeAll()
    {
        var results = new string[HeaderNames.Length];
        for (var i = 0; i < HeaderNames.Length; i++)
        {
            results[i] = MqPropertyNameEncoder.Encode(HeaderNames[i]);
        }

        return results;
    }

    [Benchmark]
    public string[] DecodeAll()
    {
        var results = new string[EncodedNames.Length];
        for (var i = 0; i < EncodedNames.Length; i++)
        {
            results[i] = MqPropertyNameEncoder.Decode(EncodedNames[i]);
        }

        return results;
    }
}
