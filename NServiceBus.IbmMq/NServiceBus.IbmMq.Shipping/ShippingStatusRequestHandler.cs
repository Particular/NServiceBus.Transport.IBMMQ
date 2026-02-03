using NServiceBus.IbmMq.Messages;

namespace NServiceBus.IbmMq.Shipping;

public class ShippingStatusRequestHandler : IHandleMessages<ShippingStatusRequest>
{
    private static readonly Random Random = new();

    public async Task Handle(ShippingStatusRequest message, IMessageHandlerContext context)
    {
        Console.WriteLine($"Received Shipping Status Request, OrderId: {message.OrderId}.");

        await Task.Delay(1500, CancellationToken.None);

        var reply = new ShippingStatusResponse
        {
            OrderId = message.OrderId,
            Text = GetRandomShippingDelayMessage()
        };

        await context.Reply(reply);
    }

    private static string GetRandomShippingDelayMessage()
    {
        var messages = new List<string>
        {
            "Order is currently delayed... A seagull stole the delivery instructions. We're in negotiations.",
            "The order is stuck in customs. They’re suspicious of how awesome it is.",
            "Your package is delayed due to unexpected detours through Narnia.",
            "We shipped it, but it seems to have entered another dimension. ETA unknown.",
            "The order was last seen riding a camel in the desert. We’re tracking it.",
            "A courier mishap resulted in your package becoming the new leader of a small island nation. Reclaiming it now.",
            "The package is on its way, but the delivery driver got sidetracked by an epic side quest.",
            "Our carrier pigeon union is still on strike. We're negotiating their demands for premium bird seed.",
            "Your package is currently starring in a reality TV show called 'Lost in Transit'.",
            "It’s out for delivery… in the wrong city. We’re sending a search party."
        };

        return messages[Random.Next(messages.Count)];
    }
}
