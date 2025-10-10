using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    [Theory]
    //[InlineData(1)]
    //[InlineData(10)]
    //[InlineData(100)]
    [InlineData(1000)]
    public async Task ClusterDispatcher_Handles_Parallel_Commands(int length)
    {
    
        var services = new ServiceCollection().AddMessageBus(options =>
        {
            options.Cluster.ClusterName = "multithreadCluster";
        });

        await using var provider = services.BuildServiceProvider();

        var broker = provider.GetRequiredService<IMessageBroker>();

        await Task.Delay(TimeSpan.FromSeconds(1)); // allow discovery

        // We will use a regular List for the responses since the worker tasks will handle the adding.
        var responses = new List<Task>();
        var totalTasks = length;
        var numberOfWorkers = 8;
        var chunkSize = totalTasks / numberOfWorkers;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        CancellationToken totalTimeoutToken = cts.Token;
        
        // 1. Create 8 independent worker tasks.
        for (int workerIndex = 0; workerIndex < numberOfWorkers; workerIndex++)
        {
            int start = workerIndex * chunkSize;
            int end = (workerIndex == numberOfWorkers - 1) ? totalTasks : start + chunkSize;

            // Use Task.Run to launch a new thread pool task that will execute the loop synchronously
            // for its portion of the work. The 'await' inside SendAsync will free the thread for I/O.
            var workerTask = Task.Run(async () =>
            {
                for (int i = start; i < end; i++)
                {
                    // The commandTask is the actual asynchronous I/O operation
                    var commandTask = broker.CommandDispatcher.Machine.SendAsync(new TestPing($"Snap-{i}"), TimeSpan.FromSeconds(10), ct: totalTimeoutToken);

                    // Wait for THIS command to complete before proceeding to the next index (i)
                    // in this worker's loop. This is the key difference: it limits the rate 
                    // at which each of the 8 workers can issue commands.
                    await commandTask;
                }
            }, totalTimeoutToken); // Pass the token to Task.Run to allow cancellation before starting

            // Add the 8 main worker tasks to the list for Task.WhenAll
            responses.Add(workerTask);
        }

        // 2. Wait for the 8 worker tasks to complete.
        try
        {
            await Task.WhenAll(responses).WaitAsync(totalTimeoutToken);
            Console.WriteLine($"All {totalTasks} commands processed by {numberOfWorkers} workers.");
        }
        catch (OperationCanceledException) when (totalTimeoutToken.IsCancellationRequested)
        {
            Console.WriteLine("Task batch timed out.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An exception occurred: {ex.Message}");
        }
    }
}