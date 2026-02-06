using Microsoft.Extensions.Hosting;
using NServiceBus.Transport.IbmMq;

Console.Title = "Subscriber.FullTransportEndpoint";
var builder = Host.CreateApplicationBuilder(args);

var ibmmq = new IbmMqTransport(settings =>
{
    settings.QueueManagerName = "QM1";
    settings.Host = "localhost";
    settings.Port = 1414;
    settings.Channel = "DEV.APP.SVRCONN";
    settings.User = "admin";
    settings.Password = "passw0rd";
});

var endpointB = new EndpointConfiguration("DEV.SHIPPING");
endpointB.SendFailedMessagesTo("error");
endpointB.UseTransport(ibmmq);
endpointB.UseSerialization<SystemJsonSerializer>();
endpointB.PurgeOnStartup(true);
endpointB.LimitMessageProcessingConcurrencyTo(2);

builder.UseNServiceBus(endpointB);

var host = builder.Build();

await host.RunAsync();