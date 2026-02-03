using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NServiceBus.IbmMq;

Console.Title = "Bridge";
var builder = Host.CreateApplicationBuilder(args);

var ibmmq = new IbmMqTransport("localhost", "admin", "passw0rd", null, null);

var endpointB = new EndpointConfiguration("DEV.SHIPPING");
endpointB.SendFailedMessagesTo("error");
endpointB.UseTransport(ibmmq);
endpointB.UseSerialization<SystemJsonSerializer>();
endpointB.PurgeOnStartup(true);

builder.UseNServiceBus(endpointB);

var host = builder.Build();

await host.StartAsync();

var instance = host.Services.GetRequiredService<IMessageSession>();

while (true)
{
    Console.ReadLine();
    Console.WriteLine("Sending message...");
    await instance.SendLocal(new MyMessage());
}


await host.StopAsync();


class MyHandler : IHandleMessages<MyMessage>
{
    public Task Handle(MyMessage message, IMessageHandlerContext context)
    {
        Console.WriteLine("Received message!");
        return Task.CompletedTask;
    }
}


record MyMessage(string Data = "Hello World!");