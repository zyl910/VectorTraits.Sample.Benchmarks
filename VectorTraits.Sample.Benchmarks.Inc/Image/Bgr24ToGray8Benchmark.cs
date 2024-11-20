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
                try {
                    TextWriter writer = Console.Out;
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
                    PeterParallelSimd();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of PeterParallelSimd: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
#endif // NETCOREAPP3_0_OR_GREATER
                } catch (Exception ex) {
                    Debug.WriteLine(ex.ToString());
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

        //[Benchmark]
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

        [Benchmark]
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
// | Method               | Width | Mean         | Error     | StdDev    | Ratio | RatioSD |
// |--------------------- |------ |-------------:|----------:|----------:|------:|--------:|
// | Scalar               | 1024  |    635.31 us |  0.537 us |  0.448 us |  1.00 |    0.00 |
// | UseVector128s        | 1024  |    126.59 us |  0.492 us |  0.437 us |  0.20 |    0.00 |
// | UseVectors           | 1024  |    127.04 us |  0.567 us |  0.474 us |  0.20 |    0.00 |
// | UseVectorsParallel   | 1024  |     46.37 us |  0.336 us |  0.314 us |  0.07 |    0.00 |
// | UseVectorsX2         | 1024  |    126.82 us |  0.094 us |  0.088 us |  0.20 |    0.00 |
// | UseVectorsX2Parallel | 1024  |     41.59 us |  0.641 us |  0.600 us |  0.07 |    0.00 |
// | PeterParallelScalar  | 1024  |    202.19 us |  1.025 us |  0.959 us |  0.32 |    0.00 |
// | PeterParallelSimd    | 1024  |           NA |        NA |        NA |     ? |       ? |
// |                      |       |              |           |           |       |         |
// | Scalar               | 2048  |  2,625.64 us |  1.795 us |  1.402 us |  1.00 |    0.00 |
// | UseVector128s        | 2048  |    519.49 us |  0.218 us |  0.204 us |  0.20 |    0.00 |
// | UseVectors           | 2048  |    521.40 us |  0.301 us |  0.282 us |  0.20 |    0.00 |
// | UseVectorsParallel   | 2048  |    152.11 us |  3.548 us | 10.064 us |  0.06 |    0.00 |
// | UseVectorsX2         | 2048  |    516.46 us |  0.606 us |  0.567 us |  0.20 |    0.00 |
// | UseVectorsX2Parallel | 2048  |    179.65 us |  6.579 us | 19.400 us |  0.07 |    0.01 |
// | PeterParallelScalar  | 2048  |    711.00 us |  1.806 us |  1.601 us |  0.27 |    0.00 |
// | PeterParallelSimd    | 2048  |           NA |        NA |        NA |     ? |       ? |
// |                      |       |              |           |           |       |         |
// | Scalar               | 4096  | 10,457.09 us |  5.697 us |  5.051 us |  1.00 |    0.00 |
// | UseVector128s        | 4096  |  2,052.41 us |  0.819 us |  0.766 us |  0.20 |    0.00 |
// | UseVectors           | 4096  |  2,058.16 us |  4.110 us |  3.643 us |  0.20 |    0.00 |
// | UseVectorsParallel   | 4096  |  1,152.15 us | 21.134 us | 21.703 us |  0.11 |    0.00 |
// | UseVectorsX2         | 4096  |  2,056.25 us |  1.088 us |  0.965 us |  0.20 |    0.00 |
// | UseVectorsX2Parallel | 4096  |  1,125.10 us | 17.040 us | 15.939 us |  0.11 |    0.00 |
// | PeterParallelScalar  | 4096  |  2,897.94 us | 56.893 us | 91.871 us |  0.28 |    0.01 |
// | PeterParallelSimd    | 4096  |           NA |        NA |        NA |     ? |       ? |

// -- `.NET8.0` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4460/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 8.0.403
//   [Host]     : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
//   DefaultJob : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
// 
// 
// | Method               | Width | Mean         | Error      | StdDev     | Ratio | RatioSD | Code Size |
// |--------------------- |------ |-------------:|-----------:|-----------:|------:|--------:|----------:|
// | Scalar               | 1024  |  1,028.55 us |  12.545 us |  11.735 us |  1.00 |    0.02 |     152 B |
// | UseVector128s        | 1024  |    166.12 us |   2.210 us |   2.067 us |  0.16 |    0.00 |        NA |
// | UseVectors           | 1024  |     94.06 us |   0.606 us |   0.537 us |  0.09 |    0.00 |        NA |
// | UseVectorsParallel   | 1024  |     24.98 us |   0.390 us |   0.365 us |  0.02 |    0.00 |        NA |
// | UseVectorsX2         | 1024  |     87.89 us |   1.085 us |   0.962 us |  0.09 |    0.00 |        NA |
// | UseVectorsX2Parallel | 1024  |     25.40 us |   0.188 us |   0.167 us |  0.02 |    0.00 |        NA |
// | PeterParallelScalar  | 1024  |    216.47 us |   1.719 us |   1.524 us |  0.21 |    0.00 |        NA |
// | PeterParallelSimd    | 1024  |  4,779.36 us |  42.416 us |  39.676 us |  4.65 |    0.06 |   5,308 B |
// |                      |       |              |            |            |       |         |           |
// | Scalar               | 2048  |  4,092.26 us |  21.098 us |  18.703 us |  1.00 |    0.01 |     152 B |
// | UseVector128s        | 2048  |    695.64 us |   8.432 us |   7.474 us |  0.17 |    0.00 |        NA |
// | UseVectors           | 2048  |    507.70 us |   9.626 us |  11.459 us |  0.12 |    0.00 |        NA |
// | UseVectorsParallel   | 2048  |    118.98 us |   1.025 us |   0.959 us |  0.03 |    0.00 |        NA |
// | UseVectorsX2         | 2048  |    490.85 us |   7.965 us |   7.450 us |  0.12 |    0.00 |        NA |
// | UseVectorsX2Parallel | 2048  |    121.17 us |   2.349 us |   2.307 us |  0.03 |    0.00 |        NA |
// | PeterParallelScalar  | 2048  |    803.30 us |   9.226 us |   8.630 us |  0.20 |    0.00 |        NA |
// | PeterParallelSimd    | 2048  | 13,209.97 us | 234.335 us | 219.197 us |  3.23 |    0.05 |   5,258 B |
// |                      |       |              |            |            |       |         |           |
// | Scalar               | 4096  | 16,391.12 us | 121.643 us | 113.785 us |  1.00 |    0.01 |     152 B |
// | UseVector128s        | 4096  |  2,925.29 us |  40.207 us |  35.642 us |  0.18 |    0.00 |        NA |
// | UseVectors           | 4096  |  2,472.16 us |  32.452 us |  30.356 us |  0.15 |    0.00 |        NA |
// | UseVectorsParallel   | 4096  |  2,034.85 us |  33.074 us |  30.937 us |  0.12 |    0.00 |        NA |
// | UseVectorsX2         | 4096  |  2,444.99 us |  41.991 us |  48.356 us |  0.15 |    0.00 |        NA |
// | UseVectorsX2Parallel | 4096  |  1,949.73 us |  23.640 us |  22.112 us |  0.12 |    0.00 |        NA |
// | PeterParallelScalar  | 4096  |  3,139.85 us |  32.657 us |  27.270 us |  0.19 |    0.00 |        NA |
// | PeterParallelSimd    | 4096  | 31,850.99 us | 272.007 us | 254.435 us |  1.94 |    0.02 |   5,267 B |

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
