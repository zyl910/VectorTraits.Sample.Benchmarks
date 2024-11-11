using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Zyl.VectorTraits.Sample.Benchmarks {
    /// <summary>
    /// Benchmarks global initializer (全局初始化器).
    /// </summary>
    internal class BenchmarksGlobal {
        private static bool m_Inited = false;

        /// <summary>
        /// Do initialize (进行初始化).
        /// </summary>
        public static void Init() {
            if (!m_Inited) return;
            m_Inited = true;
            // Initialize.
            // done.
            Debug.WriteLine("Zyl.VectorTraits.Sample.Benchmarks initialize done.");
#if (NETSTANDARD1_1)
#else
            Trace.WriteLine("Zyl.VectorTraits.Sample.Benchmarks initialize done.");
#endif
        }

    }
}
