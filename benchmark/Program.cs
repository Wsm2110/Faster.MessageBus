using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using CommandBenchmark;

namespace BenchmarkSuite1
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<CommandDispatcherBenchmark>(new DebugInProcessConfig());
        }
    }
}
