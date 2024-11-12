﻿using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Zyl.VectorTraits.Sample.Benchmarks.NetFw {
    internal class Program {
        static void Main(string[] args) {
            TextWriter writer = Console.Out;
            writer.WriteLine("VectorTraits.Sample.Benchmarks.NetFw");
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
            config = config.AddJob(Job.Default //Job.MediumRun
                                               //.WithLaunchCount(1)
                                               //.WithToolchain(InProcessEmitToolchain.Instance)
                                               //.WithId("InProcess")
                );
            var summary = BenchmarkRunner.Run(typeof(Program).Assembly, config);
            writer.WriteLine("Length={0}, {1}", summary.Length, summary);
        }
    }
}