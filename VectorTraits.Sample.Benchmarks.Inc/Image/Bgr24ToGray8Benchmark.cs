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
    /// Why SIMD only improves performance by only a little bit for RGB to Grayscale, with SIMD multiply but scalar add of vector elements? https://stackoverflow.com/questions/77603639/why-simd-only-improves-performance-by-only-a-little-bit-for-rgb-to-grayscale-wi
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
                        int difference = Math.Abs((int)(q[k]) - p[k]);
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
            const int mulRed = (int)(0.299 * mulPoint); // 19595
            const int mulGreen = (int)(0.587 * mulPoint); // 38469
            const int mulBlue = mulPoint - mulRed - mulGreen; // 7472
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
            const ushort mulRed = (ushort)(0.299 * mulPoint); // 76
            const ushort mulGreen = (ushort)(0.587 * mulPoint); // 150
            const ushort mulBlue = mulPoint - mulRed - mulGreen; // 30
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
            const ushort mulRed = (ushort)(0.299 * mulPoint); // 76
            const ushort mulGreen = (ushort)(0.587 * mulPoint); // 150
            const ushort mulBlue = mulPoint - mulRed - mulGreen; // 30
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
            const ushort mulRed = (ushort)(0.299 * mulPoint); // 76
            const ushort mulGreen = (ushort)(0.587 * mulPoint); // 150
            const ushort mulBlue = mulPoint - mulRed - mulGreen; // 30
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
