using System;
using System.IO;

namespace Zyl.VectorTraits.Sample.Benchmarks {
    internal class Program {
        static void Main(string[] args) {
            TextWriter writer = Console.Out;
            writer.WriteLine("VectorTraits.Sample.Benchmarks");
            BenchmarksGlobal.RunBenchmarks(args, writer);
        }
    }
}
