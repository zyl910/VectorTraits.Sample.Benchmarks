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
    public class ImageFlipYBenchmark : IDisposable {
        private bool _disposed = false;
        private static readonly Random _random = new Random(1);
        private BitmapData _sourceBitmapData = null;
        private BitmapData _destinationBitmapData = null;
        private BitmapData _expectedBitmapData = null;

        [Params(1024, 2048, 4096)]
        public int Width { get; set; }
        public int Height { get; set; }

        ~ImageFlipYBenchmark() {
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
            _sourceBitmapData = AllocBitmapData(Width, Height, PixelFormat.Format24bppRgb);
            _destinationBitmapData = AllocBitmapData(Width, Height, PixelFormat.Format24bppRgb);
            _expectedBitmapData = AllocBitmapData(Width, Height, PixelFormat.Format24bppRgb);
            RandomFillBitmapData(_sourceBitmapData, _random);

            // Check.
            bool allowCheck = true;
            if (allowCheck) {
                try {
                    TextWriter writer = Console.Out;
                    long totalDifference, countByteDifference;
                    int maxDifference;
                    double averageDifference;
                    long totalByte = Width * Height * 3;
                    double percentDifference;
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
                    // UseCopy
                    UseCopy();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseCopy: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // UseCopyParallel
                    UseCopyParallel();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseCopyParallel: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
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
            const int cbPixel = 3; // Bgr24.
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

        [Benchmark]
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
                    byte* pDst2 = pDst + (height - 1 - start) * (long)strideDst;
                    ScalarDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                ScalarDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void ScalarDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            int strideCommon = Math.Min(Math.Abs(strideSrc), Math.Abs(strideDst));
            byte* pRow = pSrc;
            byte* qRow = pDst + (height - 1) * (long)strideDst; // Set to last row.
            for (int i = 0; i < height; i++) {
                byte* p = pRow;
                byte* q = qRow;
                for (int j = 0; j < strideCommon; j++) {
                    *q++ = *p++;
                }
                pRow += strideSrc;
                qRow -= strideDst;
            }
        }

        [Benchmark]
        public void UseVectors() {
            UseVectorsDo(_sourceBitmapData, _destinationBitmapData, false);
        }

        [Benchmark]
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
                    byte* pDst2 = pDst + (height - 1 - start) * (long)strideDst;
                    UseVectorsDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                UseVectorsDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void UseVectorsDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            int strideCommon = Math.Min(Math.Abs(strideSrc), Math.Abs(strideDst));
            int vectorWidth = Vector<byte>.Count;
            int maxX = strideCommon - vectorWidth;
            byte* pRow = pSrc;
            byte* qRow = pDst + (height - 1) * (long)strideDst; // Set to last row.
            for (int i = 0; i < height; i++) {
                Vector<byte>* pLast = (Vector<byte>*)(pRow + maxX);
                Vector<byte>* qLast = (Vector<byte>*)(qRow + maxX);
                Vector<byte>* p = (Vector<byte>*)pRow;
                Vector<byte>* q = (Vector<byte>*)qRow;
                for (; ; ) {
                    Vector<byte> data;
                    // Load.
                    data = *p;
                    // Store.
                    *q = data;
                    // Next.
                    if (p >= pLast) break;
                    ++p;
                    ++q;
                    if (p > pLast) p = pLast; // The last block is also use vector.
                    if (q > qLast) q = qLast;
                }
                pRow += strideSrc;
                qRow -= strideDst;
            }
        }

        [Benchmark]
        public void UseCopy() {
            UseCopyDo(_sourceBitmapData, _destinationBitmapData, false);
        }

        [Benchmark]
        public void UseCopyParallel() {
            UseCopyDo(_sourceBitmapData, _destinationBitmapData, true);
        }

        public static unsafe void UseCopyDo(BitmapData src, BitmapData dst, bool useParallel = false) {
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
                    byte* pDst2 = pDst + (height - 1 - start) * (long)strideDst;
                    UseCopyDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                UseCopyDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void UseCopyDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            int strideCommon = Math.Min(Math.Abs(strideSrc), Math.Abs(strideDst));
            byte* pRow = pSrc;
            byte* qRow = pDst + (height - 1) * (long)strideDst; // Set to last row.
            for (int i = 0; i < height; i++) {
                Buffer.MemoryCopy(pRow, qRow, strideCommon, strideCommon);
                pRow += strideSrc;
                qRow -= strideDst;
            }
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
// 
// | Method             | Width | Mean         | Error      | StdDev    | Ratio | RatioSD |
// |------------------- |------ |-------------:|-----------:|----------:|------:|--------:|
// | Scalar             | 1024  |  1,227.25 us |   0.694 us |  0.649 us |  1.00 |    0.00 |
// | ScalarParallel     | 1024  |    261.38 us |   0.739 us |  0.617 us |  0.21 |    0.00 |
// | UseVectors         | 1024  |    117.96 us |   0.105 us |  0.098 us |  0.10 |    0.00 |
// | UseVectorsParallel | 1024  |     39.46 us |   0.297 us |  0.263 us |  0.03 |    0.00 |
// | UseCopy            | 1024  |     92.95 us |   0.081 us |  0.063 us |  0.08 |    0.00 |
// | UseCopyParallel    | 1024  |     34.90 us |   0.170 us |  0.159 us |  0.03 |    0.00 |
// |                    |       |              |            |           |       |         |
// | Scalar             | 2048  |  5,236.47 us |  69.941 us | 62.001 us |  1.00 |    0.02 |
// | ScalarParallel     | 2048  |    952.35 us |   3.270 us |  3.059 us |  0.18 |    0.00 |
// | UseVectors         | 2048  |    700.91 us |   4.339 us |  4.058 us |  0.13 |    0.00 |
// | UseVectorsParallel | 2048  |    254.35 us |   1.183 us |  1.107 us |  0.05 |    0.00 |
// | UseCopy            | 2048  |    757.75 us |  14.775 us | 25.485 us |  0.14 |    0.01 |
// | UseCopyParallel    | 2048  |    252.87 us |   1.078 us |  1.009 us |  0.05 |    0.00 |
// |                    |       |              |            |           |       |         |
// | Scalar             | 4096  | 20,257.16 us | 100.815 us | 84.185 us |  1.00 |    0.01 |
// | ScalarParallel     | 4096  |  3,728.60 us |  12.672 us | 11.233 us |  0.18 |    0.00 |
// | UseVectors         | 4096  |  2,788.68 us |   2.712 us |  2.404 us |  0.14 |    0.00 |
// | UseVectorsParallel | 4096  |  1,776.71 us |   1.510 us |  1.412 us |  0.09 |    0.00 |
// | UseCopy            | 4096  |  2,448.65 us |   4.232 us |  3.959 us |  0.12 |    0.00 |
// | UseCopyParallel    | 4096  |  1,796.17 us |   5.197 us |  4.861 us |  0.09 |    0.00 |

// -- `.NET8.0` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4541/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 8.0.403
//   [Host]     : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
//   DefaultJob : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
// 
// 
// | Method             | Width | Mean         | Error      | StdDev     | Ratio | RatioSD |
// |------------------- |------ |-------------:|-----------:|-----------:|------:|--------:|
// | Scalar             | 1024  |  1,077.72 us |  20.704 us |  24.647 us |  1.00 |    0.03 |
// | ScalarParallel     | 1024  |    177.58 us |   3.489 us |   3.263 us |  0.16 |    0.00 |
// | UseVectors         | 1024  |     79.40 us |   1.549 us |   2.713 us |  0.07 |    0.00 |
// | UseVectorsParallel | 1024  |     19.54 us |   0.373 us |   0.547 us |  0.02 |    0.00 |
// | UseCopy            | 1024  |     81.88 us |   1.608 us |   2.034 us |  0.08 |    0.00 |
// | UseCopyParallel    | 1024  |     18.28 us |   0.357 us |   0.351 us |  0.02 |    0.00 |
// |                    |       |              |            |            |       |         |
// | Scalar             | 2048  |  4,360.82 us |  52.264 us |  48.888 us |  1.00 |    0.02 |
// | ScalarParallel     | 2048  |    717.40 us |  13.745 us |  13.499 us |  0.16 |    0.00 |
// | UseVectors         | 2048  |    992.42 us |  19.805 us |  57.457 us |  0.23 |    0.01 |
// | UseVectorsParallel | 2048  |    409.04 us |   8.070 us |  19.022 us |  0.09 |    0.00 |
// | UseCopy            | 2048  |  1,002.18 us |  19.600 us |  27.476 us |  0.23 |    0.01 |
// | UseCopyParallel    | 2048  |    418.30 us |   6.980 us |   5.449 us |  0.10 |    0.00 |
// |                    |       |              |            |            |       |         |
// | Scalar             | 4096  | 16,913.07 us | 244.574 us | 216.808 us |  1.00 |    0.02 |
// | ScalarParallel     | 4096  |  3,844.09 us |  46.626 us |  43.614 us |  0.23 |    0.00 |
// | UseVectors         | 4096  |  4,419.30 us |  84.049 us |  78.620 us |  0.26 |    0.01 |
// | UseVectorsParallel | 4096  |  4,000.12 us |  44.611 us |  39.546 us |  0.24 |    0.00 |
// | UseCopy            | 4096  |  4,608.49 us |  33.594 us |  31.424 us |  0.27 |    0.00 |
// | UseCopyParallel    | 4096  |  3,960.86 us |  47.334 us |  44.276 us |  0.23 |    0.00 |

// -- `.NET Framework` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4541/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
//   [Host]     : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
//   DefaultJob : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
// 
// 
// | Method             | Width | Mean         | Error      | StdDev     | Ratio | RatioSD | Code Size |
// |------------------- |------ |-------------:|-----------:|-----------:|------:|--------:|----------:|
// | Scalar             | 1024  |  1,062.91 us |  14.426 us |  12.788 us |  1.00 |    0.02 |   2,891 B |
// | ScalarParallel     | 1024  |    183.82 us |   3.609 us |   4.296 us |  0.17 |    0.00 |   2,894 B |
// | UseVectors         | 1024  |     71.65 us |   1.420 us |   1.328 us |  0.07 |    0.00 |   3,602 B |
// | UseVectorsParallel | 1024  |     24.67 us |   0.471 us |   0.579 us |  0.02 |    0.00 |   3,605 B |
// | UseCopy            | 1024  |     82.86 us |   1.653 us |   2.262 us |  0.08 |    0.00 |   3,280 B |
// | UseCopyParallel    | 1024  |     24.16 us |   0.481 us |   0.659 us |  0.02 |    0.00 |   3,283 B |
// |                    |       |              |            |            |       |         |           |
// | Scalar             | 2048  |  4,344.08 us |  68.246 us |  60.498 us |  1.00 |    0.02 |   2,891 B |
// | ScalarParallel     | 2048  |    681.94 us |  12.532 us |  11.722 us |  0.16 |    0.00 |   2,894 B |
// | UseVectors         | 2048  |    981.58 us |  14.816 us |  13.134 us |  0.23 |    0.00 |   3,602 B |
// | UseVectorsParallel | 2048  |    429.28 us |   8.360 us |  16.106 us |  0.10 |    0.00 |   3,605 B |
// | UseCopy            | 2048  |    978.79 us |  15.720 us |  13.127 us |  0.23 |    0.00 |   3,280 B |
// | UseCopyParallel    | 2048  |    438.06 us |   8.691 us |  15.672 us |  0.10 |    0.00 |   3,283 B |
// |                    |       |              |            |            |       |         |           |
// | Scalar             | 4096  | 17,306.43 us | 343.417 us | 352.664 us |  1.00 |    0.03 |   2,891 B |
// | ScalarParallel     | 4096  |  3,717.65 us |  18.424 us |  17.233 us |  0.21 |    0.00 |   2,894 B |
// | UseVectors         | 4096  |  4,451.39 us |  84.848 us |  87.132 us |  0.26 |    0.01 |   3,602 B |
// | UseVectorsParallel | 4096  |  3,818.66 us |  24.223 us |  22.658 us |  0.22 |    0.00 |   3,605 B |
// | UseCopy            | 4096  |  4,721.90 us |  88.960 us |  83.214 us |  0.27 |    0.01 |   3,280 B |
// | UseCopyParallel    | 4096  |  3,820.63 us |  19.312 us |  18.065 us |  0.22 |    0.00 |   3,283 B |
