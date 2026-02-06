using FullTransportEndpoint.Messages;

namespace FullTransportEndpoint.Subscriber;

sealed class MyHandler : IHandleMessages<MyMessage>
{
    public async Task Handle(MyMessage message, IMessageHandlerContext context)
    {
        Console.WriteLine($"Start {message.Data}");
        await context.SendLocal(new MyMessage2());
        await context.SendLocal(new MyMessage2());
        await Task.Delay(200, context.CancellationToken);
        Console.WriteLine($"End: {message.Data}");
    }
}
sealed class MyHandler2 : IHandleMessages<MyMessage2>
{
    public async Task Handle(MyMessage2 message, IMessageHandlerContext context)
    {
        Console.WriteLine($"MyMessage2");
    }
}