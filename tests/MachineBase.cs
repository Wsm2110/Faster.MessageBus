using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;

namespace UnitTests
{
    internal class MachineBase : IDisposable
    {
        private static int _portOffset = 0;
        IList<ServiceProvider> _providers = new List<ServiceProvider>();
        /// <summary>
        /// Helper method to create and configure a MessageBus instance for testing.
        /// It ensures a unique RPC port for each instance to avoid conflicts during parallel test execution.
        /// </summary>
        /// <param name="configureServices">An optional action to register handlers or other services.</param>
        /// <returns>A tuple containing the configured ServiceProvider and IMessageBroker.</returns>
        public (ServiceProvider Provider, IMessageBroker Broker) CreateMessageBus(Action<IServiceCollection> configureServices = null)
        {
            var services = new ServiceCollection();
            services.AddMessageBus(options =>
            {
                // ensure a valid port, unique enough for tests running in parallel on CI and in the same process
                int portOffset = Interlocked.Increment(ref _portOffset);
                options.RPCPort = (ushort)(52000 + (Environment.ProcessId % 1000) + portOffset);
            });

            // allow tests to register specific handlers or other services
            configureServices?.Invoke(services);

            var provider = services.BuildServiceProvider();
            var broker = provider.GetRequiredService<IMessageBroker>();
            _providers.Add(provider);
            return (provider, broker);
        }

        public void Dispose()
        {
            foreach (var provider in _providers)
            {
                provider.Dispose();
            }
            _providers.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
