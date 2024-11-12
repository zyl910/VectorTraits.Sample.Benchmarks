#undef BENCHMARKS_OFF

using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if NETCOREAPP3_0_OR_GREATER
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zyl.VectorTraits;

namespace Zyl.VectorTraits.Sample.Benchmarks.Group {
#if BENCHMARKS_OFF
    using BenchmarkAttribute = FakeBenchmarkAttribute;
#else
#endif // BENCHMARKS_OFF

    /// <summary>
    /// How do I optimally fill multiple arrays with SIMDs vectors? https://stackoverflow.com/questions/77984612/how-do-i-optimally-fill-multiple-arrays-with-simds-vectors/
    /// </summary>
    [MemoryDiagnoser(false)]
    public class SplitLanes4Double {
        private static readonly Random _random = new Random(1337);
        private static Coordinate4D[] _array;
        private static double[][] _destinationArray;

        [Params(1000, 10_000, 100_000)]
        public int Count { get; set; }

        [GlobalSetup]
        public void Setup() {
            _array = Enumerable.Range(0, Count)
                .Select(_ => {
                    var x = _random.Next();
                    var y = _random.Next();
                    var z = _random.Next();
                    var w = _random.Next();
                    return new Coordinate4D(x, y, z, w);
                })
                .ToArray();
            _destinationArray = new double[][] {
                new double[Count],
                new double[Count],
                new double[Count],
                new double[Count]
            };
            // Check.
            bool allowCheck = true;
            if (allowCheck) {
                try {
                    TextWriter writer = Console.Out;
                    double[][] expected = Linq();
                    double[][] dst;
                    // Unzip.
                    dst = Unzip();
                    if (!CheckEquals(expected, dst)) writer.WriteLine("Unzip results are not correct!");
                    // Soonts.
#if NET7_0_OR_GREATER
                    dst = Soonts();
                    if (!CheckEquals(expected, dst)) writer.WriteLine("Soonts results are not correct!");
#endif // NET7_0_OR_GREATER
                } catch (Exception ex) {
                    Debug.WriteLine(ex.ToString());
                }
            }
        }

        private bool CheckEquals(double[][] expected, double[][] dst) {
            if (expected == dst) return true;
            if (null == expected) return false;
            if (null == dst) return false;
            if (expected.Length != dst.Length) return false;
            for (int i = 0; i < expected.Length; ++i) {
                if (!expected[i].SequenceEqual(dst[i])) return false;
            }
            return true;
        }

        [Benchmark]
        public double[][] Linq() {
            var result = new double[4][];

            result[0] = _array.Select(x => x.X).ToArray();
            result[1] = _array.Select(x => x.Y).ToArray();
            result[2] = _array.Select(x => x.Z).ToArray();
            result[3] = _array.Select(x => x.W).ToArray();

            return result;
        }

        [Benchmark(Baseline = true)]
        public double[][] ParallelFor() {
            var result = new double[][]
            {
                new double[Count],
                new double[Count],
                new double[Count],
                new double[Count]
            };

            Parallel.For(0, Count, i =>
            {
                result[0][i] = _array[i].X;
                result[1][i] = _array[i].Y;
                result[2][i] = _array[i].Z;
                result[3][i] = _array[i].W;
            });

            return result;
        }

        [Benchmark]
        public double[][] ParallelForNoNew() {
            var result = _destinationArray;

            Parallel.For(0, Count, i => {
                result[0][i] = _array[i].X;
                result[1][i] = _array[i].Y;
                result[2][i] = _array[i].Z;
                result[3][i] = _array[i].W;
            });

            return result;
        }

        // == Use VectorTraits. https://www.nuget.org/packages/VectorTraits

        [Benchmark]
        public double[][] Unzip() {
            ReadOnlySpan<double> source = MemoryMarshal.Cast<Coordinate4D, double>(_array.AsSpan());
            var result = UnzipBatch(source, _destinationArray);
            return result;
        }

        public static double[][] UnzipBatch(ReadOnlySpan<double> source, double[][] destinationArray = null) {
            const int groupSize = 4; // XYZW
            int length = source.Length / groupSize;
            if (length <= 0) throw new ArgumentException("length <= 0!");

            double[][] result = destinationArray;
            if (null == result || result.Length < 4 || result[0].Length < length || result[1].Length < length || result[2].Length < length || result[3].Length < length) {
                result = new double[][] {
                        new double[length],
                        new double[length],
                        new double[length],
                        new double[length]
                    };
            }

            unsafe {
                fixed (double* p = source)
                fixed (double* x = result[0])
                fixed (double* y = result[1])
                fixed (double* z = result[2])
                fixed (double* w = result[3]) {
                    UnzipBatch(p, length, x, y, z, w);
                }
            }
            return result;
        }

        static unsafe void UnzipBatch(double* source, int length, double* x, double* y, double* z, double* w) {
            const int groupSize = 4; // XYZW
            int vectorWidth = Vector<double>.Count;
            int blockSize = vectorWidth * groupSize;
            int maskAlign = -vectorWidth;
            double* pEndAligned = source + (length & maskAlign) * groupSize;
            double* pEnd = source + length * groupSize;
            double* p = source;

            // Handle majority
            for (; p < pEndAligned; p += blockSize, x += vectorWidth, y += vectorWidth, z += vectorWidth, w += vectorWidth) {
                // Load
                Vector<double>* pVector = (Vector<double>*)p;
                Vector<double> a0 = pVector[0];
                Vector<double> a1 = pVector[1];
                Vector<double> a2 = pVector[2];
                Vector<double> a3 = pVector[3];
                // Group4Unzip
                var b0 = Vectors.YGroup4Unzip(a0, a1, a2, a3, out var b1, out var b2, out var b3);
                // Store
                *(Vector<double>*)x = b0;
                *(Vector<double>*)y = b1;
                *(Vector<double>*)z = b2;
                *(Vector<double>*)w = b3;
            }

            // Handle remainder
            for (; p < pEnd; p += groupSize, x++, y++, z++, w++) {
                *x = p[0];
                *y = p[1];
                *z = p[2];
                *w = p[3];
            }
        }


        // == From Soonts. https://stackoverflow.com/questions/77984612/how-do-i-optimally-fill-multiple-arrays-with-simds-vectors/
#if NET7_0_OR_GREATER // Vector128.Load need .NET 7.0

        [Benchmark]
        public double[][] Soonts() {
            ReadOnlySpan<Vector256<double>> source = MemoryMarshal.Cast<Coordinate4D, Vector256<double>>(_array.AsSpan());
            var result = SplitLanesParallel.splitLanes(source, false, _destinationArray);
            return result;
        }

        [Benchmark]
        public double[][] SoontsParallel() {
            ReadOnlySpan<Vector256<double>> source = MemoryMarshal.Cast<Coordinate4D, Vector256<double>>(_array.AsSpan());
            var result = SplitLanesParallel.splitLanes(source, true, _destinationArray);
            return result;
        }

        // https://gist.github.com/Const-me/4482127ea797b865b7e2e77fcf429dd5
        static class SplitLanesParallel {
            /// <summary>Minimum problem size to bother with multithreading</summary>
            const int minParallel = 1024 * 4;

            /// <summary>Parallel implementation doesn't assume equal performance of cores,
            /// it implements dynamic scheduling. This number is a length of a single job.</summary>
            const uint parallellBatch = 1024;

            /// <summary>Count of CPU cores to use for the parallel implementation</summary>
            static readonly int countThreads = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

            /// <summary>Split lanes of vectors into 4 new arrays</summary>
            public static double[][] splitLanes(ReadOnlySpan<Vector256<double>> source, bool useParallel = false, double[][] destinationArray = null) {
                int length = source.Length;
                if (length <= 0)
                    throw new ArgumentException();

                double[][] result = destinationArray;
                if (null== result || result.Length<4 || result[0].Length< length || result[1].Length < length || result[2].Length < length || result[3].Length < length) {
                    result = new double[][] {
                        new double[length],
                        new double[length],
                        new double[length],
                        new double[length]
                    };
                }

                unsafe {
                    fixed (Vector256<double>* rsi = source)
                    fixed (double* x = result[0])
                    fixed (double* y = result[1])
                    fixed (double* z = result[2])
                    fixed (double* w = result[3]) {
                        if (length < minParallel || !useParallel)
                            transposeBatch(rsi, (uint)length, x, y, z, w);
                        else
                            transposeParallel(rsi, length, x, y, z, w);
                    }
                }
                return result;
            }

            /// <summary>Create AVX vector by loading 2 FP64 numbers from 2 pointers each</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static unsafe Vector256<double> load2(double* a, double* b) {
                Vector128<double> low = Vector128.Load(a);
                Vector256<double> result = Vector128.ToVector256Unsafe(low);
                Vector128<double> high = Vector128.Load(b);
                return Avx.InsertVector128(result, high, 1);
            }

            /// <summary>Load 16 numbers from source pointer, transpose, and store 4 rows</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static unsafe void transpose4x4(Vector256<double>* source,
                double* x, double* y, double* z, double* w) {
                double* rsi = (double*)source;
                Vector256<double> a, b;

                // x0, y0, x2, y2
                a = load2(rsi, rsi + 8);
                // x1, y1, x3, y3
                b = load2(rsi + 4, rsi + 12);

                Avx.UnpackLow(a, b).Store(x);
                Avx.UnpackHigh(a, b).Store(y);

                // z0, w0, z2, w2
                a = load2(rsi + 2, rsi + 10);
                // z1, w1, z3, w3
                b = load2(rsi + 6, rsi + 14);

                Avx.UnpackLow(a, b).Store(z);
                Avx.UnpackHigh(a, b).Store(w);
            }

            /// <summary>Load 4 numbers from the source pointer, and store 4 lanes</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static unsafe void transpose4x1(Vector256<double>* source,
                double* x, double* y, double* z, double* w) {
                double* rsi = (double*)source;
                Vector128<double> v;

                v = Vector128.Load(rsi);
                Sse2.StoreScalar(x, v);
                Sse2.StoreHigh(y, v);

                v = Vector128.Load(rsi + 2);
                Sse2.StoreScalar(z, v);
                Sse2.StoreHigh(w, v);
            }

            /// <summary>Transpose a batch of values</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static unsafe void transposeBatch(Vector256<double>* rsi, uint length,
                double* x, double* y, double* z, double* w) {
                const uint maskAlign4 = unchecked((uint)-4);
                Vector256<double>* rsiEndAligned = rsi + (length & maskAlign4);
                Vector256<double>* rsiEnd = rsi + length;

                // Handle majority of the data with AVX
                // Each iteration of this loop loads 4 vectors = 16 elements,
                // and stores a full vector into each output pointer
                for (; rsi < rsiEndAligned; rsi += 4, x += 4, y += 4, z += 4, w += 4)
                    transpose4x4(rsi, x, y, z, w);

                // Handle the remainder
                // Each iteration of this loop loads 4 elements,
                // and stores a single number into each output pointer
                for (; rsi < rsiEnd; rsi++, x++, y++, z++, w++)
                    transpose4x1(rsi, x, y, z, w);
            }

            /// <summary>Structure to marshal data across threads</summary>
            unsafe struct ParallelContext {
                public volatile uint offset;
                public volatile int runningThreads;

                public readonly Vector256<double>* rsi;
                public readonly double* x;
                public readonly double* y;
                public readonly double* z;
                public readonly double* w;
                public readonly uint length;

                public ParallelContext(Vector256<double>* rsi, int length,
                    double* x, double* y, double* z, double* w) {
                    offset = 0;
                    runningThreads = countThreads;

                    this.rsi = rsi;
                    this.x = x;
                    this.y = y;
                    this.z = z;
                    this.w = w;
                    this.length = (uint)length;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            static unsafe void threadProc(IntPtr contextPtr) {
                ref ParallelContext context = ref *(ParallelContext*)(contextPtr);

                // Context structure is concurrently modified by multiple threads, because volatile offset field
                // Copy pointers and length to local variables, should help with cache line sharing
                Vector256<double>* rsi = context.rsi;
                double* x = context.x;
                double* y = context.y;
                double* z = context.z;
                double* w = context.w;
                uint inputLength = context.length;

                // This loop implements dynamic scheduling without locks
                while (true) {
                    // Find the next batch to process;
                    // Interlocked.Add returns incremented value, we need the original one
                    uint off = Interlocked.Add(ref context.offset, parallellBatch) - parallellBatch;

                    // Check for completion
                    if (off < inputLength) {
                        // Processes the batch
                        uint batchLength = Math.Min(parallellBatch, inputLength - off);
                        transposeBatch(rsi + off, batchLength, x + off, y + off, z + off, w + off);
                    } else {
                        // Job is done, decrement count of running threads and return
                        Interlocked.Decrement(ref context.runningThreads);
                        return;
                    }
                }
            }

            // Cache delegate for the thread pool to save GC allocation
            static readonly Action<IntPtr> threadCallback = threadProc;

            [MethodImpl(MethodImplOptions.NoInlining)]
            static unsafe void transposeParallel(Vector256<double>* rsi, int length,
                double* x, double* y, double* z, double* w) {
                ParallelContext context = new ParallelContext(rsi, length, x, y, z, w);
                // Stack in .NET is already fixed in memory, no need for the fixed() statement
                IntPtr contextPtr = (IntPtr)(&context);

                // Launch ( countThreads - 1 ) background tasks
                for (int i = 1; i < countThreads; i++)
                    ThreadPool.UnsafeQueueUserWorkItem(threadCallback, contextPtr, preferLocal: false);
                // Run the same task on the current thread as well
                threadProc(contextPtr);

                // Busy wait for background tasks to complete
                while (context.runningThreads > 0) {
                    for (int i = 0; i < 0x100; i++) {
                        X86Base.Pause();
                        X86Base.Pause();
                        X86Base.Pause();
                        X86Base.Pause();
                    }
                }
            }
        }

#endif // NET7_0_OR_GREATER

    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    readonly struct Coordinate4D {
        [FieldOffset(0)]
        readonly double _x;
        [FieldOffset(8)]
        readonly double _y;
        [FieldOffset(16)]
        readonly double _z;
        [FieldOffset(24)]
        readonly double _w;

        public double X => _x;
        public double Y => _y;
        public double Z => _z;
        public double W => _w;

        public Coordinate4D(double x, double y, double z, double w) {
            _x = x;
            _y = y;
            _z = z;
            _w = w;
        }
    }
}
