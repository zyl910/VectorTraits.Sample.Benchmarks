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
using System.Security.Cryptography;

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
    /// Flip a 24-bit image horizontally(FlipX) (对24位图像进行水平翻转(FlipX)).
    /// </summary>
    public class ImageFlipXOn24bitBenchmark : IDisposable {
        private bool _disposed = false;
        private static readonly Random _random = new Random(1);
        private BitmapData _sourceBitmapData = null;
        private BitmapData _destinationBitmapData = null;
        private BitmapData _expectedBitmapData = null;

        [Params(1024, 2048, 4096)]
        public int Width { get; set; }
        public int Height { get; set; }

        private static readonly Vector<byte> _shuffleIndices0;
        private static readonly Vector<byte> _shuffleIndices1;
        private static readonly Vector<byte> _shuffleIndices2;

        static ImageFlipXOn24bitBenchmark() {
            const int cbPixel = 3; // 24 bit: Bgr24, Rgb24.
            int vectorWidth = Vector<byte>.Count;
            int blockSize = vectorWidth * cbPixel;
            Span<byte> buf = stackalloc byte[blockSize];
            for (int i = 0; i < blockSize; i++) {
                int m = i / cbPixel;
                int n = i % cbPixel;
                buf[i] = (byte)((vectorWidth - 1 - m) * cbPixel + n);
            }
            _shuffleIndices0 = Vectors.Create(buf);
            _shuffleIndices1 = Vectors.Create(buf.Slice(vectorWidth * 1));
            _shuffleIndices2 = Vectors.Create(buf.Slice(vectorWidth * 2));
        }

        ~ImageFlipXOn24bitBenchmark() {
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
                    writer.WriteLine(string.Format("YShuffleX3Kernel_AcceleratedTypes:\t{0}", Vectors.YShuffleX3Kernel_AcceleratedTypes));
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
            const int cbPixel = 3; // 24 bit: Bgr24, Rgb24.
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
            const int cbPixel = 3; // 24 bit: Bgr24, Rgb24.
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
            const int cbPixel = 3; // 24 bit: Bgr24, Rgb24.
            Vector<byte> indices0 = _shuffleIndices0;
            Vector<byte> indices1 = _shuffleIndices1;
            Vector<byte> indices2 = _shuffleIndices2;
            int vectorWidth = Vector<byte>.Count;
            if (width <= vectorWidth) {
                ScalarDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
                return;
            }
            int maxX = width - vectorWidth;
            byte* pRow = pSrc;
            byte* qRow = pDst;
            for (int i = 0; i < height; i++) {
                Vector<byte>* pLast = (Vector<byte>*)pRow;
                Vector<byte>* qLast = (Vector<byte>*)(qRow + maxX * cbPixel);
                Vector<byte>* p = (Vector<byte>*)(pRow + maxX * cbPixel);
                Vector<byte>* q = (Vector<byte>*)qRow;
                for (; ; ) {
                    Vector<byte> data0, data1, data2, temp0, temp1, temp2;
                    // Load.
                    data0 = p[0];
                    data1 = p[1];
                    data2 = p[2];
                    // FlipX.
                    temp0 = Vectors.YShuffleX3Kernel(data0, data1, data2, indices0);
                    temp1 = Vectors.YShuffleX3Kernel(data0, data1, data2, indices1);
                    temp2 = Vectors.YShuffleX3Kernel(data0, data1, data2, indices2);
                    // Store.
                    q[0] = temp0;
                    q[1] = temp1;
                    q[2] = temp2;
                    // Next.
                    if (p <= pLast) break;
                    p -= cbPixel;
                    q += cbPixel;
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
            const int cbPixel = 3; // 24 bit: Bgr24, Rgb24.
            Vectors.YShuffleX3Kernel_Args(_shuffleIndices0, out var indices0arg0, out var indices0arg1, out var indices0arg2, out var indices0arg3);
            Vectors.YShuffleX3Kernel_Args(_shuffleIndices1, out var indices1arg0, out var indices1arg1, out var indices1arg2, out var indices1arg3);
            Vectors.YShuffleX3Kernel_Args(_shuffleIndices2, out var indices2arg0, out var indices2arg1, out var indices2arg2, out var indices2arg3);
            int vectorWidth = Vector<byte>.Count;
            if (width <= vectorWidth) {
                ScalarDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
                return;
            }
            int maxX = width - vectorWidth;
            byte* pRow = pSrc;
            byte* qRow = pDst;
            for (int i = 0; i < height; i++) {
                Vector<byte>* pLast = (Vector<byte>*)pRow;
                Vector<byte>* qLast = (Vector<byte>*)(qRow + maxX * cbPixel);
                Vector<byte>* p = (Vector<byte>*)(pRow + maxX * cbPixel);
                Vector<byte>* q = (Vector<byte>*)qRow;
                for (; ; ) {
                    Vector<byte> data0, data1, data2, temp0, temp1, temp2;
                    // Load.
                    data0 = p[0];
                    data1 = p[1];
                    data2 = p[2];
                    // FlipX.
                    //temp0 = Vectors.YShuffleX3Kernel(data0, data1, data2, _shuffleIndices0);
                    //temp1 = Vectors.YShuffleX3Kernel(data0, data1, data2, _shuffleIndices1);
                    //temp2 = Vectors.YShuffleX3Kernel(data0, data1, data2, _shuffleIndices2);
                    temp0 = Vectors.YShuffleX3Kernel_Core(data0, data1, data2, indices0arg0, indices0arg1, indices0arg2, indices0arg3);
                    temp1 = Vectors.YShuffleX3Kernel_Core(data0, data1, data2, indices1arg0, indices1arg1, indices1arg2, indices1arg3);
                    temp2 = Vectors.YShuffleX3Kernel_Core(data0, data1, data2, indices2arg0, indices2arg1, indices2arg2, indices2arg3);
                    // Store.
                    q[0] = temp0;
                    q[1] = temp1;
                    q[2] = temp2;
                    // Next.
                    if (p <= pLast) break;
                    p -= cbPixel;
                    q += cbPixel;
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

// -- `.NET6.0` on Arm
// Vectors.Instance:	VectorTraits128AdvSimdB64	// AdvSimd
// YShuffleX3Kernel_AcceleratedTypes:	SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
// 
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.1.1 (24B91) [Darwin 24.1.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 8.0.204
//   [Host]     : .NET 6.0.33 (6.0.3324.36610), Arm64 RyuJIT AdvSIMD
//   DefaultJob : .NET 6.0.33 (6.0.3324.36610), Arm64 RyuJIT AdvSIMD
// 
// 
// | Method         | Width | Mean         | Error     | StdDev    | Ratio |
// |--------------- |------ |-------------:|----------:|----------:|------:|
// | Scalar         | 1024  |  1,504.84 us |  0.449 us |  0.375 us |  1.00 |
// | UseVectors     | 1024  |    119.36 us |  0.042 us |  0.040 us |  0.08 |
// | UseVectorsArgs | 1024  |     83.89 us |  0.160 us |  0.149 us |  0.06 |
// |                |       |              |           |           |       |
// | Scalar         | 2048  |  6,011.17 us |  1.346 us |  1.193 us |  1.00 |
// | UseVectors     | 2048  |    476.02 us |  6.485 us |  6.066 us |  0.08 |
// | UseVectorsArgs | 2048  |    328.52 us |  0.298 us |  0.264 us |  0.05 |
// |                |       |              |           |           |       |
// | Scalar         | 4096  | 24,403.68 us |  6.763 us |  6.326 us |  1.00 |
// | UseVectors     | 4096  |  3,378.05 us |  1.674 us |  1.566 us |  0.14 |
// | UseVectorsArgs | 4096  |  2,852.52 us | 22.086 us | 20.660 us |  0.12 |

// -- `.NET7.0` on Arm
// Vectors.Instance:	VectorTraits128AdvSimdB64	// AdvSimd
// YShuffleX3Kernel_AcceleratedTypes:	SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
// 
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.1.1 (24B91) [Darwin 24.1.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 8.0.204
//   [Host]     : .NET 7.0.20 (7.0.2024.26716), Arm64 RyuJIT AdvSIMD
//   DefaultJob : .NET 7.0.20 (7.0.2024.26716), Arm64 RyuJIT AdvSIMD
// 
// 
// | Method         | Width | Mean         | Error    | StdDev   | Ratio |
// |--------------- |------ |-------------:|---------:|---------:|------:|
// | Scalar         | 1024  |  1,504.47 us | 0.639 us | 0.566 us |  1.00 |
// | UseVectors     | 1024  |    108.65 us | 0.139 us | 0.123 us |  0.07 |
// | UseVectorsArgs | 1024  |     81.78 us | 0.142 us | 0.133 us |  0.05 |
// |                |       |              |          |          |       |
// | Scalar         | 2048  |  6,014.20 us | 2.201 us | 1.718 us |  1.00 |
// | UseVectors     | 2048  |    427.18 us | 0.286 us | 0.267 us |  0.07 |
// | UseVectorsArgs | 2048  |    318.35 us | 0.373 us | 0.330 us |  0.05 |
// |                |       |              |          |          |       |
// | Scalar         | 4096  | 24,403.88 us | 6.181 us | 5.480 us |  1.00 |
// | UseVectors     | 4096  |  3,280.84 us | 4.771 us | 4.463 us |  0.13 |
// | UseVectorsArgs | 4096  |  2,873.47 us | 4.675 us | 4.373 us |  0.12 |

// -- `.NET8.0` on Arm
// Vectors.Instance:	VectorTraits128AdvSimdB64	// AdvSimd
// YShuffleX3Kernel_AcceleratedTypes:	SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
// 
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.1.1 (24B91) [Darwin 24.1.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 8.0.204
//   [Host]     : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD
//   DefaultJob : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD
// 
// 
// | Method         | Width | Mean        | Error     | StdDev    | Ratio |
// |--------------- |------ |------------:|----------:|----------:|------:|
// | Scalar         | 1024  |   478.43 us |  2.053 us |  1.921 us |  1.00 |
// | UseVectors     | 1024  |    61.18 us |  0.677 us |  0.633 us |  0.13 |
// | UseVectorsArgs | 1024  |    61.93 us |  0.225 us |  0.199 us |  0.13 |
// |                |       |             |           |           |       |
// | Scalar         | 2048  | 1,891.65 us |  5.621 us |  4.693 us |  1.00 |
// | UseVectors     | 2048  |   260.20 us |  0.201 us |  0.179 us |  0.14 |
// | UseVectorsArgs | 2048  |   263.75 us |  0.851 us |  0.796 us |  0.14 |
// |                |       |             |           |           |       |
// | Scalar         | 4096  | 7,900.34 us | 91.227 us | 85.333 us |  1.00 |
// | UseVectors     | 4096  | 2,310.99 us | 17.264 us | 14.416 us |  0.29 |
// | UseVectorsArgs | 4096  | 2,310.74 us |  1.605 us |  1.423 us |  0.29 |

// -- `.NET6.0` on X86
// Vectors.Instance:       VectorTraits256Avx2     // Avx, Avx2, Sse, Sse2
// YShuffleX3Kernel_AcceleratedTypes:      SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
// 
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4541/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 8.0.403
//   [Host]     : .NET 6.0.35 (6.0.3524.45918), X64 RyuJIT AVX2
//   DefaultJob : .NET 6.0.35 (6.0.3524.45918), X64 RyuJIT AVX2
// 
// 
// | Method         | Width | Mean        | Error     | StdDev    | Ratio | RatioSD | Code Size |
// |--------------- |------ |------------:|----------:|----------:|------:|--------:|----------:|
// | Scalar         | 1024  |  1,110.8 us |  21.74 us |  22.33 us |  1.00 |    0.03 |   2,053 B |
// | UseVectors     | 1024  |    492.3 us |   9.74 us |  15.72 us |  0.44 |    0.02 |   4,505 B |
// | UseVectorsArgs | 1024  |    238.9 us |   3.14 us |   2.94 us |  0.22 |    0.00 |   4,234 B |
// |                |       |             |           |           |       |         |           |
// | Scalar         | 2048  |  4,430.0 us |  87.93 us |  94.08 us |  1.00 |    0.03 |   2,053 B |
// | UseVectors     | 2048  |  2,319.6 us |  18.62 us |  17.41 us |  0.52 |    0.01 |   4,505 B |
// | UseVectorsArgs | 2048  |  1,793.2 us |  34.57 us |  33.95 us |  0.40 |    0.01 |   4,234 B |
// |                |       |             |           |           |       |         |           |
// | Scalar         | 4096  | 16,536.4 us | 329.23 us | 618.37 us |  1.00 |    0.05 |   2,053 B |
// | UseVectors     | 4096  |  9,040.4 us | 104.73 us |  97.96 us |  0.55 |    0.02 |   4,490 B |
// | UseVectorsArgs | 4096  |  6,728.0 us | 120.28 us | 133.69 us |  0.41 |    0.02 |   4,219 B |

// -- `.NET7.0` on X86
//Vectors.Instance:       VectorTraits256Avx2     // Avx, Avx2, Sse, Sse2
//YShuffleX3Kernel_AcceleratedTypes:      SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
//
//BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4541/23H2/2023Update/SunValley3)
//AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
//.NET SDK 8.0.403
//  [Host]     : .NET 7.0.20 (7.0.2024.26716), X64 RyuJIT AVX2
//  DefaultJob : .NET 7.0.20 (7.0.2024.26716), X64 RyuJIT AVX2
//
//
//| Method         | Width | Mean        | Error     | StdDev    | Ratio | RatioSD | Code Size |
//|--------------- |------ |------------:|----------:|----------:|------:|--------:|----------:|
//| Scalar         | 1024  |  1,120.3 us |  22.39 us |  25.78 us |  1.00 |    0.03 |   1,673 B |
//| UseVectors     | 1024  |    236.7 us |   4.63 us |   5.69 us |  0.21 |    0.01 |   3,724 B |
//| UseVectorsArgs | 1024  |    209.5 us |   4.00 us |   4.45 us |  0.19 |    0.01 |   4,031 B |
//|                |       |             |           |           |       |         |           |
//| Scalar         | 2048  |  4,431.6 us |  65.38 us |  61.16 us |  1.00 |    0.02 |   1,673 B |
//| UseVectors     | 2048  |  1,866.8 us |  36.26 us |  48.41 us |  0.42 |    0.01 |   3,724 B |
//| UseVectorsArgs | 2048  |  1,889.9 us |  37.54 us |  74.97 us |  0.43 |    0.02 |   4,031 B |
//|                |       |             |           |           |       |         |           |
//| Scalar         | 4096  | 16,617.9 us | 329.75 us | 559.94 us |  1.00 |    0.05 |   1,673 B |
//| UseVectors     | 4096  |  6,337.2 us |  62.08 us |  55.03 us |  0.38 |    0.01 |   3,709 B |
//| UseVectorsArgs | 4096  |  6,408.1 us | 126.27 us | 118.11 us |  0.39 |    0.01 |   4,016 B |

// -- `.NET8.0` on X86
// Vectors.Instance:       VectorTraits256Avx2     // Avx, Avx2, Sse, Sse2, Avx512VL
// YShuffleX3Kernel_AcceleratedTypes:      SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
// 
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4541/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 8.0.403
//   [Host]     : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
//   DefaultJob : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
// 
// 
// | Method         | Width | Mean        | Error      | StdDev     | Ratio | RatioSD |
// |--------------- |------ |------------:|-----------:|-----------:|------:|--------:|
// | Scalar         | 1024  |   549.22 us |  10.876 us |  11.637 us |  1.00 |    0.03 |
// | UseVectors     | 1024  |    68.21 us |   1.326 us |   2.142 us |  0.12 |    0.00 |
// | UseVectorsArgs | 1024  |    68.71 us |   1.360 us |   2.453 us |  0.13 |    0.01 |
// |                |       |             |            |            |       |         |
// | Scalar         | 2048  | 2,704.83 us |  53.643 us |  92.531 us |  1.00 |    0.05 |
// | UseVectors     | 2048  | 1,014.52 us |   8.824 us |   7.822 us |  0.38 |    0.01 |
// | UseVectorsArgs | 2048  | 1,020.66 us |  15.739 us |  14.723 us |  0.38 |    0.01 |
// |                |       |             |            |            |       |         |
// | Scalar         | 4096  | 9,778.60 us | 114.022 us | 106.656 us |  1.00 |    0.01 |
// | UseVectors     | 4096  | 4,360.43 us |  60.832 us |  56.903 us |  0.45 |    0.01 |
// | UseVectorsArgs | 4096  | 4,341.89 us |  82.877 us | 101.780 us |  0.44 |    0.01 |

// -- `.NET Framework` on X86
// Vectors.Instance:       VectorTraits256Base     //
// YShuffleX3Kernel_AcceleratedTypes:      None
// 
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4541/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
//   [Host]     : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
//   DefaultJob : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
// 
// 
// | Method         | Width | Mean        | Error       | StdDev      | Ratio | RatioSD | Code Size |
// |--------------- |------ |------------:|------------:|------------:|------:|--------:|----------:|
// | Scalar         | 1024  |    999.7 us |    14.16 us |    11.82 us |  1.00 |    0.02 |   2,717 B |
// | UseVectors     | 1024  |  6,040.0 us |    57.76 us |    54.03 us |  6.04 |    0.09 |        NA |
// | UseVectorsArgs | 1024  |  5,896.4 us |   105.77 us |    98.94 us |  5.90 |    0.12 |        NA |
// |                |       |             |             |             |       |         |           |
// | Scalar         | 2048  |  4,267.0 us |    74.72 us |    69.90 us |  1.00 |    0.02 |   2,717 B |
// | UseVectors     | 2048  | 23,070.7 us |   250.11 us |   221.72 us |  5.41 |    0.10 |        NA |
// | UseVectorsArgs | 2048  | 23,106.7 us |   241.23 us |   201.44 us |  5.42 |    0.10 |        NA |
// |                |       |             |             |             |       |         |           |
// | Scalar         | 4096  | 15,977.6 us |   308.91 us |   489.96 us |  1.00 |    0.04 |   2,717 B |
// | UseVectors     | 4096  | 91,944.4 us | 1,152.83 us | 1,078.36 us |  5.76 |    0.19 |        NA |
// | UseVectorsArgs | 4096  | 92,677.3 us | 1,555.69 us | 1,527.90 us |  5.81 |    0.20 |        NA |
