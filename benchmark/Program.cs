using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace BenchmarkSuite1
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<MultiThreadedEventBenchmark>(new DebugInProcessConfig());
        }
    }
}
