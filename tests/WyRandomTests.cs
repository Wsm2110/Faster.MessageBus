using System;
using System.Collections.Generic;
using System.Threading;
using Faster.MessageBus.Shared;
using Xunit;

namespace UnitTests;

public class WyRandomTests
{
    [Fact]
    public void NextInt64_ReturnsDifferentValues()
    {
        var rng = new WyRandom(12345UL);
        ulong first = rng.NextInt64();
        ulong second = rng.NextInt64();
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NextInt64_IsDeterministicWithSeed()
    {
        var rng1 = new WyRandom(987654321UL);
        var rng2 = new WyRandom(987654321UL);
        Assert.Equal(rng1.NextInt64(), rng2.NextInt64());
        Assert.Equal(rng1.NextInt64(), rng2.NextInt64());
    }

    [Fact]
    public void Next_ReturnsNonNegative()
    {
        var rng = new WyRandom(42UL);
        for (int i = 0; i < 100; i++)
        {
            int value = rng.Next();
            Assert.InRange(value, 0, int.MaxValue);
        }
    }

    [Fact]
    public void Shared_IsThreadLocal()
    {
        ulong value1 = WyRandom.Shared.NextInt64();
        ulong value2 = 0;
        var thread = new Thread(() =>
        {
            value2 = WyRandom.Shared.NextInt64();
        });
        thread.Start();
        thread.Join();
        Assert.NotEqual(value1, value2);
    }

    [Fact]
    public void NextInt64_NoDuplicatesInSequence()
    {
        var rng = new Faster.MessageBus.Shared.WyRandom(2024);
        var set = new HashSet<ulong>();
        for (int i = 0; i < 10000; i++)
        {
            ulong value = rng.NextInt64();
            Assert.True(set.Add(value), $"Duplicate value detected: {value} at iteration {i}");
        }
    }

    [Fact]
    public void NextInt64_NoDuplicatesInParallelThreadsUsingSharedInstance()
    {
        const int threadCount = 8;
        const int valuesPerThread = 2000;
        var allValues = new HashSet<ulong>();
        var locks = new object();
        var threads = new Thread[threadCount];

        void GenerateValues(int threadIndex)
        {
            // Each thread uses a different seed to avoid deterministic overlap
        
            for (int i = 0; i < valuesPerThread; i++)
            {
                ulong value = WyRandom.Shared.NextInt64();
                lock (locks)
                {
                    Assert.True(allValues.Add(value), $"Duplicate value detected: {value} in thread {threadIndex} at iteration {i}");
                }
            }
        }

        for (int t = 0; t < threadCount; t++)
        {
            int idx = t;
            threads[t] = new Thread(() => GenerateValues(idx));
            threads[t].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        Assert.Equal(threadCount * valuesPerThread, allValues.Count);
    }

    [Fact]
    public void NextInt64_NoDuplicatesInParallelThreads()
    {
        const int threadCount = 8;
        const int valuesPerThread = 2000;
        var allValues = new HashSet<ulong>();
        var locks = new object();
        var threads = new Thread[threadCount];

        void GenerateValues(int threadIndex)
        {
            // Each thread uses a different seed to avoid deterministic overlap
            var rng = new Faster.MessageBus.Shared.WyRandom((ulong)(2024 + threadIndex));
            for (int i = 0; i < valuesPerThread; i++)
            {
                ulong value = rng.NextInt64();
                lock (locks)
                {
                    Assert.True(allValues.Add(value), $"Duplicate value detected: {value} in thread {threadIndex} at iteration {i}");
                }
            }
        }

        for (int t = 0; t < threadCount; t++)
        {
            int idx = t;
            threads[t] = new Thread(() => GenerateValues(idx));
            threads[t].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        Assert.Equal(threadCount * valuesPerThread, allValues.Count);
    }

    [Fact]
    public void NextInt64_NoDuplicatesInParallelThreads_ThreadSafe()
    {
        const int threadCount = 8;
        const int valuesPerThread = 2000;
        var allValues = new System.Collections.Concurrent.ConcurrentDictionary<ulong, byte>();
        var threads = new Thread[threadCount];

        void GenerateValues(int threadIndex)
        {
            // Each thread uses a different seed to avoid deterministic overlap       
            for (int i = 0; i < valuesPerThread; i++)
            {
                ulong value = WyRandom.Shared.NextInt64();
                Assert.True(allValues.TryAdd(value, 0), $"Duplicate value detected: {value} in thread {threadIndex} at iteration {i}");
            }
        }

        for (int t = 0; t < threadCount; t++)
        {
            int idx = t;
            threads[t] = new Thread(() => GenerateValues(idx));
            threads[t].Start();
        }

        foreach (var thread in threads)
            thread.Join();

        Assert.Equal(threadCount * valuesPerThread, allValues.Count);
    }
}