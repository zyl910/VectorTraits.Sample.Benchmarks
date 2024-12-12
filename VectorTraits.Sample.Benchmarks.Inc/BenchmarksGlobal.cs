using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
#if NETCOREAPP3_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif
using System.Text;
using Zyl.VectorTraits;
using Zyl.VectorTraits.Sample.Benchmarks.Group;
using Zyl.VectorTraits.Sample.Benchmarks.Image;

namespace Zyl.VectorTraits.Sample.Benchmarks {
    /// <summary>
    /// Benchmarks global initializer (全局初始化器).
    /// </summary>
    internal class BenchmarksGlobal {
        private static bool m_Inited = false;

        /// <summary>
        /// Is release make.
        /// </summary>
        public static readonly bool IsRelease =
#if DEBUG
            false
#else
            true
#endif
        ;

        /// <summary>
        /// Do initialize (进行初始化).
        /// </summary>
        /// <param name="writer">The TextWriter.</param>
        public static void Init(TextWriter writer = null) {
            if (m_Inited) return;
            m_Inited = true;
            //string indentNext = indent + "\t";
            // VectorTraitsGlobal
            VectorTraitsGlobal.Init();
#if NETSTANDARD1_3_OR_GREATER || NETCOREAPP2_0_OR_GREATER || NET461_OR_GREATER
            // No need to set up `ProcessUtil.TypeOfProcess` properties. 
#else
            Zyl.VectorTraits.Impl.Util.ProcessUtil.TypeOfProcess = typeof(System.Diagnostics.Process);
#endif

            // Output.
            string indent = "";
            if (null == writer) writer = Console.Out;
            writer.WriteLine(indent + string.Format("IsRelease:\t{0}", IsRelease));
            writer.WriteLine(indent + string.Format("Environment.Version:\t{0}", Environment.Version));
#if (NET47 || NET462 || NET461 || NET46 || NET452 || NET451 || NET45 || NET40 || NET35 || NET20) || (NETSTANDARD1_0)
#else
            writer.WriteLine(indent + string.Format("RuntimeInformation.FrameworkDescription:\t{0}", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription));
            writer.WriteLine(indent + string.Format("RuntimeInformation.OSArchitecture:\t{0}", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture));
            writer.WriteLine(indent + string.Format("RuntimeInformation.OSDescription:\t{0}", System.Runtime.InteropServices.RuntimeInformation.OSDescription)); // Same Environment.OSVersion. It's more accurate.
#endif
#if NET5_0_OR_GREATER
            writer.WriteLine(indent + string.Format("RuntimeInformation.RuntimeIdentifier:\t{0}", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier)); // e.g. win10-x64
#endif // NET5_0_OR_GREATER
            writer.WriteLine(indent + string.Format("IntPtr.Size:\t{0}", IntPtr.Size));
            writer.WriteLine(indent + string.Format("BitConverter.IsLittleEndian:\t{0}", BitConverter.IsLittleEndian));
            writer.WriteLine(indent + string.Format("Vector.IsHardwareAccelerated:\t{0}", Vector.IsHardwareAccelerated));
            writer.WriteLine(indent + string.Format("Vector<byte>.Count:\t{0}\t# {1}bit", Vector<byte>.Count, Vector<byte>.Count * sizeof(byte) * 8));
#if NET7_0_OR_GREATER
            writer.WriteLine(indent + string.Format("Vector128.IsHardwareAccelerated:\t{0}", Vector128.IsHardwareAccelerated));
            writer.WriteLine(indent + string.Format("Vector256.IsHardwareAccelerated:\t{0}", Vector256.IsHardwareAccelerated));
#endif // NET7_0_OR_GREATER
#if NET8_0_OR_GREATER
            writer.WriteLine(indent + string.Format("Vector512.IsHardwareAccelerated:\t{0}", Vector512.IsHardwareAccelerated));
#endif // NET8_0_OR_GREATER
            writer.WriteLine(indent + string.Format("VectorEnvironment.CpuModelName:\t{0}", VectorEnvironment.CpuModelName));
            if (!string.IsNullOrEmpty(VectorEnvironment.CpuFlags)) {
                writer.WriteLine(indent + string.Format("VectorEnvironment.CpuFlags:\t{0}", VectorEnvironment.CpuFlags));
            }
            writer.WriteLine(indent + string.Format("VectorEnvironment.SupportedInstructionSets:\t{0}", VectorEnvironment.SupportedInstructionSets));
            writer.WriteLine(indent + string.Format("Vectors.Instance:\t{0}\t// {1}", Vectors.Instance.GetType().Name, Vectors.Instance.UsedInstructionSets));

            // done.
            Debug.WriteLine("Zyl.VectorTraits.Sample.Benchmarks initialize done.");
#if (NETSTANDARD1_1)
#else
            Trace.WriteLine("Zyl.VectorTraits.Sample.Benchmarks initialize done.");
#endif
        }

        /// <summary>
        /// Run benchmarks
        /// </summary>
        /// <param name="args">The command args.</param>
        /// <param name="writer">The TextWriter.</param>
        public static void RunBenchmarks(string[] args, TextWriter writer = null) {
            if (null == writer) writer = Console.Out;
            Init(writer);
            writer.WriteLine();
            // Command lines.
            // -check   Only run check.
            bool onlyCheck = false;
            for (int i = 0; i < args.Length; i++) {
                string cur = args[i];
                if ("-check".Equals(cur, StringComparison.OrdinalIgnoreCase)) {
                    onlyCheck = true;
                }
            }
            // Check.
            DoCheck(writer);
            // Run.
            if (!onlyCheck) {
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
                var summary = BenchmarkRunner.Run(typeof(BenchmarksGlobal).Assembly, config);
                writer.WriteLine("Length={0}, {1}", summary.Length, summary);
            }
        }

        /// <summary>
        /// Do check (进行测试).
        /// </summary>
        /// <param name="writer">The TextWriter.</param>
        public static void DoCheck(TextWriter writer = null) {
            // - Group
            //var target = new SplitLanes4Double() { Count = 1000 };
            // - Image
            var target = new Bgr24ToGray8Benchmark() { Width = 1024 };
            //var target = new Bgr24ToGrayBgr24Benchmark() { Width = 1024 };
            //var target = new ImageFlipXOn24bitBenchmark() { Width = 1024 };
            //var target = new ImageFlipXOn32bitBenchmark() { Width = 1024 };
            //var target = new ImageFlipYBenchmark() { Width = 1024 };
            //var target = new Rgb32ToGray8Benchmark() { Width = 1024 };
            // - Run
            target.Setup();
            writer.WriteLine("Finish check.");
            if (target is IDisposable disposable) {
                disposable.Dispose();
            }
        }

    }
}