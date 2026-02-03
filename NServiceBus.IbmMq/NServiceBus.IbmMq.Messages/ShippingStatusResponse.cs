namespace NServiceBus.IbmMq.Messages;

public class ShippingStatusResponse : IMessage
{
    public required string OrderId { get; set; }
    public string? Text { get; set; }
}
