namespace NServiceBus.Transport.IBMMQ.Benchmarks;

using BenchmarkDotNet.Attributes;
using IBM.WMQ;
using NServiceBus.Transport;

[MemoryDiagnoser]
public class MessageConverterBenchmarks
{
    UnicastTransportOperation operation = null!;
    MQMessage nativeMessage = null!;

    [GlobalSetup]
    public void Setup()
    {
        var id = Guid.NewGuid();
        var headers = new Dictionary<string, string>
        {
            ["NServiceBus.MessageId"] = id.ToString(),
            ["NServiceBus.ConversationId"] = Guid.NewGuid().ToString(),
            ["NServiceBus.CorrelationId"] = Guid.NewGuid().ToString(),
            ["NServiceBus.RelatedTo"] = Guid.NewGuid().ToString(),
            ["NServiceBus.ContentType"] = "application/json",
            ["NServiceBus.EnclosedMessageTypes"] = "Acme.OrderShipped, Acme.Shared",
            ["NServiceBus.MessageIntent"] = "Publish",
            ["NServiceBus.OpenTelemetry.StartNewTrace"] = "True",
            ["NServiceBus.OriginatingEndpoint"] = "benchmark",
            ["NServiceBus.OriginatingMachine"] = Environment.MachineName,
            ["NServiceBus.ReplyToAddress"] = "benchmark",
            ["NServiceBus.TimeSent"] = DateTime.UtcNow.ToString("O"),
            ["NServiceBus.Version"] = "10.1.0",
            ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
            ["$.diagnostics.originating.hostid"] = "abcdef0123456789"
        };

        var body = System.Text.Encoding.UTF8.GetBytes("""{"OrderId":"12345","TrackingNumber":"TRACK-12345"}""");
        var message = new OutgoingMessage(id.ToString(), headers, body);
        operation = new UnicastTransportOperation(message, "destination", []);

        // Pre-create a native message for FromNative benchmarks
        nativeMessage = IBMMQMessageConverter.ToNative(operation);
    }

    [Benchmark]
    public MQMessage ToNative()
    {
        return IBMMQMessageConverter.ToNative(operation);
    }

    [Benchmark]
    public byte[] FromNative()
    {
        // Reset read cursor
        nativeMessage.Seek(0);

        var headers = new Dictionary<string, string>();
        var messageId = string.Empty;
        return IBMMQMessageConverter.FromNative(nativeMessage, headers, ref messageId);
    }

    [Benchmark]
    public byte[] RoundTrip()
    {
        var native = IBMMQMessageConverter.ToNative(operation);
        var headers = new Dictionary<string, string>();
        var messageId = string.Empty;
        return IBMMQMessageConverter.FromNative(native, headers, ref messageId);
    }
}
