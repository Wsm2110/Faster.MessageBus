using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Faster.Transport;
using Faster.Transport.Friendly;
using Faster.Transport.Serialization;
using MessagePack;
using System;
using System.Net;
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

    private Memory<byte> _payload = null!;
    private int _messageCount;
    private ManualResetEventSlim _done = null!;
    private int _received;
    private TaskCompletionSource tcs;

    /// <summary>
    /// Number of round-trip messages to send.
    /// </summary>
    [Params(10_000)]
    public int MessageCount { get; set; }

    /// <summary>
    /// Size of each message payload (bytes).
    /// </summary>
    [Params(20)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _messageCount = MessageCount;
        _payload = new byte[PayloadSize];

        //_server = new FasterServer(new IPEndPoint(IPAddress.Loopback, 5555)); 
        //_server.Start();

        //_done = new ManualResetEventSlim(false);
        //_client = new FasterClient(new IPEndPoint(IPAddress.Loopback, 5555));

        // Optionally send back a response
        //string reply = $"Server received";
        //var replyBytes = System.Text.Encoding.UTF8.GetBytes(reply);
        //_server.OnReceived += async (client, frame) =>
        //{     

        //    client.TrySend(replyBytes);
        //};

        //_client!.OnReceived += (_, frame) =>
        //{

        //};

        // give server time to accept
        // Thread.Sleep(200);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client?.Dispose();
        _server?.Dispose();       
    }

    [Benchmark(Description = "Roundtrip throughput")]
    public async Task RoundTripThroughput()
    {
        //Interlocked.Exchange(ref _received, 0);
        ////  tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        //for (int i = 0; i < _messageCount; i++)
        //{
        //    _client.TrySend(_payload);                   
        //}

        //var completed = await Task.WhenAny(tcs.Task, Task.Delay(10000));
        //if (completed != tcs.Task)
        //    throw new TimeoutException("Roundtrip benchmark timed out.");  

        //await tcs.Task; // ✅ async wait, no blocking

        var perf = new PerformanceMode(HighPriority: true, InlineHandlers: false);
        var serializer = new MessagePackNetSerializer();

        using var server = Net.Listen("tcp://127.0.0.1:5555", serializer, perf)
            .On<Trade>((s, t) =>
            {

            })
            .Start();

        using var client = Net.Connect("tcp://127.0.0.1:5555", serializer, perf)
            .On<Trade>((_, t) =>
            {
                Console.WriteLine($"got trade {t.Symbol} x{t.Qty} @ {t.Px}");
            });

        for (int i = 0; i < 100_000; i++)
        {
            var res = client.Send(new Trade("MSFT", 100, 351.12m));
        }
    }

    /// <summary>
    /// Represents a simple trade message for high-speed transport.
    /// Annotated for MessagePack serialization.
    /// </summary>
    [MessagePackObject] // Enables code generation and schema-based serialization
    public record struct Trade(
        [property: Key(0)] string Symbol, // Symbol name (e.g., "AAPL")
        [property: Key(1)] int Qty,       // Quantity traded
        [property: Key(2)] decimal Px     // Trade price
    );

}