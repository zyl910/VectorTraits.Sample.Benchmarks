//#undef BENCHMARKS_OFF

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


        // -- Indices of YShuffleX3Kernel
        private static readonly Vector<byte> _shuffleIndices0;
        private static readonly Vector<byte> _shuffleIndices1;
        private static readonly Vector<byte> _shuffleIndices2;

        // -- Indices of YShuffleX2Kernel
        private static readonly byte _shuffleX2Offset0 = (byte)Vector<byte>.Count;
        private static readonly byte _shuffleX2Offset1A = 0;
        private static readonly byte _shuffleX2Offset1B = (byte)(Vector<byte>.Count / 3 * 3);
        private static readonly byte _shuffleX2Offset2 = 0;
        private static readonly Vector<byte> _shuffleX2Indices0;
        private static readonly Vector<byte> _shuffleX2Indices1A; // Need YShuffleX3Kernel
        private static readonly Vector<byte> _shuffleX2Indices1B;
        private static readonly Vector<byte> _shuffleX2Indices2;

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
            // -- Indices of YShuffleX2Kernel
            _shuffleX2Indices0 = Vector.Subtract(_shuffleIndices0, new Vector<byte>(_shuffleX2Offset0));
            _shuffleX2Indices1A = _shuffleIndices1; // _shuffleX2Offset1A is 0
            _shuffleX2Indices1B = Vector.Subtract(_shuffleIndices1, new Vector<byte>(_shuffleX2Offset1B));
            _shuffleX2Indices2 = _shuffleIndices2; // _shuffleX2Offset2 is 0
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
                    // UseVectorsX2AArgs
                    UseVectorsX2AArgs();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectorsX2AArgs: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // UseVectorsX2AArgsParallel
                    UseVectorsX2AArgsParallel();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectorsX2AArgsParallel: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // UseVectorsX2BArgs
                    UseVectorsX2BArgs();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectorsX2BArgs: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // UseVectorsX2BArgsParallel
                    UseVectorsX2BArgsParallel();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectorsX2BArgsParallel: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // ImageshopSse
#if NETCOREAPP3_0_OR_GREATER
                    ImageshopSse();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of ImageshopSse: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
#endif
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

        [Benchmark]
        public void UseVectorsX2AArgs() {
            UseVectorsX2AArgsDo(_sourceBitmapData, _destinationBitmapData, false);
        }

        //[Benchmark]
        public void UseVectorsX2AArgsParallel() {
            UseVectorsX2AArgsDo(_sourceBitmapData, _destinationBitmapData, true);
        }

        public static unsafe void UseVectorsX2AArgsDo(BitmapData src, BitmapData dst, bool useParallel = false) {
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
                    UseVectorsX2AArgsDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                UseVectorsX2AArgsDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void UseVectorsX2AArgsDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            const int cbPixel = 3; // 24 bit: Bgr24, Rgb24.
            Vectors.YShuffleX2Kernel_Args(_shuffleX2Indices0, out var indices0arg0, out var indices0arg1, out var indices0arg2, out var indices0arg3);
            Vectors.YShuffleX3Kernel_Args(_shuffleX2Indices1A, out var indices1arg0, out var indices1arg1, out var indices1arg2, out var indices1arg3);
            Vectors.YShuffleX2Kernel_Args(_shuffleX2Indices2, out var indices2arg0, out var indices2arg1, out var indices2arg2, out var indices2arg3);
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
                    temp0 = Vectors.YShuffleX2Kernel_Core(data1, data2, indices0arg0, indices0arg1, indices0arg2, indices0arg3);
                    temp1 = Vectors.YShuffleX3Kernel_Core(data0, data1, data2, indices1arg0, indices1arg1, indices1arg2, indices1arg3);
                    temp2 = Vectors.YShuffleX2Kernel_Core(data0, data1, indices2arg0, indices2arg1, indices2arg2, indices2arg3);
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
        public void UseVectorsX2BArgs() {
            UseVectorsX2BArgsDo(_sourceBitmapData, _destinationBitmapData, false);
        }

        //[Benchmark]
        public void UseVectorsX2BArgsParallel() {
            UseVectorsX2BArgsDo(_sourceBitmapData, _destinationBitmapData, true);
        }

        public static unsafe void UseVectorsX2BArgsDo(BitmapData src, BitmapData dst, bool useParallel = false) {
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
                    UseVectorsX2BArgsDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                UseVectorsX2BArgsDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void UseVectorsX2BArgsDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            const int cbPixel = 3; // 24 bit: Bgr24, Rgb24.
            int offsetB0 = _shuffleX2Offset1B;
            int offsetB1 = offsetB0 + Vector<byte>.Count;
            Vectors.YShuffleX2Kernel_Args(_shuffleX2Indices0, out var indices0arg0, out var indices0arg1, out var indices0arg2, out var indices0arg3);
            Vectors.YShuffleX2Kernel_Args(_shuffleX2Indices1B, out var indices1arg0, out var indices1arg1, out var indices1arg2, out var indices1arg3);
            Vectors.YShuffleX2Kernel_Args(_shuffleX2Indices2, out var indices2arg0, out var indices2arg1, out var indices2arg2, out var indices2arg3);
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
                    Vector<byte> data0, data1, data2, dataB0, dataB1, temp0, temp1, temp2;
                    // Load.
                    data0 = p[0];
                    data1 = p[1];
                    data2 = p[2];
                    dataB0 = *(Vector<byte>*)((byte*)p + offsetB0);
                    dataB1 = *(Vector<byte>*)((byte*)p + offsetB1);
                    // FlipX.
                    temp0 = Vectors.YShuffleX2Kernel_Core(data1, data2, indices0arg0, indices0arg1, indices0arg2, indices0arg3);
                    temp1 = Vectors.YShuffleX2Kernel_Core(dataB0, dataB1, indices1arg0, indices1arg1, indices1arg2, indices1arg3);
                    temp2 = Vectors.YShuffleX2Kernel_Core(data0, data1, indices2arg0, indices2arg1, indices2arg2, indices2arg3);
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

        // == From Imageshop. https://www.cnblogs.com/zyl910/p/18587292/VectorTraits_Sample_Image_ImageFlipXOn24bitBenchmark
#if NETCOREAPP3_0_OR_GREATER

        [Benchmark]
        public unsafe void ImageshopSse() {
            Imageshop.IM_FlipLeftRight((byte*)_sourceBitmapData.Scan0, (byte*)_destinationBitmapData.Scan0, _sourceBitmapData.Width, _sourceBitmapData.Height, _sourceBitmapData.Stride);
        }

        /// <summary>
        /// From Imageshop. https://www.cnblogs.com/zyl910/p/18587292/VectorTraits_Sample_Image_ImageFlipXOn24bitBenchmark
        /// </summary>
        internal static class Imageshop {
            const int IM_STATUS_OK = 0;
            const int IM_STATUS_OUTOFMEMORY = -1;

            // -- SSSE3
            // int IM_FlipLeftRight(unsigned char *Src, unsigned char *Dest, int Width, int Height, int Stride) {
            //     int Channel = Stride / Width;
            //     unsigned char *Buffer = NULL;
            //     if (Src == Dest) {
            //         Buffer = (unsigned char *)malloc(Width * Channel * sizeof(unsigned char));
            //         if (Buffer == NULL) return IM_STATUS_OUTOFMEMORY;
            //     }
            // 
            //     if (Channel == 3) {
            //         int BlockSize = 16;
            //         int Block = Width / BlockSize;
            //         __m128i Mask1 = _mm_setr_epi8(13, 14, 15, 10, 11, 12, 7, 8, 9, 4, 5, 6, 1, 2, 3, 0);
            //         __m128i Mask2 = _mm_setr_epi8(14, 15, 11, 12, 13, 8, 9, 10, 5, 6, 7, 2, 3, 4, 0, 1);
            //         __m128i Mask3 = _mm_setr_epi8(15, 12, 13, 14, 9, 10, 11, 6, 7, 8, 3, 4, 5, 0, 1, 2);
            //         for (int Y = 0; Y < Height; Y++) {
            //             unsigned char *LinePS = NULL;
            //             if (Src == Dest) {
            //                 memcpy(Buffer, Src + Y * Stride, Width * 3);
            //                 LinePS = Buffer + Width * 3;
            //             } else {
            //                 LinePS = Src + Y * Stride + Width * 3;
            //             }
            // 
            //             unsigned char *LinePD = Dest + Y * Stride;
            //             int X = 0;
            //             for (; X < Block * BlockSize; X += BlockSize, LinePS -= 48, LinePD += 48) {
            //                 __m128i SrcV1 = _mm_loadu_si128((const __m128i *)(LinePS - 16));
            //                 SrcV1 = _mm_shuffle_epi8(SrcV1, Mask1);
            //                 _mm_storeu_si128((__m128i *)(LinePD + 0), SrcV1);
            //                 __m128i SrcV2 = _mm_loadu_si128((const __m128i *)(LinePS - 32));
            //                 SrcV2 = _mm_shuffle_epi8(SrcV2, Mask2);
            //                 _mm_storeu_si128((__m128i *)(LinePD + 16), SrcV2);
            //                 __m128i SrcV3 = _mm_loadu_si128((const __m128i *)(LinePS - 48));
            //                 SrcV3 = _mm_shuffle_epi8(SrcV3, Mask3);
            //                 _mm_storeu_si128((__m128i *)(LinePD + 32), SrcV3);
            //                 LinePD[15] = LinePS[-18]; LinePD[16] = LinePS[-17]; LinePD[17] = LinePS[-16];
            //                 LinePD[30] = LinePS[-33]; LinePD[31] = LinePS[-32]; LinePD[32] = LinePS[-31];
            //             }
            //             for (; X < Width; X++, LinePS -= 3, LinePD += 3) {
            //                 LinePD[0] = LinePS[-3]; LinePD[1] = LinePS[-2]; LinePD[2] = LinePS[-1];
            //             }
            //         }
            //     }
            // 
            //     if (Buffer != NULL) free(Buffer);
            //     return IM_STATUS_OK;
            // }

            public static unsafe int IM_FlipLeftRight(byte* Src, byte* Dest, int Width, int Height, int Stride) {
                if (!Sse2.IsSupported) throw new NotSupportedException("Not support X86's Sse2!");
                if (!Ssse3.IsSupported) throw new NotSupportedException("Not support X86's Ssse3!");
                int Channel = Stride / Width;
                byte* Buffer = null;
                try {
                    if (Src == Dest) {
                        Buffer = (byte*)Marshal.AllocHGlobal(Width * Channel);
                        if (Buffer == null) return IM_STATUS_OUTOFMEMORY;
                    }

                    if (Channel == 3) {
                        const int BlockSize = 16;
                        int Block = Width / BlockSize;
                        Vector128<byte> Mask1 = Vector128.Create((byte)13, 14, 15, 10, 11, 12, 7, 8, 9, 4, 5, 6, 1, 2, 3, 0);
                        Vector128<byte> Mask2 = Vector128.Create((byte)14, 15, 11, 12, 13, 8, 9, 10, 5, 6, 7, 2, 3, 4, 0, 1);
                        Vector128<byte> Mask3 = Vector128.Create((byte)15, 12, 13, 14, 9, 10, 11, 6, 7, 8, 3, 4, 5, 0, 1, 2);
                        for (int Y = 0; Y < Height; Y++) {
                            byte* LinePS = null;
                            if (Src == Dest) {
                                //memcpy(Buffer, Src + Y * Stride, Width * 3);
                                System.Buffer.MemoryCopy(Src + Y * Stride, Buffer, Width * 3, Width * 3);
                                LinePS = Buffer + Width * 3;
                            } else {
                                LinePS = Src + Y * Stride + Width * 3;
                            }

                            byte* LinePD = Dest + Y * Stride;
                            int X = 0;
                            for (; X < Block * BlockSize; X += BlockSize, LinePS -= 48, LinePD += 48) {
                                var SrcV1 = Sse2.LoadVector128(LinePS - 16);
                                SrcV1 = Ssse3.Shuffle(SrcV1, Mask1);
                                Ssse3.Store(LinePD + 0, SrcV1);

                                var SrcV2 = Sse2.LoadVector128(LinePS - 32);
                                SrcV2 = Ssse3.Shuffle(SrcV2, Mask2);
                                Ssse3.Store(LinePD + 16, SrcV2);

                                var SrcV3 = Sse2.LoadVector128(LinePS - 48);
                                SrcV3 = Ssse3.Shuffle(SrcV3, Mask3);
                                Ssse3.Store(LinePD + 32, SrcV3);

                                LinePD[15] = LinePS[-18]; LinePD[16] = LinePS[-17]; LinePD[17] = LinePS[-16];
                                LinePD[30] = LinePS[-33]; LinePD[31] = LinePS[-32]; LinePD[32] = LinePS[-31];
                            }

                            // 处理剩余像素
                            for (; X < Width; X++, LinePS -= 3, LinePD += 3) {
                                LinePD[0] = LinePS[-3]; LinePD[1] = LinePS[-2]; LinePD[2] = LinePS[-1];
                            }
                        }
                    }
                } finally {
                    if (Buffer != null) Marshal.FreeHGlobal((IntPtr)Buffer);
                }
                return IM_STATUS_OK;
            }

        }

#endif

    }

}

// == Benchmarks result

// -- `.NET6.0` on Arm
// Vectors.Instance:	VectorTraits128AdvSimdB64	// AdvSimd
// YShuffleX3Kernel_AcceleratedTypes:	SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
// 
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.1.1 (24B91) [Darwin 24.1.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 9.0.102
//   [Host]     : .NET 6.0.33 (6.0.3324.36610), Arm64 RyuJIT AdvSIMD
//   DefaultJob : .NET 6.0.33 (6.0.3324.36610), Arm64 RyuJIT AdvSIMD
// 
// | Method            | Width | Mean         | Error      | StdDev     | Ratio | RatioSD |
// |------------------ |------ |-------------:|-----------:|-----------:|------:|--------:|
// | Scalar            | 1024  |  1,504.09 us |   0.575 us |   0.480 us |  1.00 |    0.00 |
// | UseVectors        | 1024  |    120.26 us |   1.569 us |   1.468 us |  0.08 |    0.00 |
// | UseVectorsArgs    | 1024  |     83.77 us |   0.067 us |   0.056 us |  0.06 |    0.00 |
// | UseVectorsX2AArgs | 1024  |     72.68 us |   0.034 us |   0.030 us |  0.05 |    0.00 |
// | UseVectorsX2BArgs | 1024  |     82.61 us |   0.283 us |   0.265 us |  0.05 |    0.00 |
// | ImageshopSse      | 1024  |           NA |         NA |         NA |     ? |       ? |
// |                   |       |              |            |            |       |         |
// | Scalar            | 2048  |  6,015.27 us |   5.786 us |   4.831 us |  1.00 |    0.00 |
// | UseVectors        | 2048  |    479.44 us |   0.424 us |   0.397 us |  0.08 |    0.00 |
// | UseVectorsArgs    | 2048  |    320.78 us |   0.212 us |   0.165 us |  0.05 |    0.00 |
// | UseVectorsX2AArgs | 2048  |    332.22 us |   0.314 us |   0.263 us |  0.06 |    0.00 |
// | UseVectorsX2BArgs | 2048  |    319.60 us |   1.490 us |   1.394 us |  0.05 |    0.00 |
// | ImageshopSse      | 2048  |           NA |         NA |         NA |     ? |       ? |
// |                   |       |              |            |            |       |         |
// | Scalar            | 4096  | 24,709.98 us | 308.477 us | 288.549 us |  1.00 |    0.02 |
// | UseVectors        | 4096  |  3,362.91 us |   1.807 us |   1.509 us |  0.14 |    0.00 |
// | UseVectorsArgs    | 4096  |  2,840.79 us |  13.642 us |  12.760 us |  0.11 |    0.00 |
// | UseVectorsX2AArgs | 4096  |  2,592.20 us |  25.326 us |  23.690 us |  0.10 |    0.00 |
// | UseVectorsX2BArgs | 4096  |  2,843.72 us |  30.984 us |  28.982 us |  0.12 |    0.00 |
// | ImageshopSse      | 4096  |           NA |         NA |         NA |     ? |       ? |

// -- `.NET7.0` on Arm
// Vectors.Instance:	VectorTraits128AdvSimdB64	// AdvSimd
// YShuffleX3Kernel_AcceleratedTypes:	SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
// 
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.1.1 (24B91) [Darwin 24.1.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 9.0.102
//   [Host]     : .NET 7.0.20 (7.0.2024.26716), Arm64 RyuJIT AdvSIMD
//   DefaultJob : .NET 7.0.20 (7.0.2024.26716), Arm64 RyuJIT AdvSIMD
// 
// 
// | Method            | Width | Mean         | Error     | StdDev    | Ratio | RatioSD |
// |------------------ |------ |-------------:|----------:|----------:|------:|--------:|
// | Scalar            | 1024  |  1,506.38 us |  2.527 us |  2.240 us |  1.00 |    0.00 |
// | UseVectors        | 1024  |    108.38 us |  0.170 us |  0.159 us |  0.07 |    0.00 |
// | UseVectorsArgs    | 1024  |     81.57 us |  0.070 us |  0.058 us |  0.05 |    0.00 |
// | UseVectorsX2AArgs | 1024  |     69.35 us |  0.111 us |  0.098 us |  0.05 |    0.00 |
// | UseVectorsX2BArgs | 1024  |     80.66 us |  0.104 us |  0.081 us |  0.05 |    0.00 |
// | ImageshopSse      | 1024  |           NA |        NA |        NA |     ? |       ? |
// |                   |       |              |           |           |       |         |
// | Scalar            | 2048  |  6,014.79 us |  2.863 us |  2.235 us |  1.00 |    0.00 |
// | UseVectors        | 2048  |    425.96 us |  0.234 us |  0.207 us |  0.07 |    0.00 |
// | UseVectorsArgs    | 2048  |    317.95 us |  0.273 us |  0.228 us |  0.05 |    0.00 |
// | UseVectorsX2AArgs | 2048  |    270.73 us |  0.238 us |  0.199 us |  0.05 |    0.00 |
// | UseVectorsX2BArgs | 2048  |    308.50 us |  1.324 us |  1.239 us |  0.05 |    0.00 |
// | ImageshopSse      | 2048  |           NA |        NA |        NA |     ? |       ? |
// |                   |       |              |           |           |       |         |
// | Scalar            | 4096  | 24,451.53 us | 31.420 us | 27.853 us |  1.00 |    0.00 |
// | UseVectors        | 4096  |  3,263.99 us |  3.354 us |  2.801 us |  0.13 |    0.00 |
// | UseVectorsArgs    | 4096  |  2,868.68 us |  7.482 us |  6.999 us |  0.12 |    0.00 |
// | UseVectorsX2AArgs | 4096  |  2,512.38 us | 11.036 us |  9.783 us |  0.10 |    0.00 |
// | UseVectorsX2BArgs | 4096  |  2,787.01 us |  4.692 us |  3.918 us |  0.11 |    0.00 |
// | ImageshopSse      | 4096  |           NA |        NA |        NA |     ? |       ? |

// -- `.NET8.0` on Arm
// Vectors.Instance:	VectorTraits128AdvSimdB64	// AdvSimd
// YShuffleX3Kernel_AcceleratedTypes:	SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
// 
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.1.1 (24B91) [Darwin 24.1.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 9.0.102
//   [Host]     : .NET 8.0.12 (8.0.1224.60305), Arm64 RyuJIT AdvSIMD
//   DefaultJob : .NET 8.0.12 (8.0.1224.60305), Arm64 RyuJIT AdvSIMD
// 
// | Method            | Width | Mean        | Error     | StdDev    | Ratio | RatioSD |
// |------------------ |------ |------------:|----------:|----------:|------:|--------:|
// | Scalar            | 1024  |   489.45 us |  9.667 us |  8.570 us |  1.00 |    0.02 |
// | UseVectors        | 1024  |    60.78 us |  0.050 us |  0.045 us |  0.12 |    0.00 |
// | UseVectorsArgs    | 1024  |    60.20 us |  0.621 us |  0.551 us |  0.12 |    0.00 |
// | UseVectorsX2AArgs | 1024  |    61.02 us |  0.054 us |  0.045 us |  0.12 |    0.00 |
// | UseVectorsX2BArgs | 1024  |    73.73 us |  0.159 us |  0.141 us |  0.15 |    0.00 |
// | ImageshopSse      | 1024  |          NA |        NA |        NA |     ? |       ? |
// |                   |       |             |           |           |       |         |
// | Scalar            | 2048  | 1,904.18 us | 25.572 us | 23.920 us |  1.00 |    0.02 |
// | UseVectors        | 2048  |   262.79 us |  0.482 us |  0.428 us |  0.14 |    0.00 |
// | UseVectorsArgs    | 2048  |   266.08 us |  1.379 us |  1.290 us |  0.14 |    0.00 |
// | UseVectorsX2AArgs | 2048  |   266.29 us |  0.949 us |  0.887 us |  0.14 |    0.00 |
// | UseVectorsX2BArgs | 2048  |   297.26 us |  1.482 us |  1.237 us |  0.16 |    0.00 |
// | ImageshopSse      | 2048  |          NA |        NA |        NA |     ? |       ? |
// |                   |       |             |           |           |       |         |
// | Scalar            | 4096  | 8,042.44 us | 17.405 us | 16.281 us |  1.00 |    0.00 |
// | UseVectors        | 4096  | 2,307.59 us |  2.411 us |  1.882 us |  0.29 |    0.00 |
// | UseVectorsArgs    | 4096  | 2,309.09 us |  4.411 us |  3.910 us |  0.29 |    0.00 |
// | UseVectorsX2AArgs | 4096  | 2,193.09 us |  7.278 us |  6.078 us |  0.27 |    0.00 |
// | UseVectorsX2BArgs | 4096  | 2,478.22 us |  3.373 us |  2.816 us |  0.31 |    0.00 |
// | ImageshopSse      | 4096  |          NA |        NA |        NA |     ? |       ? |

// -- `.NET6.0` on X86
// Vectors.Instance:       VectorTraits256Avx2     // Avx, Avx2, Sse, Sse2
// YShuffleX3Kernel_AcceleratedTypes:      SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
// 
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.26100.3476)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 9.0.200
//   [Host]     : .NET 6.0.36 (6.0.3624.51421), X64 RyuJIT AVX2
//   DefaultJob : .NET 6.0.36 (6.0.3624.51421), X64 RyuJIT AVX2
// 
// 
// | Method            | Width | Mean        | Error     | StdDev    | Ratio | Code Size |
// |------------------ |------ |------------:|----------:|----------:|------:|----------:|
// | Scalar            | 1024  |  1,047.8 us |  10.47 us |   9.79 us |  1.00 |   2,053 B |
// | UseVectors        | 1024  |    375.6 us |   7.49 us |   7.69 us |  0.36 |   4,505 B |
// | UseVectorsArgs    | 1024  |    202.0 us |   4.02 us |   4.94 us |  0.19 |   4,234 B |
// | UseVectorsX2AArgs | 1024  |    149.6 us |   2.97 us |   8.63 us |  0.14 |   4,275 B |
// | UseVectorsX2BArgs | 1024  |    125.2 us |   2.39 us |   2.11 us |  0.12 |   3,835 B |
// | ImageshopSse      | 1024  |    145.0 us |   2.81 us |   4.30 us |  0.14 |   1,440 B |
// |                   |       |             |           |           |       |           |
// | Scalar            | 2048  |  4,248.4 us |  41.26 us |  38.59 us |  1.00 |   2,053 B |
// | UseVectors        | 2048  |  2,578.7 us |  18.84 us |  17.63 us |  0.61 |   4,505 B |
// | UseVectorsArgs    | 2048  |  2,022.4 us |  22.92 us |  21.44 us |  0.48 |   4,234 B |
// | UseVectorsX2AArgs | 2048  |  1,710.7 us |  16.22 us |  14.38 us |  0.40 |   4,275 B |
// | UseVectorsX2BArgs | 2048  |  1,682.1 us |  18.11 us |  16.94 us |  0.40 |   3,835 B |
// | ImageshopSse      | 2048  |  1,854.0 us |  21.15 us |  19.78 us |  0.44 |   1,440 B |
// |                   |       |             |           |           |       |           |
// | Scalar            | 4096  | 16,231.0 us | 133.81 us | 118.62 us |  1.00 |   2,053 B |
// | UseVectors        | 4096  |  8,418.7 us |  55.64 us |  52.04 us |  0.52 |   4,490 B |
// | UseVectorsArgs    | 4096  |  5,906.4 us |  49.55 us |  46.34 us |  0.36 |   4,219 B |
// | UseVectorsX2AArgs | 4096  |  5,497.9 us |  46.65 us |  43.64 us |  0.34 |   4,260 B |
// | UseVectorsX2BArgs | 4096  |  5,385.9 us |  79.28 us |  74.16 us |  0.33 |   3,820 B |
// | ImageshopSse      | 4096  |  5,784.4 us |  50.70 us |  44.94 us |  0.36 |   1,440 B |

// -- `.NET7.0` on X86
//Vectors.Instance:       VectorTraits256Avx2     // Avx, Avx2, Sse, Sse2
//YShuffleX3Kernel_AcceleratedTypes:      SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
//
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.26100.3476)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 9.0.200
//   [Host]     : .NET 7.0.20 (7.0.2024.26716), X64 RyuJIT AVX2
//   DefaultJob : .NET 7.0.20 (7.0.2024.26716), X64 RyuJIT AVX2
// 
// 
// | Method            | Width | Mean        | Error     | StdDev    | Ratio | Code Size |
// |------------------ |------ |------------:|----------:|----------:|------:|----------:|
// | Scalar            | 1024  |  1,009.2 us |  10.62 us |   9.42 us |  1.00 |   1,673 B |
// | UseVectors        | 1024  |    214.5 us |   4.05 us |   3.98 us |  0.21 |   3,724 B |
// | UseVectorsArgs    | 1024  |    179.5 us |   3.47 us |   3.71 us |  0.18 |   4,031 B |
// | UseVectorsX2AArgs | 1024  |    146.9 us |   2.89 us |   2.84 us |  0.15 |   3,912 B |
// | UseVectorsX2BArgs | 1024  |    119.5 us |   2.39 us |   2.75 us |  0.12 |   3,673 B |
// | ImageshopSse      | 1024  |    149.3 us |   2.92 us |   5.42 us |  0.15 |   1,350 B |
// |                   |       |             |           |           |       |           |
// | Scalar            | 2048  |  4,233.3 us |  48.45 us |  45.32 us |  1.00 |   1,673 B |
// | UseVectors        | 2048  |  1,707.1 us |  21.99 us |  20.57 us |  0.40 |   3,724 B |
// | UseVectorsArgs    | 2048  |  1,625.7 us |  14.62 us |  13.68 us |  0.38 |   4,031 B |
// | UseVectorsX2AArgs | 2048  |  1,519.1 us |  19.57 us |  18.30 us |  0.36 |   3,912 B |
// | UseVectorsX2BArgs | 2048  |  1,439.8 us |  16.77 us |  15.69 us |  0.34 |   3,673 B |
// | ImageshopSse      | 2048  |  1,425.7 us |  18.37 us |  16.28 us |  0.34 |   1,350 B |
// |                   |       |             |           |           |       |           |
// | Scalar            | 4096  | 15,994.4 us | 134.29 us | 119.04 us |  1.00 |   1,673 B |
// | UseVectors        | 4096  |  5,962.0 us |  76.95 us |  68.22 us |  0.37 |   3,709 B |
// | UseVectorsArgs    | 4096  |  5,858.2 us |  74.10 us |  69.31 us |  0.37 |   4,016 B |
// | UseVectorsX2AArgs | 4096  |  5,528.2 us |  34.26 us |  32.05 us |  0.35 |   3,897 B |
// | UseVectorsX2BArgs | 4096  |  5,342.9 us |  51.69 us |  48.35 us |  0.33 |   3,658 B |
// | ImageshopSse      | 4096  |  5,603.8 us |  38.53 us |  34.15 us |  0.35 |   1,350 B |

// -- `.NET8.0` on X86
// Vectors.Instance:       VectorTraits256Avx2     // Avx, Avx2, Sse, Sse2, Avx512VL
// YShuffleX3Kernel_AcceleratedTypes:      SByte, Byte, Int16, UInt16, Int32, UInt32, Int64, UInt64, Single, Double
// 
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.26100.3476)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 9.0.200
//   [Host]     : .NET 8.0.13 (8.0.1325.6609), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
//   DefaultJob : .NET 8.0.13 (8.0.1325.6609), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
// 
// 
// | Method            | Width | Mean         | Error     | StdDev    | Ratio | Code Size |
// |------------------ |------ |-------------:|----------:|----------:|------:|----------:|
// | Scalar            | 1024  |    565.61 us |  6.062 us |  5.671 us |  1.00 |        NA |
// | UseVectors        | 1024  |     70.15 us |  0.946 us |  0.839 us |  0.12 |        NA |
// | UseVectorsArgs    | 1024  |     71.35 us |  1.395 us |  2.368 us |  0.13 |        NA |
// | UseVectorsX2AArgs | 1024  |     70.38 us |  1.389 us |  1.757 us |  0.12 |        NA |
// | UseVectorsX2BArgs | 1024  |     71.11 us |  1.417 us |  1.325 us |  0.13 |        NA |
// | ImageshopSse      | 1024  |    147.10 us |  3.065 us |  5.286 us |  0.28 |   1,304 B |
// |                   |       |              |           |           |       |           |
// | Scalar            | 2048  |  2,778.83 us | 31.741 us | 28.137 us |  1.00 |        NA |
// | UseVectors        | 2048  |  1,021.40 us | 10.916 us | 10.211 us |  0.37 |        NA |
// | UseVectorsArgs    | 2048  |  1,057.84 us | 20.079 us | 18.782 us |  0.38 |        NA |
// | UseVectorsX2AArgs | 2048  |  1,057.32 us | 16.454 us | 15.391 us |  0.38 |        NA |
// | UseVectorsX2BArgs | 2048  |  1,012.21 us | 13.793 us | 12.227 us |  0.36 |        NA |
// | ImageshopSse      | 2048  |  1,742.22 us | 15.396 us | 14.401 us |  0.63 |   1,308 B |
// |                   |       |              |           |           |       |           |
// | Scalar            | 4096  | 11,051.36 us | 86.964 us | 77.092 us |  1.00 |        NA |
// | UseVectors        | 4096  |  4,408.84 us | 48.341 us | 45.218 us |  0.40 |        NA |
// | UseVectorsArgs    | 4096  |  4,330.39 us | 39.934 us | 35.401 us |  0.39 |        NA |
// | UseVectorsX2AArgs | 4096  |  4,336.47 us | 48.908 us | 45.748 us |  0.39 |        NA |
// | UseVectorsX2BArgs | 4096  |  4,083.04 us | 72.525 us | 67.840 us |  0.37 |        NA |
// | ImageshopSse      | 4096  |  5,692.53 us | 53.488 us | 50.032 us |  0.52 |   1,311 B |

// -- `.NET Framework` on X86
// Vectors.Instance:       VectorTraits256Base     //
// YShuffleX3Kernel_AcceleratedTypes:      None
// 
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.26100.3476)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
//   [Host]     : .NET Framework 4.8.1 (4.8.9290.0), X64 RyuJIT VectorSize=256
//   DefaultJob : .NET Framework 4.8.1 (4.8.9290.0), X64 RyuJIT VectorSize=256
// 
// 
// | Method            | Width | Mean       | Error     | StdDev    | Ratio | RatioSD | Code Size |
// |------------------ |------ |-----------:|----------:|----------:|------:|--------:|----------:|
// | Scalar            | 1024  |   1.033 ms | 0.0177 ms | 0.0165 ms |  1.00 |    0.02 |   2,717 B |
// | UseVectors        | 1024  |   6.161 ms | 0.0461 ms | 0.0409 ms |  5.96 |    0.10 |   4,883 B |
// | UseVectorsArgs    | 1024  |   6.089 ms | 0.1066 ms | 0.0998 ms |  5.89 |    0.13 |   4,928 B |
// | UseVectorsX2AArgs | 1024  |   6.349 ms | 0.0531 ms | 0.0497 ms |  6.15 |    0.11 |   5,288 B |
// | UseVectorsX2BArgs | 1024  |   6.512 ms | 0.1288 ms | 0.1205 ms |  6.30 |    0.15 |   4,794 B |
// |                   |       |            |           |           |       |         |           |
// | Scalar            | 2048  |   4.284 ms | 0.0539 ms | 0.0504 ms |  1.00 |    0.02 |   2,717 B |
// | UseVectors        | 2048  |  23.636 ms | 0.3372 ms | 0.3155 ms |  5.52 |    0.09 |   4,883 B |
// | UseVectorsArgs    | 2048  |  23.650 ms | 0.2341 ms | 0.2190 ms |  5.52 |    0.08 |   4,928 B |
// | UseVectorsX2AArgs | 2048  |  25.062 ms | 0.3512 ms | 0.3113 ms |  5.85 |    0.10 |   5,288 B |
// | UseVectorsX2BArgs | 2048  |  25.362 ms | 0.3052 ms | 0.2706 ms |  5.92 |    0.09 |   4,794 B |
// |                   |       |            |           |           |       |         |           |
// | Scalar            | 4096  |  16.291 ms | 0.2417 ms | 0.2261 ms |  1.00 |    0.02 |   2,717 B |
// | UseVectors        | 4096  |  94.486 ms | 1.5107 ms | 1.4131 ms |  5.80 |    0.11 |   4,883 B |
// | UseVectorsArgs    | 4096  |  93.715 ms | 0.8965 ms | 0.7486 ms |  5.75 |    0.09 |   4,928 B |
// | UseVectorsX2AArgs | 4096  |  99.979 ms | 1.9541 ms | 1.9192 ms |  6.14 |    0.14 |   5,288 B |
// | UseVectorsX2BArgs | 4096  | 101.354 ms | 1.6959 ms | 1.5864 ms |  6.22 |    0.13 |   4,794 B |
