using BenchmarkDotNet.Attributes;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Features.Commands.Contracts;
using Faster.MessageBus.Features.Events.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static BenchmarkSuite1.MultiThreadedEventBenchmark;

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
        private TestCommand _testCommand;

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
            _testCommand = new TestCommand { Id = Guid.NewGuid(), Payload = "BenchmarkCommand" };
        }

        /// <summary>
        /// Publishes events in parallel using the EventDispatcher.
        /// This method simulates high-throughput event publishing.
        /// </summary>
        //[Benchmark]
        //public void PublishEventParallel()
        //{         
        //    Parallel.For(0, 1_000_000, i =>
        //    {
        //        _messageBroker.EventDispatcher.Publish(_testEvent);
        //    });
        //}

        /// <summary>
        /// Dispatches commands in parallel using the CommandDispatcher.
        /// This method simulates high-throughput command dispatching.
        /// </summary>
        [Benchmark]
        public void DispatchCommandParallel()
        {
            List<Task> tasks = new List<Task>(); 

            Parallel.For(0, 10000, i =>
            {
                var task = Task.Run(async () =>
                {
                   await _messageBroker.CommandDispatcher.Machine.SendAsync(new TestCommand(), TimeSpan.FromSeconds(1));                  
                });

                if(task != null)
                tasks.Add(task);

            });
            Task.WaitAll(tasks);
             
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

        /// <summary>
        /// Simple command type for benchmarking.
        /// </summary>
        public class TestCommand : ICommand
        {
            public Guid Id { get; set; }
            public string Payload { get; set; }
        }

        /// <summary>
        /// Dummy command handler for TestCommand to satisfy handler registration.
        /// </summary>
        public class TestCommandHandler : ICommandHandler<TestCommand>
        {
            public Task Handle(TestCommand command, CancellationToken src)
            {
                return Task.CompletedTask;
            }
        }
    }
}
