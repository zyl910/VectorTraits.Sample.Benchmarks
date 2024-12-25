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
    /// Converte Bgr24 color bitmap to Gray8 grayscale bitmap (将Bgr24彩色位图转为Gray8灰度位图). Why SIMD only improves performance by only a little bit for RGB to Grayscale, with SIMD multiply but scalar add of vector elements? https://stackoverflow.com/questions/77603639/why-simd-only-improves-performance-by-only-a-little-bit-for-rgb-to-grayscale-wi
    /// </summary>
    public class Bgr24ToGray8Benchmark : IDisposable {
        private bool _disposed = false;
        private static readonly Random _random = new Random(1);
        private BitmapData _sourceBitmapData = null;
        private BitmapData _destinationBitmapData = null;
        private BitmapData _expectedBitmapData = null;

        [Params(1024, 2048, 4096)]
        public int Width { get; set; }
        public int Height { get; set; }

        ~Bgr24ToGray8Benchmark() {
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
            _destinationBitmapData = AllocBitmapData(Width, Height, PixelFormat.Format8bppIndexed);
            _expectedBitmapData = AllocBitmapData(Width, Height, PixelFormat.Format8bppIndexed);
            RandomFillBitmapData(_sourceBitmapData, _random);

            // Check.
            bool allowCheck = true;
            if (allowCheck) {
                TextWriter writer = Console.Out;
                try {
                    long totalDifference, countByteDifference;
                    int maxDifference;
                    double averageDifference;
                    long totalByte = Width * Height;
                    double percentDifference;
                    // Baseline
                    ScalarDo(_sourceBitmapData, _expectedBitmapData);
#if NETCOREAPP3_0_OR_GREATER
                    // UseVector128s
                    UseVector128s();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVector128s: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
#endif // NETCOREAPP3_0_OR_GREATER
#if NET8_0_OR_GREATER
                    // UseVector512s
                    try {
                        UseVector512s();
                        totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                        averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                        percentDifference = 100.0 * countByteDifference / totalByte;
                        writer.WriteLine(string.Format("Difference of UseVector512s: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    } catch (Exception ex1) {
                        writer.WriteLine("UseVector512s: " + ex1.ToString());
                    }
#endif // NET8_0_OR_GREATER
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
                    // UseVectorsX2
                    UseVectorsX2();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectorsX2: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // UseVectorsX2Parallel
                    UseVectorsX2Parallel();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectorsX2Parallel: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // PeterParallelScalar
                    PeterParallelScalar();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of PeterParallelScalar: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
#if NETCOREAPP3_0_OR_GREATER
                    // PeterParallelScalar
                    try {
                        PeterParallelSimd();
                        totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                        averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                        percentDifference = 100.0 * countByteDifference / totalByte;
                        writer.WriteLine(string.Format("Difference of PeterParallelSimd: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    } catch (Exception ex1) {
                        writer.WriteLine("PeterParallelSimd: " + ex1.ToString());
                    }
                    // RGB2Y_Sse
                    try {
                        RGB2Y_Sse();
                        totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                        averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                        percentDifference = 100.0 * countByteDifference / totalByte;
                        writer.WriteLine(string.Format("Difference of RGB2Y_Sse: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    } catch (Exception ex1) {
                        writer.WriteLine("RGB2Y_Sse: " + ex1.ToString());
                    }
                    // RGB2Y_Avx
                    try {
                        RGB2Y_Avx();
                        totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                        averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                        percentDifference = 100.0 * countByteDifference / totalByte;
                        writer.WriteLine(string.Format("Difference of RGB2Y_Avx: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    } catch (Exception ex1) {
                        writer.WriteLine("RGB2Y_Avx: " + ex1.ToString());
                    }
#endif // NETCOREAPP3_0_OR_GREATER
                } catch (Exception ex) {
                    writer.WriteLine(ex.ToString());
                }
            }
            // Debug break.
            //bool allowDebugBreak = true;
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
            const int cbPixel = 1; // Gray8
            long totalDifference = 0;
            countByteDifference = 0;
            maxDifference = 0;
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
                            if (maxDifference < difference) maxDifference = difference;
                        }
                        ++p;
                        ++q;
                    }
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
            return totalDifference;
        }

        [Benchmark(Baseline = true)]
        public void Scalar() {
            ScalarDo(_sourceBitmapData, _destinationBitmapData);
        }

        public static unsafe void ScalarDo(BitmapData src, BitmapData dst) {
            const int cbPixel = 3; // Bgr24
            const int shiftPoint = 16;
            const int mulPoint = 1 << shiftPoint; // 0x10000
            const int mulRed = (int)(0.299 * mulPoint + 0.5); // 19595
            const int mulGreen = (int)(0.587 * mulPoint + 0.5); // 38470
            const int mulBlue = mulPoint - mulRed - mulGreen; // 7471
            int width = src.Width;
            int height = src.Height;
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pRow = (byte*)src.Scan0.ToPointer();
            byte* qRow = (byte*)dst.Scan0.ToPointer();
            for (int i = 0; i < height; i++) {
                byte* p = pRow;
                byte* q = qRow;
                for (int j = 0; j < width; j++) {
                    *q = (byte)((p[2] * mulRed + p[1] * mulGreen + p[0] * mulBlue) >> shiftPoint);
                    p += cbPixel; // Bgr24
                    q += 1; // Gray8
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

#if NETCOREAPP3_0_OR_GREATER

        [Benchmark]
        public void UseVector128s() {
            UseVector128sDo(_sourceBitmapData, _destinationBitmapData, false);
        }

        // [Benchmark]
        public void UseVector128sParallel() {
            UseVector128sDo(_sourceBitmapData, _destinationBitmapData, true);
        }

        public static unsafe void UseVector128sDo(BitmapData src, BitmapData dst, bool useParallel = false) {
            int vectorWidth = Vector128<byte>.Count;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth) {
                ScalarDo(src, dst);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            int processorCount = Environment.ProcessorCount;
            int batchSize = height / (processorCount * 2);
            bool allowParallel = useParallel && (batchSize > 0) && (processorCount > 1);
            if (allowParallel) {
                int batchCount = (height + batchSize - 1) / batchSize; // ceil((double)length / batchSize)
                Parallel.For(0, batchCount, i => {
                    int start = batchSize * i;
                    int len = batchSize;
                    if (start + len > height) len = height - start;
                    byte* pSrc2 = pSrc + start * strideSrc;
                    byte* pDst2 = pDst + start * strideDst;
                    UseVector128sDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                UseVector128sDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void UseVector128sDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            const int cbPixel = 3; // Bgr24
            const int shiftPoint = 8;
            const int mulPoint = 1 << shiftPoint; // 0x100
            const ushort mulRed = (ushort)(0.299 * mulPoint + 0.5); // 77
            const ushort mulGreen = (ushort)(0.587 * mulPoint + 0.5); // 150
            const ushort mulBlue = mulPoint - mulRed - mulGreen; // 29
            Vector128<ushort> vmulRed = Vector128.Create((ushort)mulRed);
            Vector128<ushort> vmulGreen = Vector128.Create((ushort)mulGreen);
            Vector128<ushort> vmulBlue = Vector128.Create((ushort)mulBlue);
            int Vector128Width = Vector128<byte>.Count;
            int maxX = width - Vector128Width;
            byte* pRow = pSrc;
            byte* qRow = pDst;
            for (int i = 0; i < height; i++) {
                Vector128<byte>* pLast = (Vector128<byte>*)(pRow + maxX * cbPixel);
                Vector128<byte>* qLast = (Vector128<byte>*)(qRow + maxX * 1);
                Vector128<byte>* p = (Vector128<byte>*)pRow;
                Vector128<byte>* q = (Vector128<byte>*)qRow;
                for (; ; ) {
                    Vector128<byte> r, g, b, gray;
                    Vector128<ushort> wr0, wr1, wg0, wg1, wb0, wb1;
                    // Load.
                    b = Vector128s.YGroup3Unzip(p[0], p[1], p[2], out g, out r);
                    // widen(r) * mulRed + widen(g) * mulGreen + widen(b) * mulBlue
                    Vector128s.Widen(r, out wr0, out wr1);
                    Vector128s.Widen(g, out wg0, out wg1);
                    Vector128s.Widen(b, out wb0, out wb1);
                    wr0 = Vector128s.Multiply(wr0, vmulRed);
                    wr1 = Vector128s.Multiply(wr1, vmulRed);
                    wg0 = Vector128s.Multiply(wg0, vmulGreen);
                    wg1 = Vector128s.Multiply(wg1, vmulGreen);
                    wb0 = Vector128s.Multiply(wb0, vmulBlue);
                    wb1 = Vector128s.Multiply(wb1, vmulBlue);
                    wr0 = Vector128s.Add(wr0, wg0);
                    wr1 = Vector128s.Add(wr1, wg1);
                    wr0 = Vector128s.Add(wr0, wb0);
                    wr1 = Vector128s.Add(wr1, wb1);
                    // Shift right and narrow.
                    wr0 = Vector128s.ShiftRightLogical_Const(wr0, shiftPoint);
                    wr1 = Vector128s.ShiftRightLogical_Const(wr1, shiftPoint);
                    gray = Vector128s.Narrow(wr0, wr1);
                    // Store.
                    *q = gray;
                    // Next.
                    if (p >= pLast) break;
                    p += cbPixel;
                    ++q;
                    if (p > pLast) p = pLast; // The last block is also use vector.
                    if (q > qLast) q = qLast;
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

#endif // NETCOREAPP3_0_OR_GREATER

#if NET8_0_OR_GREATER

        [Benchmark]
        public void UseVector512s() {
            UseVector512sDo(_sourceBitmapData, _destinationBitmapData, false);
        }

        // [Benchmark]
        public void UseVector512sParallel() {
            UseVector512sDo(_sourceBitmapData, _destinationBitmapData, true);
        }

        public static unsafe void UseVector512sDo(BitmapData src, BitmapData dst, bool useParallel = false) {
            if (!Vector512s.IsHardwareAccelerated) throw new NotSupportedException("Vector512 does not have hardware acceleration!");
            int vectorWidth = Vector512<byte>.Count;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth) {
                ScalarDo(src, dst);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            int processorCount = Environment.ProcessorCount;
            int batchSize = height / (processorCount * 2);
            bool allowParallel = useParallel && (batchSize > 0) && (processorCount > 1);
            if (allowParallel) {
                int batchCount = (height + batchSize - 1) / batchSize; // ceil((double)length / batchSize)
                Parallel.For(0, batchCount, i => {
                    int start = batchSize * i;
                    int len = batchSize;
                    if (start + len > height) len = height - start;
                    byte* pSrc2 = pSrc + start * strideSrc;
                    byte* pDst2 = pDst + start * strideDst;
                    UseVector512sDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                UseVector512sDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void UseVector512sDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            const int cbPixel = 3; // Bgr24
            const int shiftPoint = 8;
            const int mulPoint = 1 << shiftPoint; // 0x100
            const ushort mulRed = (ushort)(0.299 * mulPoint + 0.5); // 77
            const ushort mulGreen = (ushort)(0.587 * mulPoint + 0.5); // 150
            const ushort mulBlue = mulPoint - mulRed - mulGreen; // 29
            Vector512<ushort> vmulRed = Vector512.Create((ushort)mulRed);
            Vector512<ushort> vmulGreen = Vector512.Create((ushort)mulGreen);
            Vector512<ushort> vmulBlue = Vector512.Create((ushort)mulBlue);
            int Vector512Width = Vector512<byte>.Count;
            int maxX = width - Vector512Width;
            byte* pRow = pSrc;
            byte* qRow = pDst;
            for (int i = 0; i < height; i++) {
                Vector512<byte>* pLast = (Vector512<byte>*)(pRow + maxX * cbPixel);
                Vector512<byte>* qLast = (Vector512<byte>*)(qRow + maxX * 1);
                Vector512<byte>* p = (Vector512<byte>*)pRow;
                Vector512<byte>* q = (Vector512<byte>*)qRow;
                for (; ; ) {
                    Vector512<byte> r, g, b, gray;
                    Vector512<ushort> wr0, wr1, wg0, wg1, wb0, wb1;
                    // Load.
                    b = Vector512s.YGroup3Unzip(p[0], p[1], p[2], out g, out r);
                    // widen(r) * mulRed + widen(g) * mulGreen + widen(b) * mulBlue
                    Vector512s.Widen(r, out wr0, out wr1);
                    Vector512s.Widen(g, out wg0, out wg1);
                    Vector512s.Widen(b, out wb0, out wb1);
                    wr0 = Vector512s.Multiply(wr0, vmulRed);
                    wr1 = Vector512s.Multiply(wr1, vmulRed);
                    wg0 = Vector512s.Multiply(wg0, vmulGreen);
                    wg1 = Vector512s.Multiply(wg1, vmulGreen);
                    wb0 = Vector512s.Multiply(wb0, vmulBlue);
                    wb1 = Vector512s.Multiply(wb1, vmulBlue);
                    wr0 = Vector512s.Add(wr0, wg0);
                    wr1 = Vector512s.Add(wr1, wg1);
                    wr0 = Vector512s.Add(wr0, wb0);
                    wr1 = Vector512s.Add(wr1, wb1);
                    // Shift right and narrow.
                    wr0 = Vector512s.ShiftRightLogical_Const(wr0, shiftPoint);
                    wr1 = Vector512s.ShiftRightLogical_Const(wr1, shiftPoint);
                    gray = Vector512s.Narrow(wr0, wr1);
                    // Store.
                    *q = gray;
                    // Next.
                    if (p >= pLast) break;
                    p += cbPixel;
                    ++q;
                    if (p > pLast) p = pLast; // The last block is also use vector.
                    if (q > qLast) q = qLast;
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

#endif // NET8_0_OR_GREATER

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
                ScalarDo(src, dst);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            int processorCount = Environment.ProcessorCount;
            int batchSize = height / (processorCount * 2);
            bool allowParallel = useParallel && (batchSize > 0) && (processorCount > 1);
            if (allowParallel) {
                int batchCount = (height + batchSize - 1) / batchSize; // ceil((double)length / batchSize)
                Parallel.For(0, batchCount, i => {
                    int start = batchSize * i;
                    int len = batchSize;
                    if (start + len > height) len = height - start;
                    byte* pSrc2 = pSrc + start * strideSrc;
                    byte* pDst2 = pDst + start * strideDst;
                    UseVectorsDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                UseVectorsDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void UseVectorsDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            const int cbPixel = 3; // Bgr24
            const int shiftPoint = 8;
            const int mulPoint = 1 << shiftPoint; // 0x100
            const ushort mulRed = (ushort)(0.299 * mulPoint + 0.5); // 77
            const ushort mulGreen = (ushort)(0.587 * mulPoint + 0.5); // 150
            const ushort mulBlue = mulPoint - mulRed - mulGreen; // 29
            Vector<ushort> vmulRed = new Vector<ushort>(mulRed);
            Vector<ushort> vmulGreen = new Vector<ushort>(mulGreen);
            Vector<ushort> vmulBlue = new Vector<ushort>(mulBlue);
            int vectorWidth = Vector<byte>.Count;
            int maxX = width - vectorWidth;
            byte* pRow = pSrc;
            byte* qRow = pDst;
            for (int i = 0; i < height; i++) {
                Vector<byte>* pLast = (Vector<byte>*)(pRow + maxX * cbPixel);
                Vector<byte>* qLast = (Vector<byte>*)(qRow + maxX * 1);
                Vector<byte>* p = (Vector<byte>*)pRow;
                Vector<byte>* q = (Vector<byte>*)qRow;
                for (; ; ) {
                    Vector<byte> r, g, b, gray;
                    Vector<ushort> wr0, wr1, wg0, wg1, wb0, wb1;
                    // Load.
                    b = Vectors.YGroup3Unzip(p[0], p[1], p[2], out g, out r);
                    // widen(r) * mulRed + widen(g) * mulGreen + widen(b) * mulBlue
                    Vector.Widen(r, out wr0, out wr1);
                    Vector.Widen(g, out wg0, out wg1);
                    Vector.Widen(b, out wb0, out wb1);
                    wr0 = Vectors.Multiply(wr0, vmulRed);
                    wr1 = Vectors.Multiply(wr1, vmulRed);
                    wg0 = Vectors.Multiply(wg0, vmulGreen);
                    wg1 = Vectors.Multiply(wg1, vmulGreen);
                    wb0 = Vectors.Multiply(wb0, vmulBlue);
                    wb1 = Vectors.Multiply(wb1, vmulBlue);
                    wr0 = Vector.Add(wr0, wg0);
                    wr1 = Vector.Add(wr1, wg1);
                    wr0 = Vector.Add(wr0, wb0);
                    wr1 = Vector.Add(wr1, wb1);
                    // Shift right and narrow.
                    wr0 = Vectors.ShiftRightLogical_Const(wr0, shiftPoint);
                    wr1 = Vectors.ShiftRightLogical_Const(wr1, shiftPoint);
                    gray = Vector.Narrow(wr0, wr1);
                    // Store.
                    *q = gray;
                    // Next.
                    if (p >= pLast) break;
                    p += cbPixel;
                    ++q;
                    if (p > pLast) p = pLast; // The last block is also use vector.
                    if (q > qLast) q = qLast;
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

        [Benchmark]
        public void UseVectorsX2() {
            UseVectorsX2Do(_sourceBitmapData, _destinationBitmapData, false);
        }

        // [Benchmark]
        public void UseVectorsX2Parallel() {
            UseVectorsX2Do(_sourceBitmapData, _destinationBitmapData, true);
        }

        public static unsafe void UseVectorsX2Do(BitmapData src, BitmapData dst, bool useParallel = false) {
            int vectorWidth = Vector<byte>.Count;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth * 2) {
                UseVectorsDo(src, dst);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            int processorCount = Environment.ProcessorCount;
            int batchSize = height / (processorCount * 2);
            bool allowParallel = useParallel && (batchSize > 0) && (processorCount > 1);
            if (allowParallel) {
                int batchCount = (height + batchSize - 1) / batchSize; // ceil((double)length / batchSize)
                Parallel.For(0, batchCount, i => {
                    int start = batchSize * i;
                    int len = batchSize;
                    if (start + len > height) len = height - start;
                    byte* pSrc2 = pSrc + start * strideSrc;
                    byte* pDst2 = pDst + start * strideDst;
                    UseVectorsX2DoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                UseVectorsX2DoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void UseVectorsX2DoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            const int vectorInBlock = 2;
            const int cbPixel = 3; // Bgr24
            const int shiftPoint = 8;
            const int mulPoint = 1 << shiftPoint; // 0x100
            const ushort mulRed = (ushort)(0.299 * mulPoint + 0.5); // 77
            const ushort mulGreen = (ushort)(0.587 * mulPoint + 0.5); // 150
            const ushort mulBlue = mulPoint - mulRed - mulGreen; // 29
            Vector<ushort> vmulRed = new Vector<ushort>(mulRed);
            Vector<ushort> vmulGreen = new Vector<ushort>(mulGreen);
            Vector<ushort> vmulBlue = new Vector<ushort>(mulBlue);
            int vectorWidth = Vector<byte>.Count;
            int blockWidth = vectorWidth * vectorInBlock;
            int maxX = width - blockWidth;
            byte* pRow = pSrc;
            byte* qRow = pDst;
            for (int i = 0; i < height; i++) {
                Vector<byte>* pLast = (Vector<byte>*)(pRow + maxX * cbPixel);
                Vector<byte>* qLast = (Vector<byte>*)(qRow + maxX * 1);
                Vector<byte>* p = (Vector<byte>*)pRow;
                Vector<byte>* q = (Vector<byte>*)qRow;
                for (; ; ) {
                    Vector<byte> r0, r1, g0, g1, b0, b1, gray0, gray1;
                    Vector<ushort> wr0, wr1, wr2, wr3, wg0, wg1, wg2, wg3, wb0, wb1, wb2, wb3;
                    // Load.
                    b0 = Vectors.YGroup3UnzipX2(p[0], p[1], p[2], p[3], p[4], p[5], out b1, out g0, out g1, out r0, out r1);
                    // widen(r) * mulRed + widen(g) * mulGreen + widen(b) * mulBlue
                    Vector.Widen(r0, out wr0, out wr1);
                    Vector.Widen(r1, out wr2, out wr3);
                    Vector.Widen(g0, out wg0, out wg1);
                    Vector.Widen(g1, out wg2, out wg3);
                    Vector.Widen(b0, out wb0, out wb1);
                    Vector.Widen(b1, out wb2, out wb3);
                    wr0 = Vectors.Multiply(wr0, vmulRed);
                    wr1 = Vectors.Multiply(wr1, vmulRed);
                    wr2 = Vectors.Multiply(wr2, vmulRed);
                    wr3 = Vectors.Multiply(wr3, vmulRed);
                    wg0 = Vectors.Multiply(wg0, vmulGreen);
                    wg1 = Vectors.Multiply(wg1, vmulGreen);
                    wg2 = Vectors.Multiply(wg2, vmulGreen);
                    wg3 = Vectors.Multiply(wg3, vmulGreen);
                    wb0 = Vectors.Multiply(wb0, vmulBlue);
                    wb1 = Vectors.Multiply(wb1, vmulBlue);
                    wb2 = Vectors.Multiply(wb2, vmulBlue);
                    wb3 = Vectors.Multiply(wb3, vmulBlue);
                    wr0 = Vector.Add(wr0, wg0);
                    wr1 = Vector.Add(wr1, wg1);
                    wr2 = Vector.Add(wr2, wg2);
                    wr3 = Vector.Add(wr3, wg3);
                    wr0 = Vector.Add(wr0, wb0);
                    wr1 = Vector.Add(wr1, wb1);
                    wr2 = Vector.Add(wr2, wb2);
                    wr3 = Vector.Add(wr3, wb3);
                    // Shift right and narrow.
                    wr0 = Vectors.ShiftRightLogical_Const(wr0, shiftPoint);
                    wr1 = Vectors.ShiftRightLogical_Const(wr1, shiftPoint);
                    wr2 = Vectors.ShiftRightLogical_Const(wr2, shiftPoint);
                    wr3 = Vectors.ShiftRightLogical_Const(wr3, shiftPoint);
                    gray0 = Vector.Narrow(wr0, wr1);
                    gray1 = Vector.Narrow(wr2, wr3);
                    // Store.
                    q[0] = gray0;
                    q[1] = gray1;
                    // Next.
                    if (p >= pLast) break;
                    p += vectorInBlock * cbPixel;
                    q += vectorInBlock;
                    if (p > pLast) p = pLast; // The last block is also use vector.
                    if (q > qLast) q = qLast;
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }


        // == From Peter Cordes. https://stackoverflow.com/questions/77603639/why-simd-only-improves-performance-by-only-a-little-bit-for-rgb-to-grayscale-wi

        [Benchmark]
        public void PeterParallelScalar() {
            Peter.GrayViaParallel(_sourceBitmapData, _destinationBitmapData);
        }

#if NETCOREAPP3_0_OR_GREATER
        [Benchmark]
        public unsafe void PeterParallelSimd() {
            if (!Sse2.IsSupported) throw new NotSupportedException("Not support X86's Sse2!");
            var org = _sourceBitmapData;
            var des = _destinationBitmapData;
            int width = org.Width;
            int height = org.Height;

            var orgp = (byte*)org.Scan0.ToPointer();
            var desp = (byte*)des.Scan0.ToPointer();

            for(int i = 0; i<height; ++i) {
                int orgSd = i * org.Stride;
                int desSd = i * des.Stride;
                Peter.GrayViaParallelAndSIMD(orgp + orgSd, desp + desSd, width);
            }
        }
#endif // NETCOREAPP3_0_OR_GREATER

        /// <summary>
        /// From Peter Cordes. https://stackoverflow.com/questions/77603639/why-simd-only-improves-performance-by-only-a-little-bit-for-rgb-to-grayscale-wi
        /// </summary>
        static class Peter {

            public static unsafe void GrayViaParallel(BitmapData org, BitmapData des) {
                int width = org.Width;
                int height = org.Height;

                var orgp = (byte*)org.Scan0.ToPointer();
                var desp = (byte*)des.Scan0.ToPointer();

                Parallel.For(0, height, i =>
                {
                    int orgSd = i * org.Stride;
                    int desSd = i * des.Stride;
                    for (int j = 0; j < width; j++) {
                        //                              Red                     Green                  Blue
                        desp[desSd] = (byte)((orgp[orgSd + 2] * 19595 + orgp[orgSd + 1] * 38469 + orgp[orgSd] * 7472) >> 16);
                        desSd++;
                        orgSd += 3;
                    }
                });
            }

#if NETCOREAPP3_0_OR_GREATER
            public static unsafe void GrayViaParallelAndSIMD(byte* src, byte* dst, int count) {
                const ushort mulBlue = (ushort)(0.114 * 0x10000); const ushort mulGreen = (ushort)(0.587 * 0x10000); const ushort mulRed = (ushort)(0.299 * 0x10000);
                var Coeleft = Vector128.Create(mulBlue, mulGreen, mulRed, mulBlue, mulGreen, mulRed, mulBlue, mulGreen);
                var CoeRight = Vector128.Create(mulRed, mulBlue, mulGreen, mulRed, mulBlue, mulGreen, mulRed, 0);

                int allPixels = count * 3;
                byte* srcEnd = src + allPixels; //Is it wrong?
                int stride = 15; //Proceed 15 bytes per step
                int loopCount = (int)((srcEnd - src) / stride);

                Parallel.For(0, loopCount, i =>
                {
                    int curPos = (i + 1) * stride;
                    if (curPos < allPixels) //If not added,  it will exceed the image data
                    {
                        // Load the first 16 bytes of the pixels
                        var _1st16bytes = Sse2.LoadVector128(src + i * stride);

                        // Get the first 8 bytes
                        var low = Sse2.UnpackLow(_1st16bytes, Vector128<byte>.Zero).AsUInt16();
                        //Get the next 8 bytes
                        var high = Sse2.UnpackHigh(_1st16bytes, Vector128<byte>.Zero).AsUInt16();

                        // Calculate the first 8 bytes
                        var lowMul = Sse2.MultiplyHigh(Coeleft, low);
                        // Calculate the next 8 bytes
                        var highMul = Sse2.MultiplyHigh(CoeRight, high);

                        //               Blue                     Green                   Red
                        var px1 = lowMul.GetElement(0) + lowMul.GetElement(1) + lowMul.GetElement(2);
                        var px2 = lowMul.GetElement(3) + lowMul.GetElement(4) + lowMul.GetElement(5);
                        var px3 = lowMul.GetElement(6) + lowMul.GetElement(7) + highMul.GetElement(0);
                        var px4 = highMul.GetElement(1) + highMul.GetElement(2) + highMul.GetElement(3);
                        var px5 = highMul.GetElement(4) + highMul.GetElement(5) + highMul.GetElement(6);

                        //15 bytes for 5 pixels 
                        var i5 = i * 5;

                        dst[i5] = (byte)px1;
                        dst[i5 + 1] = (byte)px2;
                        dst[i5 + 2] = (byte)px3;
                        dst[i5 + 3] = (byte)px4;
                        dst[i5 + 4] = (byte)px5;
                    }
                });
            }
#endif // NETCOREAPP3_0_OR_GREATER

        }

        // == From komrad36. https://github.com/komrad36/RGB2Y

#if NETCOREAPP3_0_OR_GREATER

        [Benchmark]
        public unsafe void RGB2Y_Sse() {
            if (!Sse2.IsSupported) throw new NotSupportedException("Not support X86's Sse2!");
            int vectorWidth = Vector<byte>.Count;
            var src = _sourceBitmapData;
            var dst = _destinationBitmapData;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth) {
                ScalarDo(src, dst);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            RGB2Y.RGB2Y_Sse(pSrc, pDst, width, height, strideSrc, strideDst);
        }

        [Benchmark]
        public unsafe void RGB2Y_Avx() {
            if (!Sse2.IsSupported || !Avx2.IsSupported) throw new NotSupportedException("Not support X86's Sse2, Avx2!");
            int vectorWidth = Vector<byte>.Count;
            var src = _sourceBitmapData;
            var dst = _destinationBitmapData;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth) {
                ScalarDo(src, dst);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            RGB2Y.RGB2Y_Avx(pSrc, width, height, strideSrc, pDst, strideDst);
        }

        /// <summary>
        /// From komrad36 - RGB2Y. https://github.com/komrad36/RGB2Y
        /// </summary>
        static class RGB2Y {
            // -- SSE2
            // void RGB2Y_Sse(unsigned char *Src, unsigned char *Dest, int Width, int Height, int Stride) {
            //     const int B_WT = int(0.114 * 256 + 0.5);
            //     const int G_WT = int(0.587 * 256 + 0.5);
            //     const int R_WT = 256 - B_WT - G_WT; // int(0.299 * 256 + 0.5)
            //     for (int Y = 0; Y < Height; Y++) {
            //         unsigned char *LinePS = Src + Y * Stride;
            //         unsigned char *LinePD = Dest + Y * Width;
            //         int X = 0;
            //         for (; X < Width - 12; X += 12, LinePS += 36) {
            //             __m128i p1aL = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 0))), _mm_setr_epi16(B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT)); //1
            //             __m128i p2aL = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 1))), _mm_setr_epi16(G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT)); //2
            //             __m128i p3aL = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 2))), _mm_setr_epi16(R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT)); //3
            //             __m128i p1aH = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 8))), _mm_setr_epi16(R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT));
            //             __m128i p2aH = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 9))), _mm_setr_epi16(B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT));
            //             __m128i p3aH = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 10))), _mm_setr_epi16(G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT));
            //             __m128i p1bL = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 18))), _mm_setr_epi16(B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT));
            //             __m128i p2bL = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 19))), _mm_setr_epi16(G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT));
            //             __m128i p3bL = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 20))), _mm_setr_epi16(R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT));
            //             __m128i p1bH = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 26))), _mm_setr_epi16(R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT));
            //             __m128i p2bH = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 27))), _mm_setr_epi16(B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT));
            //             __m128i p3bH = _mm_mullo_epi16(_mm_cvtepu8_epi16(_mm_loadu_si128((__m128i *)(LinePS + 28))), _mm_setr_epi16(G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT));
            //             __m128i sumaL = _mm_add_epi16(p3aL, _mm_add_epi16(p1aL, p2aL));
            //             __m128i sumaH = _mm_add_epi16(p3aH, _mm_add_epi16(p1aH, p2aH));
            //             __m128i sumbL = _mm_add_epi16(p3bL, _mm_add_epi16(p1bL, p2bL));
            //             __m128i sumbH = _mm_add_epi16(p3bH, _mm_add_epi16(p1bH, p2bH));
            //             __m128i sclaL = _mm_srli_epi16(sumaL, 8);
            //             __m128i sclaH = _mm_srli_epi16(sumaH, 8);
            //             __m128i sclbL = _mm_srli_epi16(sumbL, 8);
            //             __m128i sclbH = _mm_srli_epi16(sumbH, 8);
            //             __m128i shftaL = _mm_shuffle_epi8(sclaL, _mm_setr_epi8(0, 6, 12, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1));
            //             __m128i shftaH = _mm_shuffle_epi8(sclaH, _mm_setr_epi8(-1, -1, -1, 18, 24, 30, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1));
            //             __m128i shftbL = _mm_shuffle_epi8(sclbL, _mm_setr_epi8(-1, -1, -1, -1, -1, -1, 0, 6, 12, -1, -1, -1, -1, -1, -1, -1));
            //             __m128i shftbH = _mm_shuffle_epi8(sclbH, _mm_setr_epi8(-1, -1, -1, -1, -1, -1, -1, -1, -1, 18, 24, 30, -1, -1, -1, -1));
            //             __m128i accumL = _mm_or_si128(shftaL, shftbL);
            //             __m128i accumH = _mm_or_si128(shftaH, shftbH);
            //             __m128i h3 = _mm_or_si128(accumL, accumH);
            //             //__m128i h3 = _mm_blendv_epi8(accumL, accumH, _mm_setr_epi8(0, 0, 0, -1, -1, -1, 0, 0, 0, -1, -1, -1, 1, 1, 1, 1));
            //             _mm_storeu_si128((__m128i *)(LinePD + X), h3);
            //         }
            //         for (; X < Width; X++, LinePS += 3) {
            //             LinePD[X] = (B_WT * LinePS[0] + G_WT * LinePS[1] + R_WT * LinePS[2]) >> 8;
            //         }
            //     }
            // }

            public static unsafe void RGB2Y_Sse(byte* Src, byte* Dest, int Width, int Height, int Stride, int strideDst) {
                const int B_WT = (int)(0.114 * 256 + 0.5);
                const int G_WT = (int)(0.587 * 256 + 0.5);
                const int R_WT = 256 - B_WT - G_WT; // int(0.299 * 256 + 0.5)
                for (int Y = 0; Y < Height; Y++) {
                    byte* LinePS = Src + Y * Stride;
                    byte* LinePD = Dest + Y * strideDst;
                    int X = 0;
                    for (; X < Width - 12; X += 12, LinePS += 36) {
                        var p1aL = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 0))), Vector128.Create(B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT)); //1
                        var p2aL = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 1))), Vector128.Create(G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT)); //2
                        var p3aL = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 2))), Vector128.Create(R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT)); //3
                        var p1aH = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 8))), Vector128.Create(R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT));
                        var p2aH = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 9))), Vector128.Create(B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT));
                        var p3aH = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 10))), Vector128.Create(G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT));
                        var p1bL = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 18))), Vector128.Create(B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT));
                        var p2bL = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 19))), Vector128.Create(G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT));
                        var p3bL = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 20))), Vector128.Create(R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT));
                        var p1bH = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 26))), Vector128.Create(R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT));
                        var p2bH = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 27))), Vector128.Create(B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT));
                        var p3bH = Sse2.MultiplyLow(Sse41.ConvertToVector128Int16(Sse2.LoadVector128((LinePS + 28))), Vector128.Create(G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT));
                        var sumaL = Sse2.Add(p3aL, Sse2.Add(p1aL, p2aL));
                        var sumaH = Sse2.Add(p3aH, Sse2.Add(p1aH, p2aH));
                        var sumbL = Sse2.Add(p3bL, Sse2.Add(p1bL, p2bL));
                        var sumbH = Sse2.Add(p3bH, Sse2.Add(p1bH, p2bH));
                        var sclaL = Sse2.ShiftRightLogical(sumaL, 8);
                        var sclaH = Sse2.ShiftRightLogical(sumaH, 8);
                        var sclbL = Sse2.ShiftRightLogical(sumbL, 8);
                        var sclbH = Sse2.ShiftRightLogical(sumbH, 8);
                        var shftaL = Ssse3.Shuffle(sclaL.AsByte(), Vector128.Create(0, 6, 12, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1).AsByte());
                        var shftaH = Ssse3.Shuffle(sclaH.AsByte(), Vector128.Create(-1, -1, -1, 18, 24, 30, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1).AsByte());
                        var shftbL = Ssse3.Shuffle(sclbL.AsByte(), Vector128.Create(-1, -1, -1, -1, -1, -1, 0, 6, 12, -1, -1, -1, -1, -1, -1, -1).AsByte());
                        var shftbH = Ssse3.Shuffle(sclbH.AsByte(), Vector128.Create(-1, -1, -1, -1, -1, -1, -1, -1, -1, 18, 24, 30, -1, -1, -1, -1).AsByte());
                        var accumL = Sse2.Or(shftaL, shftbL);
                        var accumH = Sse2.Or(shftaH, shftbH);
                        var h3 = Sse2.Or(accumL, accumH);
                        //__m128i h3 = _mm_blendv_epi8(accumL, accumH, _mm_setr_epi8(0, 0, 0, -1, -1, -1, 0, 0, 0, -1, -1, -1, 1, 1, 1, 1));
                        Sse2.Store((LinePD + X), h3);
                    }
                    for (; X < Width; X++, LinePS += 3) {
                        LinePD[X] = (byte)((B_WT * LinePS[0] + G_WT * LinePS[1] + R_WT * LinePS[2]) >> 8);
                    }
                }
            }

            // -- AVX2
            // https://github.com/komrad36/RGB2Y/blob/master/RGB2Y.h // Commits on Sep 30, 2018

            // Set your weights here.
            // constexpr double B_WEIGHT = 0.114;
            // constexpr double G_WEIGHT = 0.587;
            // constexpr double R_WEIGHT = 0.299;
            const double B_WEIGHT = 0.114;
            const double G_WEIGHT = 0.587;
            const double R_WEIGHT = 0.299;

            // constexpr uint16_t B_WT = static_cast<uint16_t>(32768.0 * B_WEIGHT + 0.5);
            // constexpr uint16_t G_WT = static_cast<uint16_t>(32768.0 * G_WEIGHT + 0.5);
            // constexpr uint16_t R_WT = static_cast<uint16_t>(32768.0 * R_WEIGHT + 0.5);
            const short B_WT = (short)(32768.0 * B_WEIGHT + 0.5);
            const short G_WT = (short)(32768.0 * G_WEIGHT + 0.5);
            const short R_WT = (short)(32768.0 * R_WEIGHT + 0.5);

            // static const __m256i weight_vec = _mm256_setr_epi16(B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT);
            private static readonly Vector256<short> weight_vec = Vector256.Create(B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT, G_WT, R_WT, B_WT);

            // The weight alway use true.
            const bool weight = true;

            // template<bool last_row_and_col, bool weight>
            // void process(const uint8_t* __restrict const pt, const int32_t cols_minus_j, uint8_t* const __restrict out) {
            // 	__m128i h3;
            // 	if (weight) {
            // 		__m256i in1 = _mm256_mulhrs_epi16(_mm256_cvtepu8_epi16(_mm_loadu_si128((const __m128i*)(pt))), weight_vec);
            // 		__m256i in2 = _mm256_mulhrs_epi16(_mm256_cvtepu8_epi16(_mm_loadu_si128((const __m128i*)(pt + 15))), weight_vec);
            // 		__m256i mul = _mm256_packus_epi16(in1, in2);
            // 		__m256i b1 = _mm256_shuffle_epi8(mul, _mm256_setr_epi8(0, 3, 6, -1, -1, -1, 11, 14, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 1, 4, 7, -1, -1, 9, 12, -1, -1, -1, -1, -1, -1));
            // 		__m256i g1 = _mm256_shuffle_epi8(mul, _mm256_setr_epi8(1, 4, 7, -1, -1, 9, 12, 15, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 2, 5, -1, -1, -1, 10, 13, -1, -1, -1, -1, -1, -1));
            // 		__m256i r1 = _mm256_shuffle_epi8(mul, _mm256_setr_epi8(2, 5, -1, -1, -1, 10, 13, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0, 3, 6, -1, -1, 8, 11, 14, -1, -1, -1, -1, -1, -1));
            // 		__m256i accum = _mm256_adds_epu8(r1, _mm256_adds_epu8(b1, g1));
            // 		h3 = _mm_adds_epu8(_mm256_castsi256_si128(accum), _mm256_extracti128_si256(accum, 1));
            // 	}
            // 	else {
            // 		__m256i in1 = _mm256_castsi128_si256(_mm_loadu_si128((const __m128i*)(pt)));
            // 		in1 = _mm256_inserti128_si256(in1, _mm_loadu_si128((const __m128i*)(pt + 15)), 1);
            // 		__m256i b1 = _mm256_shuffle_epi8(in1, _mm256_setr_epi8(0, -1, 3, -1, 6, -1, 9, -1, 12, -1, -1, -1, -1, -1, -1, -1, 0, -1, 3, -1, 6, -1, 9, -1, 12, -1, -1, -1, -1, -1, -1, -1));
            // 		__m256i g1 = _mm256_shuffle_epi8(in1, _mm256_setr_epi8(1, -1, 4, -1, 7, -1, 10, -1, 13, -1, -1, -1, -1, -1, -1, -1, 1, -1, 4, -1, 7, -1, 10, -1, 13, -1, -1, -1, -1, -1, -1, -1));
            // 		__m256i r1 = _mm256_shuffle_epi8(in1, _mm256_setr_epi8(2, -1, 5, -1, 8, -1, 11, -1, 14, -1, -1, -1, -1, -1, -1, -1, 2, -1, 5, -1, 8, -1, 11, -1, 14, -1, -1, -1, -1, -1, -1, -1));
            // 		__m256i sum = _mm256_adds_epu16(r1, _mm256_adds_epu16(b1, g1));
            // 		__m256i accum = _mm256_mulhrs_epi16(sum, _mm256_set1_epi16(10923));
            // 		__m256i shuf = _mm256_shuffle_epi8(accum, _mm256_setr_epi8(0, 2, 4, 6, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0, 2, 4, 6, 8, -1, -1, -1, -1, -1, -1));
            // 		h3 = _mm_or_si128(_mm256_extracti128_si256(shuf, 1), _mm256_castsi256_si128(shuf));
            // 	}
            // 	if (last_row_and_col) {
            // 		switch (cols_minus_j) {
            // 		case 15:
            // 			out[14] = static_cast<uint8_t>(_mm_extract_epi8(h3, 14));
            // 		case 14:
            // 			out[13] = static_cast<uint8_t>(_mm_extract_epi8(h3, 13));
            // 		case 13:
            // 			out[12] = static_cast<uint8_t>(_mm_extract_epi8(h3, 12));
            // 		case 12:
            // 			out[11] = static_cast<uint8_t>(_mm_extract_epi8(h3, 11));
            // 		case 11:
            // 			out[10] = static_cast<uint8_t>(_mm_extract_epi8(h3, 10));
            // 		case 10:
            // 			out[9] = static_cast<uint8_t>(_mm_extract_epi8(h3, 9));
            // 		case 9:
            // 			out[8] = static_cast<uint8_t>(_mm_extract_epi8(h3, 8));
            // 		case 8:
            // 			out[7] = static_cast<uint8_t>(_mm_extract_epi8(h3, 7));
            // 		case 7:
            // 			out[6] = static_cast<uint8_t>(_mm_extract_epi8(h3, 6));
            // 		case 6:
            // 			out[5] = static_cast<uint8_t>(_mm_extract_epi8(h3, 5));
            // 		case 5:
            // 			out[4] = static_cast<uint8_t>(_mm_extract_epi8(h3, 4));
            // 		case 4:
            // 			out[3] = static_cast<uint8_t>(_mm_extract_epi8(h3, 3));
            // 		case 3:
            // 			out[2] = static_cast<uint8_t>(_mm_extract_epi8(h3, 2));
            // 		case 2:
            // 			out[1] = static_cast<uint8_t>(_mm_extract_epi8(h3, 1));
            // 		case 1:
            // 			out[0] = static_cast<uint8_t>(_mm_extract_epi8(h3, 0));
            // 		}
            // 	}
            // 	else {
            // 		_mm_storeu_si128(reinterpret_cast<__m128i*>(out), h3);
            // 	}
            // }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static unsafe void process(byte* pt, int cols_minus_j, byte* pout, bool last_row_and_col) {
                Vector128<byte> h3;
                if (weight) {
                    var in1 = Avx2.MultiplyHighRoundScale(Avx2.ConvertToVector256Int16(Sse2.LoadVector128((pt))), weight_vec);
                    var in2 = Avx2.MultiplyHighRoundScale(Avx2.ConvertToVector256Int16(Sse2.LoadVector128((pt + 15))), weight_vec);
                    var mul = Avx2.PackUnsignedSaturate(in1, in2);
                    var b1 = Avx2.Shuffle(mul, Vector256.Create(0, 3, 6, -1, -1, -1, 11, 14, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 1, 4, 7, -1, -1, 9, 12, -1, -1, -1, -1, -1, -1).AsByte());
                    var g1 = Avx2.Shuffle(mul, Vector256.Create(1, 4, 7, -1, -1, 9, 12, 15, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 2, 5, -1, -1, -1, 10, 13, -1, -1, -1, -1, -1, -1).AsByte());
                    var r1 = Avx2.Shuffle(mul, Vector256.Create(2, 5, -1, -1, -1, 10, 13, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0, 3, 6, -1, -1, 8, 11, 14, -1, -1, -1, -1, -1, -1).AsByte());
                    var accum = Avx2.AddSaturate(r1, Avx2.AddSaturate(b1, g1));
                    h3 = Sse2.AddSaturate(accum.GetLower(), Avx2.ExtractVector128(accum, 1));
                } else {
                    //__m256i in1 = _mm256_castsi128_si256(_mm_loadu_si128((const __m128i*)(pt)));
                    //in1 = _mm256_inserti128_si256(in1, _mm_loadu_si128((const __m128i*)(pt + 15)), 1);
                    //__m256i b1 = _mm256_shuffle_epi8(in1, _mm256_setr_epi8(0, -1, 3, -1, 6, -1, 9, -1, 12, -1, -1, -1, -1, -1, -1, -1, 0, -1, 3, -1, 6, -1, 9, -1, 12, -1, -1, -1, -1, -1, -1, -1));
                    //__m256i g1 = _mm256_shuffle_epi8(in1, _mm256_setr_epi8(1, -1, 4, -1, 7, -1, 10, -1, 13, -1, -1, -1, -1, -1, -1, -1, 1, -1, 4, -1, 7, -1, 10, -1, 13, -1, -1, -1, -1, -1, -1, -1));
                    //__m256i r1 = _mm256_shuffle_epi8(in1, _mm256_setr_epi8(2, -1, 5, -1, 8, -1, 11, -1, 14, -1, -1, -1, -1, -1, -1, -1, 2, -1, 5, -1, 8, -1, 11, -1, 14, -1, -1, -1, -1, -1, -1, -1));
                    //__m256i sum = _mm256_adds_epu16(r1, _mm256_adds_epu16(b1, g1));
                    //__m256i accum = _mm256_mulhrs_epi16(sum, _mm256_set1_epi16(10923));
                    //__m256i shuf = _mm256_shuffle_epi8(accum, _mm256_setr_epi8(0, 2, 4, 6, 8, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, 0, 2, 4, 6, 8, -1, -1, -1, -1, -1, -1));
                    //h3 = _mm_or_si128(_mm256_extracti128_si256(shuf, 1), _mm256_castsi256_si128(shuf));
                }
                if (last_row_and_col) {
                    if (0 < cols_minus_j && cols_minus_j < 16) {
                        Buffer.MemoryCopy(&h3, pout, cols_minus_j, cols_minus_j);
                    }
                } else {
                    Sse2.Store(pout, h3);
                }
            }

            // template<bool last_row, bool weight>
            // void processRow(const uint8_t* __restrict pt, const int32_t cols, uint8_t* const __restrict out) {
            // 	int j = 0;
            // 	for (; j < cols - 10; j += 10, pt += 30) {
            // 		process<false, weight>(pt, cols - j, out + j);
            // 	}
            // 	process<last_row, weight>(pt, cols - j, out + j);
            // }

            public static unsafe void processRow(byte* pt, int cols, byte* pout, bool last_row) {
	            int j = 0;
	            for (; j < cols - 10; j += 10, pt += 30) {
		            process(pt, cols - j, pout + j, false);
	            }
                process(pt, cols - j, pout + j, last_row);
                //process(pt, cols - j, pout + j, true);
                //const int B_WT = (int)(0.114 * 256 + 0.5);
                //const int G_WT = (int)(0.587 * 256 + 0.5);
                //const int R_WT = 256 - B_WT - G_WT; // int(0.299 * 256 + 0.5)
                //byte* q = pout + j;
                //for (; j < cols; j++, pt += 3, ++q) {
                //    *q = (byte)((B_WT * pt[0] + G_WT * pt[1] + R_WT * pt[2]) >> 8);
                //}
            }

            // template<bool weight>
            // void __forceinline _RGB2Y(const uint8_t* __restrict const data, const int32_t cols, const int32_t start_row, const int32_t rows, const int32_t stride, uint8_t* const __restrict out) {
            // 	int i = start_row;
            // 	for (; i < start_row + rows - 1; ++i) {
            // 		processRow<false, weight>(data + 3 * i * stride, cols, out + i * cols);
            // 	}
            // 	processRow<true, weight>(data + 3 * i * stride, cols, out + i * cols);
            // }
            public static unsafe void _RGB2Y(byte* data, int cols, int start_row, int rows, int stride, byte* pout, int strideDst) {
	            int i = start_row;
	            for (; i < (start_row + rows - 1); ++i) {
		            processRow(data + i * stride, cols, pout + i * strideDst, false);
	            }
	            processRow(data + i * stride, cols, pout + i * strideDst, true);
            }

            // template<bool multithread, bool weight>
            // void RGB2Y(const uint8_t* const __restrict image, const int width, const int height, const int stride, uint8_t* const __restrict out) {
            // 	if (multithread) {
            // 		const int32_t hw_concur = std::min(height >> 4, static_cast<int32_t>(std::thread::hardware_concurrency()));
            // 		if (hw_concur > 1) {
            // 			std::vector<std::future<void>> fut(hw_concur);
            // 			const int thread_stride = (height - 1) / hw_concur + 1;
            // 			int i = 0, start = 0;
            // 			for (; i < std::min(height - 1, hw_concur - 1); ++i, start += thread_stride) {
            // 				fut[i] = std::async(std::launch::async, _RGB2Y<weight>, image, width, start, thread_stride, stride, out);
            // 			}
            // 			fut[i] = std::async(std::launch::async, _RGB2Y<weight>, image, width, start, height - start, stride, out);
            // 			for (int j = 0; j <= i; ++j) fut[j].wait();
            // 		}
            // 		else {
            // 			_RGB2Y<weight>(image, width, 0, height, stride, out);
            // 		}
            // 	}
            // 	else {
            // 		_RGB2Y<weight>(image, width, 0, height, stride, out);
            // 	}
            // }

            public static unsafe void RGB2Y_Avx(byte* image, int width, int height, int stride, byte* pout, int strideDst) {
                _RGB2Y(image, width, 0, height, stride, pout, strideDst);
            }

        }

#endif // NETCOREAPP3_0_OR_GREATER

    }
}

// == Benchmarks result

// -- `.NET7.0` on Arm
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.1.1 (24B91) [Darwin 24.1.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 8.0.204
//   [Host]     : .NET 7.0.20 (7.0.2024.26716), Arm64 RyuJIT AdvSIMD
//   DefaultJob : .NET 7.0.20 (7.0.2024.26716), Arm64 RyuJIT AdvSIMD
// 
// 
// | Method               | Width | Mean         | Error     | StdDev    | Ratio | RatioSD |
// |--------------------- |------ |-------------:|----------:|----------:|------:|--------:|
// | Scalar               | 1024  |    628.65 us |  0.416 us |  0.325 us |  1.00 |    0.00 |
// | UseVector128s        | 1024  |    302.15 us |  3.819 us |  3.572 us |  0.48 |    0.01 |
// | UseVectors           | 1024  |    167.75 us |  0.051 us |  0.047 us |  0.27 |    0.00 |
// | UseVectorsParallel   | 1024  |     59.48 us |  0.659 us |  0.616 us |  0.09 |    0.00 |
// | UseVectorsX2         | 1024  |    170.29 us |  0.171 us |  0.143 us |  0.27 |    0.00 |
// | UseVectorsX2Parallel | 1024  |     49.12 us |  0.977 us |  1.337 us |  0.08 |    0.00 |
// | PeterParallelScalar  | 1024  |    204.72 us |  0.464 us |  0.434 us |  0.33 |    0.00 |
// | PeterParallelSimd    | 1024  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Sse            | 1024  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Avx            | 1024  |           NA |        NA |        NA |     ? |       ? |
// |                      |       |              |           |           |       |         |
// | Scalar               | 2048  |  2,561.87 us |  3.064 us |  2.866 us |  1.00 |    0.00 |
// | UseVector128s        | 2048  |  1,254.35 us |  1.404 us |  1.245 us |  0.49 |    0.00 |
// | UseVectors           | 2048  |    690.34 us |  0.403 us |  0.377 us |  0.27 |    0.00 |
// | UseVectorsParallel   | 2048  |    168.07 us |  0.557 us |  0.465 us |  0.07 |    0.00 |
// | UseVectorsX2         | 2048  |    698.10 us |  0.386 us |  0.322 us |  0.27 |    0.00 |
// | UseVectorsX2Parallel | 2048  |    152.57 us |  1.749 us |  2.970 us |  0.06 |    0.00 |
// | PeterParallelScalar  | 2048  |    720.21 us |  4.383 us |  4.099 us |  0.28 |    0.00 |
// | PeterParallelSimd    | 2048  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Sse            | 2048  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Avx            | 2048  |           NA |        NA |        NA |     ? |       ? |
// |                      |       |              |           |           |       |         |
// | Scalar               | 4096  | 10,247.63 us | 23.753 us | 19.835 us |  1.00 |    0.00 |
// | UseVector128s        | 4096  |  4,935.40 us |  4.547 us |  4.253 us |  0.48 |    0.00 |
// | UseVectors           | 4096  |  2,799.62 us | 55.078 us | 65.567 us |  0.27 |    0.01 |
// | UseVectorsParallel   | 4096  |  1,116.36 us | 14.602 us | 12.944 us |  0.11 |    0.00 |
// | UseVectorsX2         | 4096  |  2,763.65 us |  1.048 us |  0.929 us |  0.27 |    0.00 |
// | UseVectorsX2Parallel | 4096  |  1,103.35 us | 11.984 us | 10.623 us |  0.11 |    0.00 |
// | PeterParallelScalar  | 4096  |  2,793.25 us | 21.542 us | 17.989 us |  0.27 |    0.00 |
// | PeterParallelSimd    | 4096  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Sse            | 4096  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Avx            | 4096  |           NA |        NA |        NA |     ? |       ? |

// -- `.NET8.0` on Arm
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.0.1 (24A348) [Darwin 24.0.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 8.0.204
//   [Host]     : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD [AttachedDebugger]
//   DefaultJob : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD
// 
// 
// | Method               | Width | Mean         | Error     | StdDev    | Ratio | RatioSD |
// |--------------------- |------ |-------------:|----------:|----------:|------:|--------:|
// | Scalar               | 1024  |    635.31 us |  0.537 us |  0.448 us |  1.00 |    0.00 |
// | UseVector128s        | 1024  |    126.59 us |  0.492 us |  0.437 us |  0.20 |    0.00 |
// | UseVector512s        | 1024  |           NA |        NA |        NA |     ? |       ? |
// | UseVectors           | 1024  |    127.04 us |  0.567 us |  0.474 us |  0.20 |    0.00 |
// | UseVectorsParallel   | 1024  |     46.37 us |  0.336 us |  0.314 us |  0.07 |    0.00 |
// | UseVectorsX2         | 1024  |    126.82 us |  0.094 us |  0.088 us |  0.20 |    0.00 |
// | UseVectorsX2Parallel | 1024  |     41.59 us |  0.641 us |  0.600 us |  0.07 |    0.00 |
// | PeterParallelScalar  | 1024  |    202.19 us |  1.025 us |  0.959 us |  0.32 |    0.00 |
// | PeterParallelSimd    | 1024  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Sse            | 1024  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Avx            | 1024  |           NA |        NA |        NA |     ? |       ? |
// |                      |       |              |           |           |       |         |
// | Scalar               | 2048  |  2,625.64 us |  1.795 us |  1.402 us |  1.00 |    0.00 |
// | UseVector128s        | 2048  |    519.49 us |  0.218 us |  0.204 us |  0.20 |    0.00 |
// | UseVector512s        | 2048  |           NA |        NA |        NA |     ? |       ? |
// | UseVectors           | 2048  |    521.40 us |  0.301 us |  0.282 us |  0.20 |    0.00 |
// | UseVectorsParallel   | 2048  |    152.11 us |  3.548 us | 10.064 us |  0.06 |    0.00 |
// | UseVectorsX2         | 2048  |    516.46 us |  0.606 us |  0.567 us |  0.20 |    0.00 |
// | UseVectorsX2Parallel | 2048  |    179.65 us |  6.579 us | 19.400 us |  0.07 |    0.01 |
// | PeterParallelScalar  | 2048  |    711.00 us |  1.806 us |  1.601 us |  0.27 |    0.00 |
// | PeterParallelSimd    | 2048  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Sse            | 2048  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Avx            | 2048  |           NA |        NA |        NA |     ? |       ? |
// |                      |       |              |           |           |       |         |
// | Scalar               | 4096  | 10,457.09 us |  5.697 us |  5.051 us |  1.00 |    0.00 |
// | UseVector128s        | 4096  |  2,052.41 us |  0.819 us |  0.766 us |  0.20 |    0.00 |
// | UseVector512s        | 4096  |           NA |        NA |        NA |     ? |       ? |
// | UseVectors           | 4096  |  2,058.16 us |  4.110 us |  3.643 us |  0.20 |    0.00 |
// | UseVectorsParallel   | 4096  |  1,152.15 us | 21.134 us | 21.703 us |  0.11 |    0.00 |
// | UseVectorsX2         | 4096  |  2,056.25 us |  1.088 us |  0.965 us |  0.20 |    0.00 |
// | UseVectorsX2Parallel | 4096  |  1,125.10 us | 17.040 us | 15.939 us |  0.11 |    0.00 |
// | PeterParallelScalar  | 4096  |  2,897.94 us | 56.893 us | 91.871 us |  0.28 |    0.01 |
// | PeterParallelSimd    | 4096  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Sse            | 4096  |           NA |        NA |        NA |     ? |       ? |
// | RGB2Y_Avx            | 4096  |           NA |        NA |        NA |     ? |       ? |

// -- `.NET7.0` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.26100.2605)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 8.0.403
//   [Host]     : .NET 7.0.20 (7.0.2024.26716), X64 RyuJIT AVX2
//   DefaultJob : .NET 7.0.20 (7.0.2024.26716), X64 RyuJIT AVX2
// 
// 
// | Method              | Width | Mean         | Error      | StdDev     | Median       | Ratio | RatioSD | Code Size |
// |-------------------- |------ |-------------:|-----------:|-----------:|-------------:|------:|--------:|----------:|
// | Scalar              | 1024  |  1,070.78 us |  14.018 us |  13.112 us |  1,067.33 us |  1.00 |    0.02 |     159 B |
// | UseVector128s       | 1024  |    213.02 us |   2.539 us |   2.375 us |    212.91 us |  0.20 |    0.00 |   2,538 B |
// | UseVectors          | 1024  |    124.48 us |   2.419 us |   2.589 us |    124.05 us |  0.12 |    0.00 |   2,640 B |
// | UseVectorsParallel  | 1024  |     38.93 us |   0.772 us |   1.863 us |     39.00 us |  0.04 |    0.00 |   4,262 B |
// | UseVectorsX2        | 1024  |    126.33 us |   2.390 us |   2.236 us |    126.38 us |  0.12 |    0.00 |   5,436 B |
// | PeterParallelScalar | 1024  |    291.19 us |   5.764 us |  13.359 us |    289.51 us |  0.27 |    0.01 |   3,117 B |
// | PeterParallelSimd   | 1024  |  4,766.51 us |  55.246 us |  51.677 us |  4,771.00 us |  4.45 |    0.07 |   3,224 B |
// | RGB2Y_Sse           | 1024  |    290.82 us |   4.458 us |   4.170 us |    290.22 us |  0.27 |    0.00 |   1,180 B |
// | RGB2Y_Avx           | 1024  |    161.90 us |   2.921 us |   4.094 us |    161.13 us |  0.15 |    0.00 |   1,609 B |
// |                     |       |              |            |            |              |       |         |           |
// | Scalar              | 2048  |  4,308.26 us |  58.425 us |  54.651 us |  4,284.51 us |  1.00 |    0.02 |     159 B |
// | UseVector128s       | 2048  |    961.45 us |  19.082 us |  24.132 us |    959.55 us |  0.22 |    0.01 |   2,538 B |
// | UseVectors          | 2048  |    743.54 us |  14.289 us |  15.289 us |    743.50 us |  0.17 |    0.00 |   2,640 B |
// | UseVectorsParallel  | 2048  |    189.77 us |   3.691 us |   4.927 us |    190.11 us |  0.04 |    0.00 |   4,262 B |
// | UseVectorsX2        | 2048  |    717.88 us |  14.151 us |  31.062 us |    720.54 us |  0.17 |    0.01 |   5,436 B |
// | PeterParallelScalar | 2048  |  1,005.74 us |  22.335 us |  65.506 us |    992.44 us |  0.23 |    0.02 |   3,117 B |
// | PeterParallelSimd   | 2048  | 12,308.28 us | 213.961 us | 200.139 us | 12,278.09 us |  2.86 |    0.06 |   3,224 B |
// | RGB2Y_Sse           | 2048  |  1,157.93 us |  18.965 us |  15.837 us |  1,153.45 us |  0.27 |    0.00 |   1,180 B |
// | RGB2Y_Avx           | 2048  |    810.24 us |  15.891 us |  28.655 us |    814.03 us |  0.19 |    0.01 |   1,609 B |
// |                     |       |              |            |            |              |       |         |           |
// | Scalar              | 4096  | 17,650.25 us | 192.173 us | 179.759 us | 17,657.81 us |  1.00 |    0.01 |     159 B |
// | UseVector128s       | 4096  |  4,482.83 us |  89.644 us | 243.884 us |  4,529.06 us |  0.25 |    0.01 |   2,538 B |
// | UseVectors          | 4096  |  3,663.72 us |  72.281 us |  98.939 us |  3,691.48 us |  0.21 |    0.01 |   2,640 B |
// | UseVectorsParallel  | 4096  |  1,879.11 us |  36.739 us |  40.835 us |  1,884.76 us |  0.11 |    0.00 |   4,262 B |
// | UseVectorsX2        | 4096  |  3,674.10 us | 103.084 us | 303.945 us |  3,775.71 us |  0.21 |    0.02 |   5,436 B |
// | PeterParallelScalar | 4096  |  3,799.83 us |  86.645 us | 247.202 us |  3,784.88 us |  0.22 |    0.01 |   3,117 B |
// | PeterParallelSimd   | 4096  | 32,688.21 us | 522.646 us | 488.883 us | 32,788.06 us |  1.85 |    0.03 |   3,224 B |
// | RGB2Y_Sse           | 4096  |  5,293.30 us | 101.847 us | 125.077 us |  5,314.90 us |  0.30 |    0.01 |   1,180 B |
// | RGB2Y_Avx           | 4096  |  3,827.84 us |  74.990 us | 154.868 us |  3,825.17 us |  0.22 |    0.01 |   1,609 B |

// -- `.NET8.0` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.26100.2605)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 8.0.403
//   [Host]     : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
//   DefaultJob : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
// 
// 
// | Method              | Width | Mean         | Error      | StdDev     | Median       | Ratio | RatioSD | Code Size |
// |-------------------- |------ |-------------:|-----------:|-----------:|-------------:|------:|--------:|----------:|
// | Scalar              | 1024  |  1,068.21 us |  20.677 us |  22.982 us |  1,071.65 us |  1.00 |    0.03 |     152 B |
// | UseVector128s       | 1024  |    205.42 us |   4.069 us |   8.845 us |    206.40 us |  0.19 |    0.01 |        NA |
// | UseVector512s       | 1024  |     89.76 us |   1.727 us |   2.305 us |     89.94 us |  0.08 |    0.00 |        NA |
// | UseVectors          | 1024  |    115.26 us |   2.165 us |   2.127 us |    115.42 us |  0.11 |    0.00 |        NA |
// | UseVectorsParallel  | 1024  |     30.53 us |   0.609 us |   0.540 us |     30.64 us |  0.03 |    0.00 |        NA |
// | UseVectorsX2        | 1024  |    111.36 us |   1.426 us |   1.264 us |    111.67 us |  0.10 |    0.00 |        NA |
// | PeterParallelScalar | 1024  |    268.31 us |   5.163 us |   5.302 us |    268.84 us |  0.25 |    0.01 |        NA |
// | PeterParallelSimd   | 1024  |  4,682.33 us |  88.768 us |  83.033 us |  4,693.03 us |  4.39 |    0.12 |   5,334 B |
// | RGB2Y_Sse           | 1024  |    304.98 us |   6.029 us |   5.922 us |    304.93 us |  0.29 |    0.01 |   1,157 B |
// | RGB2Y_Avx           | 1024  |    138.09 us |   2.691 us |   3.099 us |    138.87 us |  0.13 |    0.00 |   1,540 B |
// |                     |       |              |            |            |              |       |         |           |
// | Scalar              | 2048  |  4,353.42 us |  86.802 us | 112.867 us |  4,393.02 us |  1.00 |    0.04 |     152 B |
// | UseVector128s       | 2048  |    955.72 us |  19.017 us |  22.638 us |    954.41 us |  0.22 |    0.01 |        NA |
// | UseVector512s       | 2048  |    613.52 us |  12.220 us |  24.404 us |    615.05 us |  0.14 |    0.01 |        NA |
// | UseVectors          | 2048  |    705.17 us |  13.965 us |  17.661 us |    709.60 us |  0.16 |    0.01 |        NA |
// | UseVectorsParallel  | 2048  |    154.20 us |   3.019 us |   5.745 us |    155.46 us |  0.04 |    0.00 |        NA |
// | UseVectorsX2        | 2048  |    646.14 us |  20.938 us |  61.735 us |    665.04 us |  0.15 |    0.01 |        NA |
// | PeterParallelScalar | 2048  |    934.35 us |  17.367 us |  16.245 us |    937.62 us |  0.21 |    0.01 |        NA |
// | PeterParallelSimd   | 2048  | 12,526.94 us | 241.316 us | 225.727 us | 12,535.06 us |  2.88 |    0.09 |   5,258 B |
// | RGB2Y_Sse           | 2048  |  1,305.99 us |  17.416 us |  16.291 us |  1,308.38 us |  0.30 |    0.01 |   1,157 B |
// | RGB2Y_Avx           | 2048  |    723.61 us |  13.914 us |  13.665 us |    724.76 us |  0.17 |    0.01 |   1,540 B |
// |                     |       |              |            |            |              |       |         |           |
// | Scalar              | 4096  | 17,542.77 us | 294.576 us | 275.547 us | 17,583.06 us |  1.00 |    0.02 |     152 B |
// | UseVector128s       | 4096  |  4,289.34 us |  84.197 us | 166.196 us |  4,327.95 us |  0.24 |    0.01 |        NA |
// | UseVector512s       | 4096  |  3,002.27 us |  58.116 us |  81.470 us |  2,998.51 us |  0.17 |    0.01 |        NA |
// | UseVectors          | 4096  |  3,244.71 us |  63.577 us |  62.441 us |  3,242.79 us |  0.19 |    0.00 |        NA |
// | UseVectorsParallel  | 4096  |  1,887.42 us |  36.716 us |  49.015 us |  1,900.06 us |  0.11 |    0.00 |        NA |
// | UseVectorsX2        | 4096  |  3,375.33 us |  79.806 us | 235.311 us |  3,431.02 us |  0.19 |    0.01 |        NA |
// | PeterParallelScalar | 4096  |  3,668.24 us |  73.315 us | 181.217 us |  3,667.75 us |  0.21 |    0.01 |        NA |
// | PeterParallelSimd   | 4096  | 28,616.42 us | 371.164 us | 520.319 us | 28,565.94 us |  1.63 |    0.04 |   5,267 B |
// | RGB2Y_Sse           | 4096  |  5,395.80 us | 107.190 us | 187.734 us |  5,460.36 us |  0.31 |    0.01 |   1,157 B |
// | RGB2Y_Avx           | 4096  |  3,395.69 us |  65.199 us |  87.039 us |  3,402.75 us |  0.19 |    0.01 |   1,550 B |

// -- `.NET Framework` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4460/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
//   [Host]     : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
//   DefaultJob : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
// 
// 
// | Method               | Width | Mean        | Error     | StdDev    | Ratio | RatioSD | Code Size |
// |--------------------- |------ |------------:|----------:|----------:|------:|--------:|----------:|
// | Scalar               | 1024  |  1,022.4 us |  17.70 us |  18.18 us |  1.00 |    0.02 |     166 B |
// | UseVectors           | 1024  |  1,630.0 us |   9.99 us |   9.34 us |  1.59 |    0.03 |   5,655 B |
// | UseVectorsParallel   | 1024  |    293.7 us |   4.63 us |   4.33 us |  0.29 |    0.01 |   5,658 B |
// | UseVectorsX2         | 1024  |  1,114.6 us |  21.87 us |  22.46 us |  1.09 |    0.03 |   8,827 B |
// | UseVectorsX2Parallel | 1024  |    209.9 us |   4.19 us |   5.88 us |  0.21 |    0.01 |   8,830 B |
// | PeterParallelScalar  | 1024  |    215.1 us |   1.11 us |   1.04 us |  0.21 |    0.00 |   2,428 B |
// |                      |       |             |           |           |       |         |           |
// | Scalar               | 2048  |  4,079.4 us |  61.81 us |  57.82 us |  1.00 |    0.02 |     166 B |
// | UseVectors           | 2048  |  6,604.3 us |  48.58 us |  45.44 us |  1.62 |    0.02 |   5,655 B |
// | UseVectorsParallel   | 2048  |  1,040.5 us |  19.53 us |  20.90 us |  0.26 |    0.01 |   5,658 B |
// | UseVectorsX2         | 2048  |  4,072.7 us |  28.73 us |  26.87 us |  1.00 |    0.02 |        NA |
// | UseVectorsX2Parallel | 2048  |    698.3 us |  12.45 us |  11.64 us |  0.17 |    0.00 |   8,830 B |
// | PeterParallelScalar  | 2048  |    765.1 us |   5.17 us |   4.84 us |  0.19 |    0.00 |   2,428 B |
// |                      |       |             |           |           |       |         |           |
// | Scalar               | 4096  | 16,368.5 us | 106.80 us |  99.90 us |  1.00 |    0.01 |     166 B |
// | UseVectors           | 4096  | 26,111.8 us | 140.54 us | 131.46 us |  1.60 |    0.01 |   5,655 B |
// | UseVectorsParallel   | 4096  |  4,230.2 us |  82.56 us | 123.57 us |  0.26 |    0.01 |   5,658 B |
// | UseVectorsX2         | 4096  | 18,262.6 us | 175.37 us | 164.04 us |  1.12 |    0.01 |   8,827 B |
// | UseVectorsX2Parallel | 4096  |  2,920.3 us |  56.40 us |  79.06 us |  0.18 |    0.00 |   8,830 B |
// | PeterParallelScalar  | 4096  |  2,950.5 us |  18.40 us |  15.37 us |  0.18 |    0.00 |   2,428 B |
