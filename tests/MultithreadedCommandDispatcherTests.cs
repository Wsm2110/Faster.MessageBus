using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Faster.MessageBus.Contracts;
using Faster.MessageBus.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static UnitTests.MachineDispatcherTests;

namespace UnitTests;

public class MultithreadedCommandDispatcherTests
{
    [Fact]
    public async Task MachineDispatcher_Handles_Multiple_Concurrent_Commands()
    {
        using var machineBase = new MachineBase();
        var (_, broker) = machineBase.CreateMessageBus();

        await Task.Delay(TimeSpan.FromSeconds(1)); // allow discovery

        int threadCount = 8;
        int commandsPerThread = 50;
        var results = new ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, threadCount).Select(thread =>
            Task.Run(async () =>
            {
                for (int i = 0; i < commandsPerThread; i++)
                {
                    await foreach (var response in broker.CommandDispatcher.Machine.StreamAsync(new TestPing($"ping-{thread}-{i}"), TimeSpan.FromSeconds(2)))
                    {
                        results.Add(response?.ToString() ?? "");
                    }
                }
            })
        ).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(threadCount * commandsPerThread, results.Count);
    }

    [Fact]
    public async Task ClusterDispatcher_Handles_Parallel_Commands()
    {
        var services = new ServiceCollection().AddMessageBus(options =>
        {
            options.Cluster.ClusterName = "multithreadCluster";
        });
        var provider = services.BuildServiceProvider();
        var broker = provider.GetRequiredService<IMessageBroker>();

        await Task.Delay(TimeSpan.FromSeconds(1)); // allow discovery

        int parallelCount = 16;
        var responses = new ConcurrentBag<object>();

        Parallel.For(0, 16, i =>
        {
            var task = Task.Run(async () =>
            {
                await foreach (var resp in broker.CommandDispatcher.Cluster.StreamAsync(new TestPing($"cluster-{i}"), TimeSpan.FromSeconds(1)))
                {
                    responses.Add(resp);
                }
            });
            task.Wait();
        });

        Assert.Equal(parallelCount, responses.Count);
    }
}