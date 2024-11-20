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
    /// Converte Bgr24 color bitmap to grayscale Bgr24 bitmap (将Bgr24彩色位图转为灰度的Bgr24位图). C++ to C# memory alignment issue. https://stackoverflow.com/questions/79185374/c-to-c-sharp-memory-alignment-issue/
    /// </summary>
    public class Bgr24ToGrayBgr24Benchmark : IDisposable {
        private bool _disposed = false;
        private static readonly Random _random = new Random(1);
        private BitmapData _sourceBitmapData = null;
        private BitmapData _destinationBitmapData = null;
        private BitmapData _expectedBitmapData = null;

        [Params(1024, 2048, 4096)]
        public int Width { get; set; }
        public int Height { get; set; }

        ~Bgr24ToGrayBgr24Benchmark() {
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
                    // UseVectorsParallel2
                    UseVectorsParallel2();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of UseVectorsParallel2: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
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
#if NETCOREAPP3_0_OR_GREATER
                    // SoontsVector
                    SoontsVector();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of SoontsVector: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
#endif // NETCOREAPP3_0_OR_GREATER
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
            const int cbPixel = 3; // Bgr24 store grayscale.
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
            ScalarDo(_sourceBitmapData, _destinationBitmapData, 0);
        }

        [Benchmark]
        public void ScalarParallel() {
            ScalarDo(_sourceBitmapData, _destinationBitmapData, 1);
        }

        public static unsafe void ScalarDo(BitmapData src, BitmapData dst, int parallelFactor = 0) {
            int width = src.Width;
            int height = src.Height;
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            int processorCount = Environment.ProcessorCount;
            int batchSize = 0;
            if (parallelFactor > 1) {
                batchSize = height / (processorCount * parallelFactor);
            } else if (parallelFactor == 1) {
                if (height >= processorCount) batchSize = 1;
            }
            bool allowParallel = (batchSize > 0) && (processorCount > 1);
            if (allowParallel) {
                int batchCount = (height + batchSize - 1) / batchSize; // ceil((double)length / batchSize)
                Parallel.For(0, batchCount, i => {
                    int start = batchSize * i;
                    int len = batchSize;
                    if (start + len > height) len = height - start;
                    byte* pSrc2 = pSrc + start * strideSrc;
                    byte* pDst2 = pDst + start * strideDst;
                    ScalarDoBatch(pSrc2, strideSrc, width, len, pDst2, strideDst);
                });
            } else {
                ScalarDoBatch(pSrc, strideSrc, width, height, pDst, strideDst);
            }
        }

        public static unsafe void ScalarDoBatch(byte* pSrc, int strideSrc, int width, int height, byte* pDst, int strideDst) {
            const int cbPixel = 3; // Bgr24
            const int shiftPoint = 16;
            const int mulPoint = 1 << shiftPoint; // 0x10000
            const int mulRed = (int)(0.299 * mulPoint + 0.5); // 19595
            const int mulGreen = (int)(0.587 * mulPoint + 0.5); // 38470
            const int mulBlue = mulPoint - mulRed - mulGreen; // 7471
            byte* pRow = pSrc;
            byte* qRow = pDst;
            for (int i = 0; i < height; i++) {
                byte* p = pRow;
                byte* q = qRow;
                for (int j = 0; j < width; j++) {
                    byte gray = (byte)((p[2] * mulRed + p[1] * mulGreen + p[0] * mulBlue) >> shiftPoint);
                    q[0] = q[1] = q[2] = gray;
                    p += cbPixel; // Bgr24
                    q += cbPixel; // Bgr24 store grayscale.
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

        [Benchmark]
        public void UseVectors() {
            UseVectorsDo(_sourceBitmapData, _destinationBitmapData, 0);
        }

        [Benchmark]
        public void UseVectorsParallel() {
            UseVectorsDo(_sourceBitmapData, _destinationBitmapData, 1);
        }

        [Benchmark]
        public void UseVectorsParallel2() {
            UseVectorsDo(_sourceBitmapData, _destinationBitmapData, 2);
        }

        public static unsafe void UseVectorsDo(BitmapData src, BitmapData dst, int parallelFactor = 0) {
            int vectorWidth = Vector<byte>.Count;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth) {
                ScalarDo(src, dst, parallelFactor);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            int processorCount = Environment.ProcessorCount;
            int batchSize = 0;
            if (parallelFactor > 1) {
                batchSize = height / (processorCount * parallelFactor);
            } else if (parallelFactor == 1) {
                if (height >= processorCount) batchSize = 1;
            }
            bool allowParallel = (batchSize > 0) && (processorCount > 1);
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
                Vector<byte>* pLast = (Vector<byte>*)(pRow + maxX * cbPixel); // Bgr24
                Vector<byte>* qLast = (Vector<byte>*)(qRow + maxX * cbPixel); // Bgr24 store grayscale.
                Vector<byte>* p = (Vector<byte>*)pRow;
                Vector<byte>* q = (Vector<byte>*)qRow;
                for (; ; ) {
                    Vector<byte> r, g, b, gray, gray0, gray1, gray2;
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
                    gray0 = Vectors.YGroup3Zip(gray, gray, gray, out gray1, out gray2);
                    q[0] = gray0;
                    q[1] = gray1;
                    q[2] = gray2;
                    // Next.
                    if (p >= pLast) break;
                    p += cbPixel;
                    q += cbPixel;
                    if (p > pLast) p = pLast; // The last block is also use vector.
                    if (q > qLast) q = qLast;
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

        [Benchmark]
        public void UseVectorsX2() {
            UseVectorsX2Do(_sourceBitmapData, _destinationBitmapData, 0);
        }

        [Benchmark]
        public void UseVectorsX2Parallel() {
            UseVectorsX2Do(_sourceBitmapData, _destinationBitmapData, 1);
        }

        public static unsafe void UseVectorsX2Do(BitmapData src, BitmapData dst, int parallelFactor = 0) {
            int vectorWidth = Vector<byte>.Count;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth * 2) {
                UseVectorsDo(src, dst, parallelFactor);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            int processorCount = Environment.ProcessorCount;
            int batchSize = 0;
            if (parallelFactor > 1) {
                batchSize = height / (processorCount * parallelFactor);
            } else if (parallelFactor == 1) {
                if (height >= processorCount) batchSize = 1;
            }
            bool allowParallel = (batchSize > 0) && (processorCount > 1);
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
                Vector<byte>* pLast = (Vector<byte>*)(pRow + maxX * cbPixel); // Bgr24
                Vector<byte>* qLast = (Vector<byte>*)(qRow + maxX * cbPixel); // Bgr24 store grayscale.
                Vector<byte>* p = (Vector<byte>*)pRow;
                Vector<byte>* q = (Vector<byte>*)qRow;
                for (; ; ) {
                    Vector<byte> r0, r1, g0, g1, b0, b1, gray0, gray1, gray2, gray3, gray4, gray5;
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
                    gray0 = Vectors.YGroup3ZipX2(gray0, gray1, gray0, gray1, gray0, gray1, out gray1, out gray2, out gray3, out gray4, out gray5);
                    q[0] = gray0;
                    q[1] = gray1;
                    q[2] = gray2;
                    q[3] = gray3;
                    q[4] = gray4;
                    q[5] = gray5;
                    // Next.
                    if (p >= pLast) break;
                    p += vectorInBlock * cbPixel;
                    q += vectorInBlock * cbPixel;
                    if (p > pLast) p = pLast; // The last block is also use vector.
                    if (q > qLast) q = qLast;
                }
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

        // == From Soonts. https://stackoverflow.com/questions/79185374/c-to-c-sharp-memory-alignment-issue/

#if NETCOREAPP3_0_OR_GREATER
        [Benchmark]
        public unsafe void SoontsVector() {
            SoontsVectorDo(_sourceBitmapData, _destinationBitmapData);
        }

        public static unsafe void SoontsVectorDo(BitmapData src, BitmapData dst) {
            if (!Ssse3.IsSupported) throw new NotSupportedException("Not support X86's Ssse3!");
            if (!Avx2.IsSupported) throw new NotSupportedException("Not support X86's Avx2!");
            int width = src.Width;
            int height = src.Height;
            int length = width * 3; // Bgr24.
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pSrc = (byte*)src.Scan0.ToPointer();
            byte* pDst = (byte*)dst.Scan0.ToPointer();
            for (int i = 0; i < height; ++i) {
                Soonts.ConvertRgbToGrayscaleSIMDTo(pSrc, pDst, length);
                pSrc += strideSrc;
                pDst += strideDst;
            }
        }

        /// <summary>
        /// From Soonts. https://stackoverflow.com/questions/79185374/c-to-c-sharp-memory-alignment-issue/
        /// </summary>
        static class Soonts {
            // static const __m128i s_unpackTriplets = _mm_setr_epi8(
            //     0, 1, 2, -1, 3, 4, 5, -1, 6, 7, 8, -1, 9, 10, 11, -1 );
            static readonly Vector128<byte> s_unpackTriplets = Vector128.Create((sbyte)0, 1, 2, -1, 3, 4, 5, -1, 6, 7, 8, -1, 9, 10, 11, -1).AsByte();
            static readonly Vector256<byte> s_unpackTriplets256 = Vector256.Create(s_unpackTriplets, s_unpackTriplets);

            // Load 24 bytes from memory, zero extending triplets from RGB into RGBA
            // The alpha bytes will be zeros
            // inline __m256i loadRgb8( const uint8_t* rsi )
            // {
            //     // Load 24 bytes into 2 SSE vectors, 16 and 8 bytes respectively
            //     const __m128i low = _mm_loadu_si128( ( const __m128i* )rsi );
            //     __m128i high = _mm_loadu_si64( rsi + 16 );
            //     // Make the high vector contain exactly 4 triplets = 12 bytes
            //     high = _mm_alignr_epi8( high, low, 12 );
            //     // Combine into AVX2 vector
            //     __m256i res = _mm256_setr_m128i( low, high );
            //     // Hope the compiler inlines this function, and moves the vbroadcasti128 outside of the loop
            //     const __m256i perm = _mm256_broadcastsi128_si256( s_unpackTriplets );
            //     // Unpack RGB24 into RGB32
            //     return _mm256_shuffle_epi8( res, perm );
            // }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static unsafe Vector256<byte> loadRgb8(byte* rsi) {
                // Load 24 bytes into 2 SSE vectors, 16 and 8 bytes respectively
                Vector128<byte> low = *(Vector128<byte>*)rsi;
                Vector128<byte> high = (*(Vector64<byte>*)(rsi + 16)).ToVector128();
                // Make the high vector contain exactly 4 triplets = 12 bytes
                high = Ssse3.AlignRight(high, low, 12);
                // Combine into AVX2 vector
                Vector256<byte> res = Vector256.Create(low, high);
                // Use s_unpackTriplets256. // Hope the compiler inlines this function, and moves the vbroadcasti128 outside of the loop
                Vector256<byte> perm = s_unpackTriplets256;
                // Unpack RGB24 into RGB32
                return Avx2.Shuffle(res, perm);
            }

            // Greyscale coefficients approximated to integers: R = 0.3, G = 0.59, B = 0.11
            // constexpr uint8_t coeffR = 77;  // 0.3 * 256 ≈ 77
            // constexpr uint8_t coeffG = 150; // 0.59 * 256 ≈ 150
            // constexpr uint8_t coeffB = 29;  // 0.11 * 256 ≈ 29
            const byte coeffR = 77;  // 0.3 * 256 ≈ 77
            const byte coeffG = 150; // 0.59 * 256 ≈ 150
            const byte coeffB = 29;  // 0.11 * 256 ≈ 29

            // Compute vector of int32 lanes with r*coeffR + g*coeffG + b*coeffB
            // inline __m256i makeGreyscale( __m256i rgba )
            // {
            //     const __m256i lowBytesMask = _mm256_set1_epi32( 0x00FF00FF );
            //     __m256i rb = _mm256_and_si256( rgba, lowBytesMask );
            //     __m256i g = _mm256_and_si256( _mm256_srli_epi16( rgba, 8 ), lowBytesMask );
            // 
            //     // Scale red and blue channels, then add pairwise into int32 lanes
            //     constexpr int mulRbScalar = ( ( (int)coeffB ) << 16 ) | coeffR;
            //     const __m256i mulRb = _mm256_set1_epi32( mulRbScalar );
            //     rb = _mm256_madd_epi16( rb, mulRb );
            // 
            //     // Scale green channel
            //     const __m256i mulGreen = _mm256_set1_epi32( coeffG );
            //     g = _mm256_mullo_epi16( g, mulGreen );
            // 
            //     // Compute the result in 32-bit lanes
            //     return _mm256_add_epi32( rb, g );
            // }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static Vector256<uint> makeGreyscaleBgr(Vector256<byte> rgba) {
                Vector256<int> lowBytesMask = Vector256.Create(0x00FF00FF);
                Vector256<int> rb = Avx2.And(rgba.AsInt32(), lowBytesMask);
                Vector256<int> g = Avx2.And(Avx2.ShiftRightLogical(rgba.AsUInt16(), 8).AsInt32(), lowBytesMask);

                // Scale red and blue channels, then add pairwise into int32 lanes
                const int mulRbScalar = (((int)coeffR) << 16) | coeffB; // This is BGR pixel, not RGB in the original code.
                Vector256<int> mulRb = Vector256.Create(mulRbScalar);
                rb = Avx2.MultiplyAddAdjacent(rb.AsInt16(), mulRb.AsInt16());

                // Scale green channel
                Vector256<int> mulGreen = Vector256.Create((int)coeffG);
                g = Avx2.MultiplyLow(g, mulGreen);

                // Compute the result in 32-bit lanes
                return Avx2.Add(rb, g).AsUInt32();
            }

            // static const __m256i s_packTriplets = _mm256_setr_epi8(
            //     // Low half of the vector: e0 e0 e0 e1 e1 e1 e2 e2 e2 e3 e3 e3 0 0 0 0 
            //     1, 1, 1, 5, 5, 5, 9, 9, 9, 13, 13, 13, -1, -1, -1, -1,
            //     // High half of the vector: e1 e1 e2 e2 e2 e3 e3 e3 0 0 0 0 e0 e0 e0 e1 
            //     5, 5, 9, 9, 9, 13, 13, 13, -1, -1, -1, -1, 1, 1, 1, 5 );
            static readonly Vector256<byte> s_packTriplets = Vector256.Create(
                 // Low half of the vector: e0 e0 e0 e1 e1 e1 e2 e2 e2 e3 e3 e3 0 0 0 0 
                 1, 1, 1, 5, 5, 5, 9, 9, 9, 13, 13, 13, -1, -1, -1, -1,
                 // High half of the vector: e1 e1 e2 e2 e2 e3 e3 e3 0 0 0 0 e0 e0 e0 e1 
                 5, 5, 9, 9, 9, 13, 13, 13, -1, -1, -1, -1, 1, 1, 1, 5
                ).AsByte();

            // Extract second byte from each int32 lane, triplicate these bytes, and store 24 bytes to memory
            // inline void storeRgb8( uint8_t* rdi, __m256i gs )
            // {
            //     // Move bytes within 16 byte lanes
            //     gs = _mm256_shuffle_epi8( gs, s_packTriplets );
            // 
            //     // Split vector into halves
            //     __m128i low = _mm256_castsi256_si128( gs );
            //     const __m128i high = _mm256_extracti128_si256( gs, 1 );
            //     // Insert high 4 bytes from high into low
            //     low = _mm_blend_epi32( low, high, 0b1000 );
            // 
            //     // Store 24 RGB bytes
            //     _mm_storeu_si128( ( __m128i* )rdi, low );
            //     _mm_storeu_si64( rdi + 16, high );
            // }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static unsafe void storeRgb8(byte* rdi, Vector256<byte> gs) {
                // Move bytes within 16 byte lanes
                gs = Avx2.Shuffle(gs, s_packTriplets);

                // Split vector into halves
                Vector128<byte> low = gs.GetLower();
                Vector128<byte> high = gs.GetUpper();
                // Insert high 4 bytes from high into low
                low = Avx2.Blend(low.AsUInt32(), high.AsUInt32(), 0b1000).AsByte();

                // Store 24 RGB bytes
                *(Vector128<byte>*)rdi = low;
                *(Vector64<byte>*)(rdi + 16) = high.GetLower();
            }

            // inline void computeGreyscale8( uint8_t* ptr )
            // {
            //     __m256i v = loadRgb8( ptr );
            //     v = makeGreyscale( v );
            //     storeRgb8( ptr, v );
            // }
            public static unsafe void computeGreyscale8To(byte* pSrc, byte* pDst) {
                Vector256<byte> v = loadRgb8(pSrc);
                v = makeGreyscaleBgr(v).AsByte();
                storeRgb8(pDst, v);
            }

            // void ConvertRgbToGrayscaleSIMD( uint8_t* ptr, size_t length )
            // {
            //     const size_t rem = length % 24;
            // 
            //     uint8_t* const endAligned = ptr + ( length - rem );
            //     for( ; ptr < endAligned; ptr += 24 )
            //         computeGreyscale8( ptr );
            // 
            //     if( rem != 0 )
            //     {
            //         // An easy way to handle remainder is using a local buffer of 24 bytes, reusing the implementation
            //         // Unlike memcpy / memset which are function calls and are subject to ABI conventions,
            //         // __movsb / __stosb don't destroy data in vector registers
            //         uint8_t remSpan[ 24 ];
            //         __movsb( remSpan, ptr, rem );
            //         __stosb( &remSpan[ rem ], 0, 24 - rem );
            // 
            //         computeGreyscale8( remSpan );
            // 
            //         __movsb( ptr, remSpan, rem );
            //     }
            // }
            public static unsafe void ConvertRgbToGrayscaleSIMDTo(byte* pSrc, byte* pDst, int length) {
                int rem = length % 24;

                byte* endAligned = pSrc + (length - rem);
                for (; pSrc < endAligned; pSrc += 24, pDst += 24) {
                    computeGreyscale8To(pSrc, pDst);
                }

                if (rem != 0) {
                    // An easy way to handle remainder is using a local buffer of 24 bytes
                    const int spanSize = 24;
                    Span<byte> remSpan = stackalloc byte[spanSize];
                    remSpan.Clear(); // __stosb(&remSpan[rem], 0, 24 - rem);
                    fixed (byte* pRem = remSpan) {
                        Buffer.MemoryCopy(pSrc, pRem, spanSize, rem);
                        
                        computeGreyscale8To(pRem, pRem);

                        Buffer.MemoryCopy(pRem, pDst, spanSize, rem);
                    }
                }
            }
        }
#endif // NETCOREAPP3_0_OR_GREATER

    }

}

// == Benchmarks result

// -- `.NET8.0` on Arm
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.0.1 (24A348) [Darwin 24.0.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 8.0.204
//   [Host]     : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD
//   DefaultJob : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD
// 
// 
// | Method               | Width | Mean         | Error      | StdDev     | Median       | Ratio | RatioSD |
// |--------------------- |------ |-------------:|-----------:|-----------:|-------------:|------:|--------:|
// | Scalar               | 1024  |    719.32 us |   0.215 us |   0.201 us |    719.34 us |  1.00 |    0.00 |
// | ScalarParallel       | 1024  |    157.38 us |   1.423 us |   1.111 us |    157.25 us |  0.22 |    0.00 |
// | UseVectors           | 1024  |    169.25 us |   0.538 us |   0.503 us |    169.40 us |  0.24 |    0.00 |
// | UseVectorsParallel   | 1024  |     57.81 us |   0.998 us |   2.149 us |     58.11 us |  0.08 |    0.00 |
// | UseVectorsParallel2  | 1024  |     71.15 us |   0.575 us |   0.639 us |     71.05 us |  0.10 |    0.00 |
// | UseVectorsX2         | 1024  |    183.74 us |   1.046 us |   0.927 us |    183.39 us |  0.26 |    0.00 |
// | UseVectorsX2Parallel | 1024  |     60.60 us |   1.199 us |   1.902 us |     60.03 us |  0.08 |    0.00 |
// | SoontsVector         | 1024  |           NA |         NA |         NA |           NA |     ? |       ? |
// |                      |       |              |            |            |              |       |         |
// | Scalar               | 2048  |  2,963.48 us |   6.674 us |   5.211 us |  2,961.39 us |  1.00 |    0.00 |
// | ScalarParallel       | 2048  |    627.47 us |  11.680 us |  25.142 us |    616.63 us |  0.21 |    0.01 |
// | UseVectors           | 2048  |    716.27 us |   2.097 us |   1.961 us |    717.02 us |  0.24 |    0.00 |
// | UseVectorsParallel   | 2048  |    368.49 us |   7.320 us |  21.469 us |    378.95 us |  0.12 |    0.01 |
// | UseVectorsParallel2  | 2048  |    397.41 us |   3.373 us |   5.252 us |    396.97 us |  0.13 |    0.00 |
// | UseVectorsX2         | 2048  |    758.99 us |   2.281 us |   2.133 us |    759.22 us |  0.26 |    0.00 |
// | UseVectorsX2Parallel | 2048  |    351.37 us |   6.756 us |   7.509 us |    354.25 us |  0.12 |    0.00 |
// | SoontsVector         | 2048  |           NA |         NA |         NA |           NA |     ? |       ? |
// |                      |       |              |            |            |              |       |         |
// | Scalar               | 4096  | 12,449.32 us | 177.868 us | 157.676 us | 12,508.13 us |  1.00 |    0.02 |
// | ScalarParallel       | 4096  |  2,510.22 us |  34.541 us |  30.620 us |  2,501.37 us |  0.20 |    0.00 |
// | UseVectors           | 4096  |  2,968.72 us |  20.503 us |  18.175 us |  2,965.71 us |  0.24 |    0.00 |
// | UseVectorsParallel   | 4096  |  1,728.46 us |   4.362 us |   4.080 us |  1,729.00 us |  0.14 |    0.00 |
// | UseVectorsParallel2  | 4096  |  1,769.06 us |  13.280 us |  11.090 us |  1,766.84 us |  0.14 |    0.00 |
// | UseVectorsX2         | 4096  |  3,057.69 us |  23.689 us |  22.159 us |  3,063.90 us |  0.25 |    0.00 |
// | UseVectorsX2Parallel | 4096  |  1,730.11 us |   3.373 us |   2.990 us |  1,729.84 us |  0.14 |    0.00 |
// | SoontsVector         | 4096  |           NA |         NA |         NA |           NA |     ? |       ? |

// -- `.NET8.0` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4460/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 8.0.403
//   [Host]     : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
//   DefaultJob : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
// 
// 
// | Method               | Width | Mean         | Error      | StdDev     | Ratio |
// |--------------------- |------ |-------------:|-----------:|-----------:|------:|
// | Scalar               | 1024  |  1,128.81 us |   4.436 us |   3.932 us |  1.00 |
// | ScalarParallel       | 1024  |    157.96 us |   1.007 us |   0.942 us |  0.14 |
// | UseVectors           | 1024  |    123.79 us |   1.144 us |   1.014 us |  0.11 |
// | UseVectorsParallel   | 1024  |     26.05 us |   0.503 us |   0.471 us |  0.02 |
// | UseVectorsParallel2  | 1024  |     27.59 us |   0.313 us |   0.278 us |  0.02 |
// | UseVectorsX2         | 1024  |    113.03 us |   0.751 us |   0.702 us |  0.10 |
// | UseVectorsX2Parallel | 1024  |     23.95 us |   0.479 us |   0.448 us |  0.02 |
// | SoontsVector         | 1024  |  1,412.87 us |   4.399 us |   3.673 us |  1.25 |
// |                      |       |              |            |            |       |
// | Scalar               | 2048  |  4,279.99 us |  37.658 us |  35.226 us |  1.00 |
// | ScalarParallel       | 2048  |    622.01 us |   3.989 us |   3.537 us |  0.15 |
// | UseVectors           | 2048  |    631.53 us |   6.741 us |   6.305 us |  0.15 |
// | UseVectorsParallel   | 2048  |    330.47 us |   5.479 us |   4.857 us |  0.08 |
// | UseVectorsParallel2  | 2048  |    362.95 us |   6.374 us |   5.962 us |  0.08 |
// | UseVectorsX2         | 2048  |    622.86 us |   7.399 us |   6.921 us |  0.15 |
// | UseVectorsX2Parallel | 2048  |    339.39 us |   5.836 us |   5.459 us |  0.08 |
// | SoontsVector         | 2048  |  5,691.75 us |  20.576 us |  17.182 us |  1.33 |
// |                      |       |              |            |            |       |
// | Scalar               | 4096  | 17,252.90 us | 106.215 us |  99.353 us |  1.00 |
// | ScalarParallel       | 4096  |  3,743.78 us |  25.989 us |  24.310 us |  0.22 |
// | UseVectors           | 4096  |  3,273.92 us |  32.645 us |  30.537 us |  0.19 |
// | UseVectorsParallel   | 4096  |  3,746.83 us |  11.083 us |   9.255 us |  0.22 |
// | UseVectorsParallel2  | 4096  |  3,778.64 us |  72.801 us |  71.501 us |  0.22 |
// | UseVectorsX2         | 4096  |  3,218.48 us |  28.084 us |  26.270 us |  0.19 |
// | UseVectorsX2Parallel | 4096  |  3,811.73 us |  44.010 us |  41.167 us |  0.22 |
// | SoontsVector         | 4096  | 22,836.86 us | 190.572 us | 178.261 us |  1.32 |

// -- `.NET Framework` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4460/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
//   [Host]     : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
//   DefaultJob : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
// 
// 
// | Method               | Width | Mean        | Error     | StdDev    | Ratio | RatioSD | Code Size |
// |--------------------- |------ |------------:|----------:|----------:|------:|--------:|----------:|
// | Scalar               | 1024  |  1,144.3 us |   6.87 us |   6.43 us |  1.00 |    0.01 |   2,813 B |
// | ScalarParallel       | 1024  |    188.0 us |   2.84 us |   2.65 us |  0.16 |    0.00 |   2,816 B |
// | UseVectors           | 1024  |  3,761.0 us |  44.63 us |  41.75 us |  3.29 |    0.04 |        NA |
// | UseVectorsParallel   | 1024  |    510.2 us |   7.41 us |   6.93 us |  0.45 |    0.01 |        NA |
// | UseVectorsParallel2  | 1024  |    655.9 us |  12.36 us |  11.56 us |  0.57 |    0.01 |        NA |
// | UseVectorsX2         | 1024  |  2,545.8 us |  14.70 us |  13.03 us |  2.22 |    0.02 |        NA |
// | UseVectorsX2Parallel | 1024  |    355.5 us |   4.53 us |   4.24 us |  0.31 |    0.00 |        NA |
// |                      |       |             |           |           |       |         |           |
// | Scalar               | 2048  |  4,572.6 us |  16.74 us |  14.84 us |  1.00 |    0.00 |   2,813 B |
// | ScalarParallel       | 2048  |    704.0 us |   8.79 us |   8.22 us |  0.15 |    0.00 |   2,816 B |
// | UseVectors           | 2048  | 14,765.7 us | 168.90 us | 157.99 us |  3.23 |    0.03 |        NA |
// | UseVectorsParallel   | 2048  |  1,946.6 us |  38.41 us |  39.44 us |  0.43 |    0.01 |        NA |
// | UseVectorsParallel2  | 2048  |  2,437.1 us |  48.45 us |  59.50 us |  0.53 |    0.01 |        NA |
// | UseVectorsX2         | 2048  | 10,566.7 us |  36.51 us |  32.37 us |  2.31 |    0.01 |        NA |
// | UseVectorsX2Parallel | 2048  |  1,338.9 us |  26.01 us |  38.12 us |  0.29 |    0.01 |        NA |
// |                      |       |             |           |           |       |         |           |
// | Scalar               | 4096  | 18,254.0 us | 122.53 us | 114.61 us |  1.00 |    0.01 |   2,813 B |
// | ScalarParallel       | 4096  |  3,726.5 us |  25.17 us |  23.54 us |  0.20 |    0.00 |   2,816 B |
// | UseVectors           | 4096  | 59,189.0 us | 931.28 us | 871.12 us |  3.24 |    0.05 |        NA |
// | UseVectorsParallel   | 4096  |  7,127.7 us | 138.79 us | 136.31 us |  0.39 |    0.01 |        NA |
// | UseVectorsParallel2  | 4096  |  9,802.2 us | 191.50 us | 325.18 us |  0.54 |    0.02 |        NA |
// | UseVectorsX2         | 4096  | 38,508.4 us | 135.91 us | 113.49 us |  2.11 |    0.01 |        NA |
// | UseVectorsX2Parallel | 4096  |  5,204.3 us |  55.91 us |  49.56 us |  0.29 |    0.00 |        NA |
