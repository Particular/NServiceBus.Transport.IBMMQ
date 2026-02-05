using Microsoft.Extensions.Hosting;
using NServiceBus.Transport.IbmMq;

Console.Title = "Subscriber.FullTransportEndpoint";
var builder = Host.CreateApplicationBuilder(args);

var ibmmq = new IbmMqTransport("localhost", "admin", "passw0rd", null, null);

var endpointB = new EndpointConfiguration("DEV.SHIPPING");
endpointB.SendFailedMessagesTo("error");
endpointB.UseTransport(ibmmq);
endpointB.UseSerialization<SystemJsonSerializer>();
endpointB.PurgeOnStartup(true);
endpointB.LimitMessageProcessingConcurrencyTo(2);

builder.UseNServiceBus(endpointB);

var host = builder.Build();

await host.RunAsync();