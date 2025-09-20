using BenchmarkDotNet.Attributes;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Shared;
using Faster.MessageBus.Contracts;
using System;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace BenchmarkSuite1
{

    /// <summary>
    /// Benchmarks the performance of event publishing using the EventDispatcher via the IMessageBroker.
    /// </summary>
    [MemoryDiagnoser]
    public class MultiThreadedEventBenchmark
    {
        private IMessageBroker _messageBroker;
        private IServiceProvider _serviceProvider;
        private TestMultiThreadedEvent _testEvent;

        /// <summary>
        /// Sets up the DI container and initializes the message broker and test event.
        /// </summary>
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
            _testEvent = new TestMultiThreadedEvent { Id = Guid.NewGuid(), Name = "BenchmarkEvent" };
        }

        /// <summary>
        /// Publishes events in parallel using the EventDispatcher.
        /// This method simulates high-throughput event publishing.
        /// </summary>
        [Benchmark]
        public void PublishEventParallel()
        {         
            Parallel.For(0, 1_000_000, i =>
            {
                _messageBroker.EventDispatcher.Publish(_testEvent);
            });
        }

        /// <summary>
        /// Simple event type for benchmarking.
        /// </summary>
        public class TestMultiThreadedEvent : IEvent
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>
        /// Dummy event handler for TestEvent to satisfy handler registration.
        /// </summary>
        public class TestEventHandler : IEventHandler<TestMultiThreadedEvent>
        {
            public Task Handle(TestMultiThreadedEvent msg)
            {
                return Task.CompletedTask;
            }
        }
    }
}
   