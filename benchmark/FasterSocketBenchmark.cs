using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Faster.Transport;
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Benchmarks FasterServer + FasterClient performance using BenchmarkDotNet.
/// Measures round-trip latency and throughput for framed message I/O.
/// </summary>
[MemoryDiagnoser]                 // track allocations
[GcServer(true)]                  // use server GC for stable perf
[WarmupCount(2)]                  // short warmup
[IterationCount(5)]               // reliable average

public class FasterSocketBenchmark
{
    private FasterServer? _server;
    private FasterClient? _client;

    private byte[] _payload = null!;
    private int _messageCount;
    private ManualResetEventSlim _done = null!;
    private int _received;
    private TaskCompletionSource tcs;

    /// <summary>
    /// Number of round-trip messages to send.
    /// </summary>
    [Params(50000)]
    public int MessageCount { get; set; }

    /// <summary>
    /// Size of each message payload (bytes).
    /// </summary>
    [Params(1024)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _messageCount = MessageCount;
        _payload = new byte[PayloadSize];
        new Random(42).NextBytes(_payload);

        _server = new FasterServer(new IPEndPoint(IPAddress.Loopback, 5555));
        _server.Start();

        _done = new ManualResetEventSlim(false);
        _client = new FasterClient(new IPEndPoint(IPAddress.Loopback, 5555));

        _server.OnReceived += async (client, frame) =>
        {
            var msg = Encoding.UTF8.GetString(frame.Span);
            if (!msg.StartsWith("RESP")) // respond only to requests
            {
                var reply = Encoding.UTF8.GetBytes("RESP: OK");
                await client.SendAsync(reply);
            }
        };


        _client!.OnReceived += (_, frame) =>
        {
            int n = Interlocked.Increment(ref _received);
            if (n >= _messageCount)
                tcs.TrySetResult();
        };


        // give server time to accept
        Thread.Sleep(200);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _server?.Dispose();
        _done.Dispose();
    }

    [Benchmark(Description = "Roundtrip throughput")]
    public async Task RoundTripThroughput()
    {
       Interlocked.Exchange(ref _received, 0) ;
        tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        int serverCounter = 0;
  
        var payloadMem = _payload.AsMemory();
        for (int i = 0; i < _messageCount; i++)
            await _client.SendAsync(payloadMem);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        if (completed != tcs.Task)
            throw new TimeoutException("Roundtrip benchmark timed out.");

        await tcs.Task; // ✅ async wait, no blocking
     }
}