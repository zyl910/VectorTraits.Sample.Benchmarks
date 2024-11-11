using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Zyl.VectorTraits;

namespace Zyl.VectorTraits.Sample.Benchmarks {
    internal class Program {
        static void Main(string[] args) {
            TextWriter writer = Console.Out;
            writer.WriteLine("Zyl.VectorTraits.Sample.Benchmarks");
            BenchmarksGlobal.Init();
            writer.WriteLine();
            // Run.
            Architecture architecture = RuntimeInformation.OSArchitecture;
            var config = DefaultConfig.Instance;
            if (architecture == Architecture.X86 || architecture == Architecture.X64) {
                config = config.AddDiagnoser(new DisassemblyDiagnoser(new DisassemblyDiagnoserConfig(maxDepth: 3, printSource: true, printInstructionAddresses: true, exportGithubMarkdown: true, exportHtml: true)));
            } else {
                // Message: Arm64 is not supported (Iced library limitation)
            }
            config = config.AddJob(Job.MediumRun
                //.WithLaunchCount(1)
                //.WithToolchain(InProcessEmitToolchain.Instance)
                //.WithId("InProcess")
                );
            var summary = BenchmarkRunner.Run(typeof(Program).Assembly, config);
            writer.WriteLine("Length={0}, {1}", summary.Length, summary);
        }
    }
}
