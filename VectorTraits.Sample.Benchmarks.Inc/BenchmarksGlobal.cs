using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
#if NETCOREAPP3_0_OR_GREATER
using System.Runtime.Intrinsics;
#endif
using System.Text;
using Zyl.VectorTraits;

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
        public static void Init() {
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
            TextWriter writer = Console.Out;
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

    }
}
