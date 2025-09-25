using BenchmarkDotNet.Attributes;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Shared;
using Faster.MessageBus.Contracts;
using System;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace BenchmarkSuite1
{
    [MemoryDiagnoser]
    public class EventDispatcherBenchmark
    {
        private IMessageBroker _messageBroker;
        private IServiceProvider _serviceProvider;
        private TestEvent _testEvent;

        [GlobalSetup]
        public void Setup()
        {
            var services = new ServiceCollection();
            services.AddMessageBus(options =>
            {
                options.ApplicationName = "benchmark_node";
                options.RPCPort = 52345;
            });

            _serviceProvider = services.BuildServiceProvider();
            _messageBroker = _serviceProvider.GetRequiredService<IMessageBroker>();
            _testEvent = new TestEvent { Id = Guid.NewGuid(), Name = "BenchmarkEvent" };
        }

        [Benchmark]
        public void PublishEventAsync()
        {
            for (int i = 0; i < 1000000; i++)
            {
                _messageBroker.EventDispatcher.Publish(_testEvent); // If EventDispatcher has StreamAsync, use that instead
            }
        }

        public class TestEvent : IEvent
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        // Fixes for CS0305, CS1514, CS1513
        public class TestEventHandler : Faster.MessageBus.Contracts.IEventHandler<TestEvent>
        {
            public Task Handle(TestEvent msg)
            {
                return Task.CompletedTask;
            }
        }
    }
}
