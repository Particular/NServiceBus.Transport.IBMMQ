using FullTransportEndpoint.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FullTransportEndpoint.Subscriber
{

    sealed class MyHandler : IHandleMessages<MyMessage>
    {
        public async Task Handle(MyMessage message, IMessageHandlerContext context)
        {
            Console.WriteLine($"Start {message.Data}");
            await Task.Delay(200, context.CancellationToken);
            Console.WriteLine($"End: {message.Data}");
        }
    }
}
