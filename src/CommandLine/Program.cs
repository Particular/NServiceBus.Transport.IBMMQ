namespace NServiceBus.Transport.IbmMq.CommandLine;

using McMaster.Extensions.CommandLineUtils;

class Program
{
    static int Main(string[] args)
    {
        var app = new CommandLineApplication { Name = "ibmmq-transport" };

        var connectionOptions = ConnectionOptions.Register(app);

        app.HelpOption();
        EndpointCommand.Register(app, connectionOptions);
        QueueCommand.Register(app, connectionOptions);
        app.OnExecute(() => app.ShowHelp());

        return app.Execute(args);
    }
}
