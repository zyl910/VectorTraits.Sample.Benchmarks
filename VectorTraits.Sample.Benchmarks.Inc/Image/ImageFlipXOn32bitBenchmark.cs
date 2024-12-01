#undef BENCHMARKS_OFF

using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
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

namespace Zyl.VectorTraits.Sample.Benchmarks.Image {
#if BENCHMARKS_OFF
    using BenchmarkAttribute = FakeBenchmarkAttribute;
#else
#endif // BENCHMARKS_OFF

    /// <summary>
    /// Flip the image vertically(FlipY) (对图像进行垂直翻转(FlipY)).
    /// </summary>
    public class ImageFlipXOn32bitBenchmark : IDisposable {
        private bool _disposed = false;
        private static readonly Random _random = new Random(1);
        private BitmapData _sourceBitmapData = null;
        private BitmapData _destinationBitmapData = null;
        private BitmapData _expectedBitmapData = null;

        [Params(1024, 2048, 4096)]
        public int Width { get; set; }
        public int Height { get; set; }

        private static readonly Vector<int> _shuffleIndices;

        static ImageFlipXOn32bitBenchmark() {
            bool AllowCreateByDoubleLoop = true;
            if (AllowCreateByDoubleLoop) {
                _shuffleIndices = Vectors.CreateByDoubleLoop<int>(Vector<int>.Count - 1, -1);
            } else {
                Span<int> buf = stackalloc int[Vector<int>.Count];
                for (int i = 0;i< Vector<int>.Count; i++) {
                    buf[i] = Vector<int>.Count - 1 - i;
                }
                _shuffleIndices = Vectors.Create(buf);
            }
        }

        ~ImageFlipXOn32bitBenchmark() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing) {
            if (_disposed) return;
            _disposed = true;
            if (disposing) {
                Cleanup();
            }
        }

        private BitmapData AllocBitmapData(int width, int height, PixelFormat format) {
            const int strideAlign = 4;
            if (width <= 0) throw new ArgumentOutOfRangeException($"The width({width}) need > 0!");
            if (height <= 0) throw new ArgumentOutOfRangeException($"The width({height}) need > 0!");
            int stride = 0;
            switch (format) {
                case PixelFormat.Format8bppIndexed:
                    stride = width * 1;
                    break;
                case PixelFormat.Format24bppRgb:
                    stride = width * 3;
                    break;
                case PixelFormat.Format32bppRgb:
                    stride = width * 4;
                    break;
            }
            if (stride <= 0) throw new ArgumentOutOfRangeException($"Invalid pixel format({format})!");
            if (0 != (stride % strideAlign)) {
                stride = stride - (stride % strideAlign) + strideAlign;
            }
            BitmapData bitmapData = new BitmapData();
            bitmapData.Width = width;
            bitmapData.Height = height;
            bitmapData.PixelFormat = format;
            bitmapData.Stride = stride;
            bitmapData.Scan0 = Marshal.AllocHGlobal(stride * height);
            return bitmapData;
        }

        private void FreeBitmapData(BitmapData bitmapData) {
            if (null == bitmapData) return;
            if (IntPtr.Zero == bitmapData.Scan0) return;
            Marshal.FreeHGlobal(bitmapData.Scan0);
            bitmapData.Scan0 = IntPtr.Zero;
        }

        [GlobalCleanup]
        public void Cleanup() {
            FreeBitmapData(_sourceBitmapData); _sourceBitmapData = null;
            FreeBitmapData(_destinationBitmapData); _destinationBitmapData = null;
            FreeBitmapData(_expectedBitmapData); _expectedBitmapData = null;
        }

        [GlobalSetup]
        public void Setup() {
            Height = Width;
            // Create.
            Cleanup();
            _sourceBitmapData = AllocBitmapData(Width, Height, PixelFormat.Format32bppRgb);
            _destinationBitmapData = AllocBitmapData(Width, Height, PixelFormat.Format32bppRgb);
            _expectedBitmapData = AllocBitmapData(Width, Height, PixelFormat.Format32bppRgb);
            RandomFillBitmapData(_sourceBitmapData, _random);

            // Check.
            bool allowCheck = true;
            if (allowCheck) {
                try {
                    TextWriter writer = Console.Out;
                    long totalDifference, countByteDifference;
                    int maxDifference;
                    double averageDifference;
                    long totalByte = Width * Height * 4;
                    double percentDifference;
                    writer.WriteLine(string.Format("YShuffleKernel_AcceleratedTypes:\t{0}", Vectors.YShuffleKernel_AcceleratedTypes));
                    // Baseline
                    ScalarDo(_sourceBitmapData, _expectedBitmapData);
                    // Scalar
                    ScalarDo(_expectedBitmapData, _destinationBitmapData);
                    totalDifference = SumDifference(_sourceBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of Scalar: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // ScalarParallel
                    ScalarParallel();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of ScalarParallel: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // UseVectors
                    UseVectors();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectors: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // UseVectorsParallel
                    UseVectorsParallel();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectorsParallel: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // UseVectorsArgs
                    UseVectorsArgs();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectorsArgs: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // UseVectorsArgsParallel
                    UseVectorsArgsParallel();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectorsArgsParallel: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                } catch (Exception ex) {
                    Debug.WriteLine(ex.ToString());
                }
            }
            // Debug break.
            //bool allowDebugBreak = false;
            //if (allowDebugBreak) {
            //    for (int i = 0; i < 10000; ++i) {
            //        UseVectors();
            //    }
            //    Debugger.Break();
            //    UseVectors();
            //}
        }

        internal unsafe void RandomFillBitmapData(BitmapData bitmapData, Random random) {
            if (null == bitmapData) return;
            if (IntPtr.Zero == bitmapData.Scan0) return;
            byte* pRow = (byte*)bitmapData.Scan0;
            for (int i = 0; i < bitmapData.Height; i++) {
                byte* p = pRow;
                for (int j = 0; j < bitmapData.Stride; j++) {
                    *p++ = (byte)random.Next(0x100);
                }
                pRow += bitmapData.Stride;
            }
        }

        private unsafe long SumDifference(BitmapData expected, BitmapData dst, out long countByteDifference, out int maxDifference) {
            const int cbPixel = 4; // 32 bit: Bgr32, Bgra32, Rgb32, Rgba32.
            long totalDifference = 0;
            countByteDifference = 0;
            maxDifference = 0;
            int maxPosX = -1, maxPosY = -1;
            int width = expected.Width;
            int height = expected.Height;
            int strideSrc = expected.Stride;
            int strideDst = dst.Stride;
            byte* pRow = (byte*)expected.Scan0.ToPointer();
            byte* qRow = (byte*)dst.Scan0.ToPointer();
            for (int i = 0; i < height; i++) {
                byte* p = pRow;
                byte* q = qRow;
                for (int j = 0; j < width; j++) {
                    for (int k = 0; k < cbPixel; ++k) {
                        int difference = Math.Abs((int)(*q) - *p);
                        if (0 != difference) {
                            totalDifference += difference;
                            ++countByteDifference;
                            if (maxDifference < difference) {
                                maxDifference = difference;
                                maxPosX = j;
                                maxPosY = i;
                            }
                        }
                        ++p;
                        ++q;
                    }
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
            if (maxDifference > 0) {
                //Console.WriteLine(string.Format("SumDifference maxDifference={0}, at ({1}, {2})", maxDifference, maxPosX, maxPosY));
            }
            return totalDifference;
        }

        [Benchmark(Baseline = true)]
        public void Scalar() {
            ScalarDo(_sourceBitmapData, _destinationBitmapData, false);
        }

        //[Benchmark]
        public void ScalarParallel() {
            ScalarDo(_sourceBitmapData, _destinationBitmapData, true);
        }

        public static unsafe void ScalarDo(BitmapData src, BitmapData dst, bool useParallel = false) {
            int width = src.Width;
            int height = src.Height;
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            bool allowParallel = useParallel && (height > 16) && (Environment.ProcessorCount > 1);
            if (allowParallel) {
                Parallel.For(0, height, i => {
                    int start = i;
                    int len = 1;
                    byte* pSrc2 = pSrc + start * (long)strideSrc;
                    byte* pDst2 = pDst + start * (long)strideDst;
                    ScalarDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                ScalarDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void ScalarDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            const int cbPixel = 4; // 32 bit: Bgr32, Bgra32, Rgb32, Rgba32.
            byte* pRow = pSrc;
            byte* qRow = pDst;
            for (int i = 0; i < height; i++) {
                byte* p = pRow + (width - 1) * cbPixel;
                byte* q = qRow;
                for (int j = 0; j < width; j++) {
                    for (int k = 0; k < cbPixel; k++) {
                        q[k] = p[k];
                    }
                    p -= cbPixel;
                    q += cbPixel;
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

        [Benchmark]
        public void UseVectors() {
            UseVectorsDo(_sourceBitmapData, _destinationBitmapData, false);
        }

        //[Benchmark]
        public void UseVectorsParallel() {
            UseVectorsDo(_sourceBitmapData, _destinationBitmapData, true);
        }

        public static unsafe void UseVectorsDo(BitmapData src, BitmapData dst, bool useParallel = false) {
            int vectorWidth = Vector<byte>.Count;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth) {
                ScalarDo(src, dst, useParallel);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            bool allowParallel = useParallel && (height > 16) && (Environment.ProcessorCount > 1);
            if (allowParallel) {
                Parallel.For(0, height, i => {
                    int start = i;
                    int len = 1;
                    byte* pSrc2 = pSrc + start * (long)strideSrc;
                    byte* pDst2 = pDst + start * (long)strideDst;
                    UseVectorsDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                UseVectorsDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void UseVectorsDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            const int cbPixel = 4; // 32 bit: Bgr32, Bgra32, Rgb32, Rgba32.
            Vector<int> indices = _shuffleIndices;
            int vectorWidth = Vector<int>.Count;
            int maxX = width - vectorWidth;
            byte* pRow = pSrc;
            byte* qRow = pDst;
            for (int i = 0; i < height; i++) {
                Vector<int>* pLast = (Vector<int>*)pRow;
                Vector<int>* qLast = (Vector<int>*)(qRow + maxX * cbPixel);
                Vector<int>* p = (Vector<int>*)(pRow + maxX * cbPixel);
                Vector<int>* q = (Vector<int>*)qRow;
                for (; ; ) {
                    Vector<int> data, temp;
                    // Load.
                    data = *p;
                    // FlipX.
                    //temp = Vectors.Shuffle(data, indices);
                    temp = Vectors.YShuffleKernel(data, indices);
                    // Store.
                    *q = temp;
                    // Next.
                    if (p <= pLast) break;
                    --p;
                    ++q;
                    if (p < pLast) p = pLast; // The last block is also use vector.
                    if (q > qLast) q = qLast;
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

        [Benchmark]
        public void UseVectorsArgs() {
            UseVectorsArgsDo(_sourceBitmapData, _destinationBitmapData, false);
        }

        //[Benchmark]
        public void UseVectorsArgsParallel() {
            UseVectorsArgsDo(_sourceBitmapData, _destinationBitmapData, true);
        }

        public static unsafe void UseVectorsArgsDo(BitmapData src, BitmapData dst, bool useParallel = false) {
            int vectorWidth = Vector<byte>.Count;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth) {
                ScalarDo(src, dst, useParallel);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            bool allowParallel = useParallel && (height > 16) && (Environment.ProcessorCount > 1);
            if (allowParallel) {
                Parallel.For(0, height, i => {
                    int start = i;
                    int len = 1;
                    byte* pSrc2 = pSrc + start * (long)strideSrc;
                    byte* pDst2 = pDst + start * (long)strideDst;
                    UseVectorsArgsDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                UseVectorsArgsDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void UseVectorsArgsDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            const int cbPixel = 4; // 32 bit: Bgr32, Bgra32, Rgb32, Rgba32.
            Vector<int> indices = _shuffleIndices;
            Vector<int> args0, args1;
            Vectors.YShuffleKernel_Args(indices, out args0, out args1);
            int vectorWidth = Vector<int>.Count;
            int maxX = width - vectorWidth;
            byte* pRow = pSrc;
            byte* qRow = pDst;
            for (int i = 0; i < height; i++) {
                Vector<int>* pLast = (Vector<int>*)pRow;
                Vector<int>* qLast = (Vector<int>*)(qRow + maxX * cbPixel);
                Vector<int>* p = (Vector<int>*)(pRow + maxX * cbPixel);
                Vector<int>* q = (Vector<int>*)qRow;
                for (; ; ) {
                    Vector<int> data, temp;
                    // Load.
                    data = *p;
                    // FlipX.
                    //temp = Vectors.YShuffleKernel(data, indices);
                    temp = Vectors.YShuffleKernel_Core(data, args0, args1);
                    // Store.
                    *q = temp;
                    // Next.
                    if (p <= pLast) break;
                    --p;
                    ++q;
                    if (p < pLast) p = pLast; // The last block is also use vector.
                    if (q > qLast) q = qLast;
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

    }

}

// == Benchmarks result

// -- `.NET8.0` on Arm
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.1.1 (24B91) [Darwin 24.1.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 8.0.204
//   [Host]     : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD [AttachedDebugger]
//   DefaultJob : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD
// 
// 
// | Method         | Width | Mean        | Error    | StdDev   | Ratio |
// |--------------- |------ |------------:|---------:|---------:|------:|
// | Scalar         | 1024  |    625.8 us |  0.81 us |  0.68 us |  1.00 |
// | UseVectors     | 1024  |    151.9 us |  0.32 us |  0.27 us |  0.24 |
// | UseVectorsArgs | 1024  |    151.2 us |  0.13 us |  0.12 us |  0.24 |
// |                |       |             |          |          |       |
// | Scalar         | 2048  |  2,522.4 us |  1.28 us |  1.14 us |  1.00 |
// | UseVectors     | 2048  |    666.9 us |  0.55 us |  0.51 us |  0.26 |
// | UseVectorsArgs | 2048  |    663.8 us |  0.80 us |  0.67 us |  0.26 |
// |                |       |             |          |          |       |
// | Scalar         | 4096  | 10,797.2 us | 11.21 us | 10.48 us |  1.00 |
// | UseVectors     | 4096  |  3,349.0 us | 39.67 us | 37.11 us |  0.31 |
// | UseVectorsArgs | 4096  |  3,339.6 us | 20.76 us | 16.21 us |  0.31 |

// -- `.NET6.0` on Arm
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.1.1 (24B91) [Darwin 24.1.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 8.0.204
//   [Host]     : .NET 6.0.33 (6.0.3324.36610), Arm64 RyuJIT AdvSIMD [AttachedDebugger]
//   DefaultJob : .NET 6.0.33 (6.0.3324.36610), Arm64 RyuJIT AdvSIMD
// 
// 
// | Method         | Width | Mean        | Error    | StdDev   | Ratio |
// |--------------- |------ |------------:|---------:|---------:|------:|
// | Scalar         | 1024  |  1,805.2 us |  0.72 us |  0.60 us |  1.00 |
// | UseVectors     | 1024  |    454.5 us |  5.45 us |  5.10 us |  0.25 |
// | UseVectorsArgs | 1024  |    158.4 us |  0.05 us |  0.04 us |  0.09 |
// |                |       |             |          |          |       |
// | Scalar         | 2048  |  7,229.0 us |  2.88 us |  2.69 us |  1.00 |
// | UseVectors     | 2048  |  1,857.4 us |  2.73 us |  2.56 us |  0.26 |
// | UseVectorsArgs | 2048  |    656.2 us |  0.26 us |  0.23 us |  0.09 |
// |                |       |             |          |          |       |
// | Scalar         | 4096  | 29,574.1 us | 13.21 us | 11.03 us |  1.00 |
// | UseVectors     | 4096  |  8,117.2 us | 28.06 us | 26.25 us |  0.27 |
// | UseVectorsArgs | 4096  |  4,671.7 us |  2.50 us |  2.21 us |  0.16 |

// -- `.NET8.0` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4541/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 8.0.403
//   [Host]     : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
//   DefaultJob : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
// 
// 
// | Method         | Width | Mean        | Error     | StdDev    | Ratio | RatioSD |
// |--------------- |------ |------------:|----------:|----------:|------:|--------:|
// | Scalar         | 1024  |    784.7 us |  14.56 us |  14.30 us |  1.00 |    0.03 |
// | UseVectors     | 1024  |    106.4 us |   2.12 us |   4.96 us |  0.14 |    0.01 |
// | UseVectorsArgs | 1024  |    101.4 us |   2.03 us |   3.85 us |  0.13 |    0.01 |
// |                |       |             |           |           |       |         |
// | Scalar         | 2048  |  3,453.5 us |  25.88 us |  22.94 us |  1.00 |    0.01 |
// | UseVectors     | 2048  |  1,520.8 us |  15.11 us |  14.13 us |  0.44 |    0.00 |
// | UseVectorsArgs | 2048  |  1,412.9 us |  27.96 us |  47.48 us |  0.41 |    0.01 |
// |                |       |             |           |           |       |         |
// | Scalar         | 4096  | 12,932.8 us | 177.40 us | 165.94 us |  1.00 |    0.02 |
// | UseVectors     | 4096  |  6,113.0 us |  43.35 us |  40.55 us |  0.47 |    0.01 |
// | UseVectorsArgs | 4096  |  6,270.9 us |  56.80 us |  50.35 us |  0.48 |    0.01 |

// -- `.NET Framework` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4541/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
//   [Host]     : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
//   DefaultJob : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
// 
// 
// | Method         | Width | Mean        | Error     | StdDev    | Ratio | RatioSD | Code Size |
// |--------------- |------ |------------:|----------:|----------:|------:|--------:|----------:|
// | Scalar         | 1024  |  1,315.2 us |  26.06 us |  25.59 us |  1.00 |    0.03 |   2,718 B |
// | UseVectors     | 1024  |    968.2 us |  17.55 us |  16.42 us |  0.74 |    0.02 |   3,507 B |
// | UseVectorsArgs | 1024  |    887.0 us |   9.91 us |   8.78 us |  0.67 |    0.01 |   3,507 B |
// |                |       |             |           |           |       |         |           |
// | Scalar         | 2048  |  5,259.4 us |  85.87 us |  80.32 us |  1.00 |    0.02 |   2,718 B |
// | UseVectors     | 2048  |  3,696.0 us |  29.64 us |  27.72 us |  0.70 |    0.01 |   3,507 B |
// | UseVectorsArgs | 2048  |  3,722.9 us |  39.36 us |  34.90 us |  0.71 |    0.01 |   3,507 B |
// |                |       |             |           |           |       |         |           |
// | Scalar         | 4096  | 19,763.1 us | 300.29 us | 266.20 us |  1.00 |    0.02 |   2,718 B |
// | UseVectors     | 4096  | 14,303.8 us |  62.36 us |  55.28 us |  0.72 |    0.01 |   3,507 B |
// | UseVectorsArgs | 4096  | 14,988.7 us | 286.49 us | 281.37 us |  0.76 |    0.02 |   3,507 B |
