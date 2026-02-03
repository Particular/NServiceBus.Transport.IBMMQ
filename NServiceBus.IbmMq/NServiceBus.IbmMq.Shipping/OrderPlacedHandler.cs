using NServiceBus.IbmMq.Messages;

namespace NServiceBus.IbmMq.Shipping;

public class OrderPlacedHandler : IHandleMessages<OrderPlaced>
{
    public async Task Handle(OrderPlaced message, IMessageHandlerContext context)
    {
        Console.WriteLine($"Received OrderPlaced Event, OrderId: {message.OrderId}.");

        await Task.Delay(1500, CancellationToken.None);

        var orderShipped = new OrderShipped
        {
            OrderId = message.OrderId
        };

        await context.Publish(orderShipped);

        Console.WriteLine($"Published OrderShipped Event, OrderId: {orderShipped.OrderId}.");
    }
}
