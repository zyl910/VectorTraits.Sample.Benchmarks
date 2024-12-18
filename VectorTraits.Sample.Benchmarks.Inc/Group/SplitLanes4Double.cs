﻿//#undef BENCHMARKS_OFF

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
    /// Deinterleave the Double array and split it into arrays of X, Y, Z, and W (对Double数组进行解交织，拆分出 X,Y,Z,W的数组). How do I optimally fill multiple arrays with SIMDs vectors? https://stackoverflow.com/questions/77984612/how-do-i-optimally-fill-multiple-arrays-with-simds-vectors/
    /// </summary>
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
                    // UnzipParallel.
                    dst = UnzipParallel();
                    if (!CheckEquals(expected, dst)) writer.WriteLine("UnzipParallel results are not correct!");
                    // Soonts.
#if NET7_0_OR_GREATER
                    dst = Soonts();
                    if (!CheckEquals(expected, dst)) writer.WriteLine("Soonts results are not correct!");
                    dst = SoontsParallel();
                    if (!CheckEquals(expected, dst)) writer.WriteLine("SoontsParallel results are not correct!");
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
            var result = UnzipBatch(source, false, _destinationArray);
            return result;
        }

        [Benchmark]
        public double[][] UnzipParallel() {
            ReadOnlySpan<double> source = MemoryMarshal.Cast<Coordinate4D, double>(_array.AsSpan());
            var result = UnzipBatch(source, true, _destinationArray);
            return result;
        }

        public static double[][] UnzipBatch(ReadOnlySpan<double> source, bool useParallel = false, double[][] destinationArray = null) {
            const int parallelThreshold = 1024 * 4;
            const int minBatchSize = 1024;
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
                    int processorCount = Environment.ProcessorCount;
                    bool allowParallel = useParallel && (length > parallelThreshold) && (processorCount > 1);
                    if (allowParallel) {
                        //int batchSize = minBatchSize;
                        int batchSize = (length / processorCount / 2) & (-minBatchSize);
                        if (batchSize < minBatchSize) batchSize = minBatchSize;
                        int batchCount = (length + batchSize - 1) / batchSize; // ceil((double)length / batchSize)
                        //Console.WriteLine(string.Format("batchSize={0}, batchCount={1} // 0x{0:X}, 0x{1:X}", batchSize, batchCount));
                        double* p0 = p;
                        double* x0 = x, y0 = y, z0 = z, w0 = w;
                        Parallel.For(0, batchCount, i => {
                            int start = batchSize * i;
                            int len = batchSize;
                            if (start + len > length) len = length - start;
                            double* p2 = p0 + start * groupSize;
                            //UnzipBatch(p2, len, x + start, y + start, z + start, w + start); // Error CS1764	Cannot use fixed local 'p' inside an anonymous method, lambda expression, or query expression
                            UnzipBatch(p2, len, x0 + start, y0 + start, z0 + start, w0 + start);
                        });
                    } else {
                        UnzipBatch(p, length, x, y, z, w);
                    }
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
                if (!Avx2.IsSupported) throw new NotSupportedException("Not support X86's Avx2!");

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

// == Benchmarks result

// -- `.NET8.0` on Arm
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.0.1 (24A348) [Darwin 24.0.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 8.0.204
//   [Host]     : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD [AttachedDebugger]
//   DefaultJob : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD
// 
// | Method           | Count  | Mean         | Error       | StdDev      | Ratio | RatioSD |
// |----------------- |------- |-------------:|------------:|------------:|------:|--------:|
// | Linq             | 1000   |   7,302.9 ns |     8.86 ns |     8.29 ns |  0.63 |    0.00 |
// | ParallelFor      | 1000   |  11,522.6 ns |    75.72 ns |    70.83 ns |  1.00 |    0.01 |
// | ParallelForNoNew | 1000   |  10,134.9 ns |    39.63 ns |    33.09 ns |  0.88 |    0.01 |
// | Unzip            | 1000   |     657.7 ns |     1.20 ns |     1.12 ns |  0.06 |    0.00 |
// | UnzipParallel    | 1000   |     650.9 ns |     1.65 ns |     1.54 ns |  0.06 |    0.00 |
// | Soonts           | 1000   |           NA |          NA |          NA |     ? |       ? |
// | SoontsParallel   | 1000   |           NA |          NA |          NA |     ? |       ? |
// |                  |        |              |             |             |       |         |
// | Linq             | 10000  |  70,962.2 ns |    56.27 ns |    52.63 ns |  2.04 |    0.02 |
// | ParallelFor      | 10000  |  34,750.8 ns |   301.04 ns |   281.59 ns |  1.00 |    0.01 |
// | ParallelForNoNew | 10000  |  25,168.9 ns |   150.15 ns |   140.45 ns |  0.72 |    0.01 |
// | Unzip            | 10000  |   8,860.4 ns |    16.40 ns |    15.34 ns |  0.25 |    0.00 |
// | UnzipParallel    | 10000  |   9,340.8 ns |    49.81 ns |    46.59 ns |  0.27 |    0.00 |
// | Soonts           | 10000  |           NA |          NA |          NA |     ? |       ? |
// | SoontsParallel   | 10000  |           NA |          NA |          NA |     ? |       ? |
// |                  |        |              |             |             |       |         |
// | Linq             | 100000 | 871,966.5 ns | 7,968.57 ns | 7,453.80 ns |  3.29 |    0.04 |
// | ParallelFor      | 100000 | 265,150.8 ns | 3,256.47 ns | 2,886.78 ns |  1.00 |    0.01 |
// | ParallelForNoNew | 100000 | 152,403.4 ns | 1,719.17 ns | 1,524.00 ns |  0.57 |    0.01 |
// | Unzip            | 100000 |  89,025.7 ns |   101.08 ns |    94.55 ns |  0.34 |    0.00 |
// | UnzipParallel    | 100000 |  42,555.0 ns |   303.98 ns |   284.34 ns |  0.16 |    0.00 |
// | Soonts           | 100000 |           NA |          NA |          NA |     ? |       ? |
// | SoontsParallel   | 100000 |           NA |          NA |          NA |     ? |       ? |

// -- `.NET8.0` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4460/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 8.0.403
//   [Host]     : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
//   DefaultJob : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
// 
// | Method           | Count  | Mean           | Error        | StdDev       | Median         | Ratio | RatioSD | Code Size |
// |----------------- |------- |---------------:|-------------:|-------------:|---------------:|------:|--------:|----------:|
// | Linq             | 1000   |     7,802.5 ns |    151.61 ns |    174.59 ns |     7,808.9 ns |  0.87 |    0.02 |   3,381 B |
// | ParallelFor      | 1000   |     9,010.2 ns |    134.49 ns |    125.80 ns |     8,968.6 ns |  1.00 |    0.02 |        NA |
// | ParallelForNoNew | 1000   |     7,863.9 ns |     28.89 ns |     27.03 ns |     7,861.1 ns |  0.87 |    0.01 |        NA |
// | Unzip            | 1000   |       498.9 ns |      5.01 ns |      4.69 ns |       497.7 ns |  0.06 |    0.00 |        NA |
// | UnzipParallel    | 1000   |       496.9 ns |      5.28 ns |      4.94 ns |       496.7 ns |  0.06 |    0.00 |        NA |
// | Soonts           | 1000   |       440.9 ns |      1.96 ns |      1.74 ns |       441.1 ns |  0.05 |    0.00 |        NA |
// | SoontsParallel   | 1000   |       440.6 ns |      2.34 ns |      2.19 ns |       440.9 ns |  0.05 |    0.00 |        NA |
// |                  |        |                |              |              |                |       |         |           |
// | Linq             | 10000  |    72,536.3 ns |    795.69 ns |    705.36 ns |    72,661.0 ns |  3.22 |    0.06 |   3,381 B |
// | ParallelFor      | 10000  |    22,551.1 ns |    387.31 ns |    362.29 ns |    22,541.3 ns |  1.00 |    0.02 |        NA |
// | ParallelForNoNew | 10000  |    13,441.4 ns |     61.79 ns |     57.80 ns |    13,441.4 ns |  0.60 |    0.01 |        NA |
// | Unzip            | 10000  |     4,771.7 ns |     31.01 ns |     29.00 ns |     4,778.2 ns |  0.21 |    0.00 |        NA |
// | UnzipParallel    | 10000  |     4,937.8 ns |     29.44 ns |     26.10 ns |     4,942.1 ns |  0.22 |    0.00 |        NA |
// | Soonts           | 10000  |     4,426.8 ns |     43.07 ns |     40.29 ns |     4,429.2 ns |  0.20 |    0.00 |        NA |
// | SoontsParallel   | 10000  |    15,678.5 ns |    194.98 ns |    182.38 ns |    15,618.7 ns |  0.70 |    0.01 |        NA |
// |                  |        |                |              |              |                |       |         |           |
// | Linq             | 100000 | 1,325,172.3 ns | 26,322.40 ns | 49,439.85 ns | 1,323,487.3 ns |  3.18 |    0.72 |   3,343 B |
// | ParallelFor      | 100000 |   437,942.7 ns | 33,489.64 ns | 98,744.95 ns |   411,440.4 ns |  1.05 |    0.34 |        NA |
// | ParallelForNoNew | 100000 |    58,496.1 ns |    922.52 ns |    817.79 ns |    58,215.5 ns |  0.14 |    0.03 |        NA |
// | Unzip            | 100000 |    57,467.8 ns |    802.26 ns |    711.18 ns |    57,497.7 ns |  0.14 |    0.03 |        NA |
// | UnzipParallel    | 100000 |    18,033.7 ns |    351.31 ns |    375.90 ns |    18,099.2 ns |  0.04 |    0.01 |        NA |
// | Soonts           | 100000 |    61,783.5 ns |  1,197.89 ns |  1,426.00 ns |    61,583.4 ns |  0.15 |    0.03 |        NA |
// | SoontsParallel   | 100000 |    29,202.8 ns |    249.69 ns |    233.56 ns |    29,294.6 ns |  0.07 |    0.02 |        NA |

// -- `.NET Framework` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4460/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
//   [Host]     : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
//   DefaultJob : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
// 
// | Method           | Count  | Mean         | Error       | StdDev      | Median       | Ratio | RatioSD | Code Size |
// |----------------- |------- |-------------:|------------:|------------:|-------------:|------:|--------:|----------:|
// | Linq             | 1000   |    37.033 us |   0.5153 us |   0.4303 us |    37.097 us |  3.61 |    0.06 |   2,258 B |
// | ParallelFor      | 1000   |    10.249 us |   0.1346 us |   0.1193 us |    10.211 us |  1.00 |    0.02 |   8,883 B |
// | ParallelForNoNew | 1000   |     9.619 us |   0.1899 us |   0.2260 us |     9.642 us |  0.94 |    0.02 |   8,732 B |
// | Unzip            | 1000   |     8.139 us |   0.1492 us |   0.1395 us |     8.098 us |  0.79 |    0.02 |   5,096 B |
// | UnzipParallel    | 1000   |     8.290 us |   0.1166 us |   0.1091 us |     8.257 us |  0.81 |    0.01 |   5,099 B |
// |                  |        |              |             |             |              |       |         |           |
// | Linq             | 10000  |   440.397 us |   5.8768 us |   5.2096 us |   438.828 us | 14.12 |    0.31 |   2,258 B |
// | ParallelFor      | 10000  |    31.209 us |   0.5914 us |   0.6073 us |    30.966 us |  1.00 |    0.03 |   8,883 B |
// | ParallelForNoNew | 10000  |    20.223 us |   0.1624 us |   0.1440 us |    20.237 us |  0.65 |    0.01 |   8,732 B |
// | Unzip            | 10000  |    76.318 us |   0.7444 us |   0.6963 us |    76.385 us |  2.45 |    0.05 |   5,214 B |
// | UnzipParallel    | 10000  |    36.299 us |   0.3027 us |   0.2364 us |    36.355 us |  1.16 |    0.02 |   5,217 B |
// |                  |        |              |             |             |              |       |         |           |
// | Linq             | 100000 | 5,474.105 us | 108.7900 us | 175.6756 us | 5,488.038 us | 14.49 |    1.39 |   2,258 B |
// | ParallelFor      | 100000 |   381.343 us |  13.9218 us |  38.5773 us |   367.393 us |  1.01 |    0.14 |   8,883 B |
// | ParallelForNoNew | 100000 |    83.610 us |   1.2179 us |   1.1392 us |    83.295 us |  0.22 |    0.02 |   8,732 B |
// | Unzip            | 100000 |   947.284 us |   8.7045 us |   8.1422 us |   949.734 us |  2.51 |    0.23 |   5,214 B |
// | UnzipParallel    | 100000 |   253.043 us |   4.6891 us |   4.8153 us |   253.532 us |  0.67 |    0.06 |   5,217 B |
