using Microsoft.Extensions.Hosting;
using NServiceBus.IbmMq;

Console.Title = "Shipping";

var builder = Host.CreateApplicationBuilder(args);

var endpointConfiguration = new EndpointConfiguration("DEV.SHIPPING");

var routing = endpointConfiguration.UseTransport(new IbmMqTransport("localhost", "admin", "passw0rd", null, null));

endpointConfiguration.UsePersistence<LearningPersistence>();

endpointConfiguration.UseSerialization<SystemJsonSerializer>();

endpointConfiguration.Recoverability().Immediate(i => i.NumberOfRetries(0));
endpointConfiguration.Recoverability().Delayed(d => d.NumberOfRetries(0));

builder.UseNServiceBus(endpointConfiguration);

await builder.Build().RunAsync();
