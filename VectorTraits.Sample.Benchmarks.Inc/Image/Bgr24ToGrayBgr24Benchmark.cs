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
