using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.Options;

namespace Faster.MessageBus.Features.Commands.Shared
{
    public delegate bool SocketValidationDelegate(MeshContext context, IOptions<MessageBrokerOptions> options);

    public class SocketStrategy(SocketValidationDelegate validationDelegate) : ISocketStrategy
    {
        public bool Validate(MeshContext context, IOptions<MessageBrokerOptions> options)
        {
            return validationDelegate.Invoke(context, options);
        }
    }
}