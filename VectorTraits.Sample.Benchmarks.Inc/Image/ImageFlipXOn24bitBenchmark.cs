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

// -- `.NET8.0` on Arm

// -- `.NET7.0` on Arm

// -- `.NET8.0` on X86

// -- `.NET7.0` on X86

// -- `.NET Framework` on X86
