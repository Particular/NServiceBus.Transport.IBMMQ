using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NServiceBus.IbmMq.Messages;
using NServiceBus.IbmMq.Sales;

Console.Title = "Sales";

var builder = Host.CreateApplicationBuilder(args);

// TODO: consider moving common endpoint configuration into a shared project
// for use by all endpoints in the system

var endpointConfiguration = new EndpointConfiguration("DEV.SALES");

// Learning Transport: https://docs.particular.net/transports/learning/
// new IbmMqTransport("localhost", "admin", "passw0rd", null, null)
var routing = endpointConfiguration.UseTransport(new LearningTransport());

// Define routing for commands: https://docs.particular.net/nservicebus/messaging/routing#command-routing
routing.RouteToEndpoint(typeof(ShippingStatusRequest), "DEV.SHIPPING");
// routing.RouteToEndpoint(typeof(MessageType).Assembly, "DestinationForAllCommandsInAssembly");

// Learning Persistence: https://docs.particular.net/persistence/learning/
endpointConfiguration.UsePersistence<LearningPersistence>();

// Message serialization
endpointConfiguration.UseSerialization<SystemJsonSerializer>();

endpointConfiguration.DefineCriticalErrorAction(OnCriticalError);

endpointConfiguration.Recoverability().Immediate(i => i.NumberOfRetries(0));
endpointConfiguration.Recoverability().Delayed(d => d.NumberOfRetries(0));

// Installers are useful in development. Consider disabling in production.
// https://docs.particular.net/nservicebus/operations/installers
endpointConfiguration.EnableInstallers();

builder.UseNServiceBus(endpointConfiguration);

builder.Services.AddHostedService<InputLoopService>();

await builder.Build().RunAsync();

Console.WriteLine("Running!");

static async Task OnCriticalError(ICriticalErrorContext context, CancellationToken cancellationToken)
{
    // TODO: decide if stopping the endpoint and exiting the process is the best response to a critical error
    // https://docs.particular.net/nservicebus/hosting/critical-errors
    // and consider setting up service recovery
    // https://docs.particular.net/nservicebus/hosting/windows-service#installation-restart-recovery
    try
    {
        await context.Stop(cancellationToken);
    }
    finally
    {
        FailFast($"Critical error, shutting down: {context.Error}", context.Exception);
    }
}

static void FailFast(string message, Exception exception)
{
    try
    {
        // TODO: decide what kind of last resort logging is necessary
        // TODO: when using an external logging framework it is important to flush any pending entries prior to calling FailFast
        // https://docs.particular.net/nservicebus/hosting/critical-errors#when-to-override-the-default-critical-error-action
    }
    finally
    {
        Environment.FailFast(message, exception);
    }
}
