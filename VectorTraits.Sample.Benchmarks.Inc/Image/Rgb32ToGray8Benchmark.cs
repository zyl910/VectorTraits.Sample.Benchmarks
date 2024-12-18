﻿//#undef BENCHMARKS_OFF

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
    /// Converte Rgb32 color bitmap to Gray8 grayscale bitmap (将Rgb32彩色位图转为Gray8灰度位图). How to convert byte array of image pixels data to grayscale using vector SSE operation? https://stackoverflow.com/questions/58881359/how-to-convert-byte-array-of-image-pixels-data-to-grayscale-using-vector-sse-ope/
    /// </summary>
    public class Rgb32ToGray8Benchmark : IDisposable {
        private bool _disposed = false;
        private static readonly Random _random = new Random(1);
        private BitmapData _sourceBitmapData = null;
        private BitmapData _destinationBitmapData = null;
        private BitmapData _expectedBitmapData = null;

        [Params(1024, 2048, 4096)]
        public int Width { get; set; }
        public int Height { get; set; }

        ~Rgb32ToGray8Benchmark() {
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
#if NETCOREAPP3_0_OR_GREATER
                    // Soonts_MultiplyHigh
                    Soonts_MultiplyHigh();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of Soonts_MultiplyHigh: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
                    // User555045_MultiplyAddAdjacent
                    User555045_MultiplyAddAdjacent();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of User555045_MultiplyAddAdjacent: {0}/{1}={2}, max={3}, percentDifference={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentDifference));
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
            const int cbPixel = 4; // Rgb32
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
                    *q = (byte)((p[0] * mulRed + p[1] * mulGreen + p[2] * mulBlue) >> shiftPoint);
                    p += cbPixel; // Rgb32
                    q += 1; // Gray8
                }
                pRow += strideSrc;
                qRow += strideDst;
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
            const int cbPixel = 4; // Rgb32
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
                    r = Vectors.YGroup4Unzip(p[0], p[1], p[2], p[3], out g, out b, out _);
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


        // == From Soonts. https://stackoverflow.com/questions/58881359/how-to-convert-byte-array-of-image-pixels-data-to-grayscale-using-vector-sse-ope/
#if NETCOREAPP3_0_OR_GREATER

        [Benchmark]
        public void Soonts_MultiplyHigh() {
            Soonts_MultiplyHighDo(_sourceBitmapData, _destinationBitmapData);
        }

        public static unsafe void Soonts_MultiplyHighDo(BitmapData src, BitmapData dst) {
            if (!Sse41.IsSupported) throw new NotSupportedException("Not support X86's Sse41!");
            int vectorWidth = Vector<byte>.Count;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth) {
                ScalarDo(src, dst);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pRow = (byte*)src.Scan0.ToPointer();
            byte* qRow = (byte*)dst.Scan0.ToPointer();
            for (int i = 0; i < height; i++) {
                Soonts.convertToGrayscale(pRow, qRow, width);
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

        /// <summary>
        /// From Soonts. https://stackoverflow.com/questions/58881359/how-to-convert-byte-array-of-image-pixels-data-to-grayscale-using-vector-sse-ope/
        /// </summary>
        static class Soonts {
            /// <summary>Load 4 pixels of RGB</summary>
            static unsafe Vector128<int> load4(byte* src) {
                return Sse2.LoadVector128((int*)src);
            }

            /// <summary>Pack red channel of 8 pixels into ushort values in [ 0xFF00 .. 0 ] interval</summary>
            static Vector128<ushort> packRed(Vector128<int> a, Vector128<int> b) {
                Vector128<int> mask = Vector128.Create(0xFF);
                a = Sse2.And(a, mask);
                b = Sse2.And(b, mask);
                return Sse2.ShiftLeftLogical128BitLane(Sse41.PackUnsignedSaturate(a, b), 1);
            }

            /// <summary>Pack green channel of 8 pixels into ushort values in [ 0xFF00 .. 0 ] interval</summary>
            static Vector128<ushort> packGreen(Vector128<int> a, Vector128<int> b) {
                Vector128<int> mask = Vector128.Create(0xFF00);
                a = Sse2.And(a, mask);
                b = Sse2.And(b, mask);
                return Sse41.PackUnsignedSaturate(a, b);
            }

            /// <summary>Pack blue channel of 8 pixels into ushort values in [ 0xFF00 .. 0 ] interval</summary>
            static Vector128<ushort> packBlue(Vector128<int> a, Vector128<int> b) {
                a = Sse2.ShiftRightLogical128BitLane(a, 1);
                b = Sse2.ShiftRightLogical128BitLane(b, 1);
                Vector128<int> mask = Vector128.Create(0xFF00);
                a = Sse2.And(a, mask);
                b = Sse2.And(b, mask);
                return Sse41.PackUnsignedSaturate(a, b);
            }

            /// <summary>Load 8 pixels, split into RGB channels.</summary>
            static unsafe void loadRgb(byte* src, out Vector128<ushort> red, out Vector128<ushort> green, out Vector128<ushort> blue) {
                var a = load4(src);
                var b = load4(src + 16);
                red = packRed(a, b);
                green = packGreen(a, b);
                blue = packBlue(a, b);
            }

            const ushort mulRed = (ushort)(0.29891 * 0x10000);
            const ushort mulGreen = (ushort)(0.58661 * 0x10000);
            const ushort mulBlue = (ushort)(0.11448 * 0x10000);

            /// <summary>Compute brightness of 8 pixels</summary>
            static Vector128<short> brightness(Vector128<ushort> r, Vector128<ushort> g, Vector128<ushort> b) {
                r = Sse2.MultiplyHigh(r, Vector128.Create(mulRed));
                g = Sse2.MultiplyHigh(g, Vector128.Create(mulGreen));
                b = Sse2.MultiplyHigh(b, Vector128.Create(mulBlue));
                var result = Sse2.AddSaturate(Sse2.AddSaturate(r, g), b);
                return Vector128.AsInt16(Sse2.ShiftRightLogical(result, 8));
            }

            /// <summary>Convert buffer from RGBA to grayscale.</summary>
            /// <remarks>
            /// <para>If your image has line paddings, you'll want to call this once per line, not for the complete image.</para>
            /// <para>If width of the image is not multiple of 16 pixels, you'll need to do more work to handle the last few pixels of every line.</para>
            /// </remarks>
            public static unsafe void convertToGrayscale(byte* src, byte* dst, int count) {
                byte* srcEnd = src + count * 4;
                while (src < srcEnd) {
                    loadRgb(src, out var r, out var g, out var b);
                    var low = brightness(r, g, b);
                    loadRgb(src + 32, out r, out g, out b);
                    var hi = brightness(r, g, b);

                    var bytes = Sse2.PackUnsignedSaturate(low, hi);
                    Sse2.Store(dst, bytes);

                    src += 64;
                    dst += 16;
                }
            }
        }
#endif // NETCOREAPP3_0_OR_GREATER

        // == From user555045. https://stackoverflow.com/questions/58881359/how-to-convert-byte-array-of-image-pixels-data-to-grayscale-using-vector-sse-ope/
#if NETCOREAPP3_0_OR_GREATER

        [Benchmark]
        public void User555045_MultiplyAddAdjacent() {
            User555045_MultiplyAddAdjacentDo(_sourceBitmapData, _destinationBitmapData);
        }

        public static unsafe void User555045_MultiplyAddAdjacentDo(BitmapData src, BitmapData dst) {
            if (!Ssse3.IsSupported) throw new NotSupportedException("Not support X86's Ssse3!");
            int vectorWidth = Vector<byte>.Count;
            int width = src.Width;
            int height = src.Height;
            if (width <= vectorWidth) {
                ScalarDo(src, dst);
                return;
            }
            int strideSrc = src.Stride;
            int strideDst = dst.Stride;
            byte* pRow = (byte*)src.Scan0.ToPointer();
            byte* qRow = (byte*)dst.Scan0.ToPointer();
            for (int i = 0; i < height; i++) {
                User555045.convertToGrayscale(pRow, qRow, width);
                pRow += strideSrc;
                qRow += strideDst;
            }
        }

        /// <summary>
        /// From user555045. https://stackoverflow.com/questions/58881359/how-to-convert-byte-array-of-image-pixels-data-to-grayscale-using-vector-sse-ope/
        /// </summary>
        static class User555045 {
            public static unsafe void convertToGrayscale(byte* src, byte* dst, int count) {
                int countMain = count & -16;
                byte* srcEnd = src + countMain * 4;
                byte* srcRealEnd = src + count * 4;
                byte* dstRealEnd = dst + count;
                sbyte scaleR = (sbyte)(128 * 0.29891);
                sbyte scaleG = (sbyte)(128 * 0.58661);
                sbyte scaleB = (sbyte)(128 * 0.118);
                Vector128<sbyte> scales = Vector128.Create(scaleR, scaleG, scaleB, 0, scaleR, scaleG, scaleB, 0, scaleR, scaleG, scaleB, 0, scaleR, scaleG, scaleB, 0);
                Vector128<short> ones = Vector128.Create((short)1);
                do {
                    while (src < srcEnd) {
                        var block0 = Sse2.LoadVector128(src);
                        var block1 = Sse2.LoadVector128(src + 16);
                        var block2 = Sse2.LoadVector128(src + 32);
                        var block3 = Sse2.LoadVector128(src + 48);
                        var scaled0 = Ssse3.MultiplyAddAdjacent(block0, scales);
                        var scaled1 = Ssse3.MultiplyAddAdjacent(block1, scales);
                        var scaled2 = Ssse3.MultiplyAddAdjacent(block2, scales);
                        var scaled3 = Ssse3.MultiplyAddAdjacent(block3, scales);
                        var t0 = Sse2.MultiplyAddAdjacent(scaled0, ones);
                        var t1 = Sse2.MultiplyAddAdjacent(scaled1, ones);
                        var t2 = Sse2.MultiplyAddAdjacent(scaled2, ones);
                        var t3 = Sse2.MultiplyAddAdjacent(scaled3, ones);
                        var c01 = Sse2.PackSignedSaturate(t0, t1);
                        c01 = Sse2.ShiftRightLogical(c01, 7);
                        var c23 = Sse2.PackSignedSaturate(t2, t3);
                        c23 = Sse2.ShiftRightLogical(c23, 7);
                        var c0123 = Sse2.PackUnsignedSaturate(c01, c23);
                        Sse2.Store(dst, c0123);
                        src += 64;
                        dst += 16;
                    }
                    // hack to re-use the main loop for the "tail"
                    if (src == srcRealEnd)
                        break;
                    srcEnd = srcRealEnd;
                    src = srcRealEnd - 64;
                    dst = dstRealEnd - 16;
                } while (true);
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
// | Method                         | Width | Mean         | Error     | StdDev    | Ratio | RatioSD |
// |------------------------------- |------ |-------------:|----------:|----------:|------:|--------:|
// | Scalar                         | 1024  |    637.15 us |  0.207 us |  0.184 us |  1.00 |    0.00 |
// | UseVectors                     | 1024  |    127.65 us |  0.052 us |  0.048 us |  0.20 |    0.00 |
// | UseVectorsParallel             | 1024  |     43.16 us |  0.177 us |  0.148 us |  0.07 |    0.00 |
// | Soonts_MultiplyHigh            | 1024  |           NA |        NA |        NA |     ? |       ? |
// | User555045_MultiplyAddAdjacent | 1024  |           NA |        NA |        NA |     ? |       ? |
// |                                |       |              |           |           |       |         |
// | Scalar                         | 2048  |  2,581.51 us | 32.263 us | 30.179 us |  1.00 |    0.02 |
// | UseVectors                     | 2048  |    527.90 us |  1.921 us |  1.500 us |  0.20 |    0.00 |
// | UseVectorsParallel             | 2048  |    196.80 us |  3.912 us |  7.252 us |  0.08 |    0.00 |
// | Soonts_MultiplyHigh            | 2048  |           NA |        NA |        NA |     ? |       ? |
// | User555045_MultiplyAddAdjacent | 2048  |           NA |        NA |        NA |     ? |       ? |
// |                                |       |              |           |           |       |         |
// | Scalar                         | 4096  | 10,473.95 us |  5.011 us |  4.687 us |  1.00 |    0.00 |
// | UseVectors                     | 4096  |  2,123.46 us |  1.009 us |  0.944 us |  0.20 |    0.00 |
// | UseVectorsParallel             | 4096  |  1,367.01 us | 26.117 us | 36.612 us |  0.13 |    0.00 |
// | Soonts_MultiplyHigh            | 4096  |           NA |        NA |        NA |     ? |       ? |
// | User555045_MultiplyAddAdjacent | 4096  |           NA |        NA |        NA |     ? |       ? |

// -- `.NET8.0` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.4460/23H2/2023Update/SunValley3)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 8.0.403
//   [Host]     : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
//   DefaultJob : .NET 8.0.10 (8.0.1024.46610), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
// 
// 
// | Method                         | Width | Mean         | Error     | StdDev    | Ratio | Code Size |
// |------------------------------- |------ |-------------:|----------:|----------:|------:|----------:|
// | Scalar                         | 1024  |  1,020.44 us |  3.610 us |  3.377 us |  1.00 |     152 B |
// | UseVectors                     | 1024  |     88.43 us |  1.408 us |  1.446 us |  0.09 |        NA |
// | UseVectorsParallel             | 1024  |     26.95 us |  0.519 us |  0.486 us |  0.03 |        NA |
// | Soonts_MultiplyHigh            | 1024  |    166.90 us |  1.465 us |  1.299 us |  0.16 |   1,006 B |
// | User555045_MultiplyAddAdjacent | 1024  |     77.91 us |  1.491 us |  1.831 us |  0.08 |     899 B |
// |                                |       |              |           |           |       |           |
// | Scalar                         | 2048  |  4,082.12 us | 11.502 us | 10.196 us |  1.00 |     152 B |
// | UseVectors                     | 2048  |    644.38 us | 12.742 us | 15.649 us |  0.16 |        NA |
// | UseVectorsParallel             | 2048  |    198.42 us |  5.257 us | 15.500 us |  0.05 |        NA |
// | Soonts_MultiplyHigh            | 2048  |    817.16 us | 10.894 us | 10.191 us |  0.20 |   1,006 B |
// | User555045_MultiplyAddAdjacent | 2048  |    615.16 us |  7.149 us |  6.338 us |  0.15 |     899 B |
// |                                |       |              |           |           |       |           |
// | Scalar                         | 4096  | 16,325.46 us | 53.068 us | 47.044 us |  1.00 |     152 B |
// | UseVectors                     | 4096  |  2,994.78 us | 27.709 us | 23.138 us |  0.18 |        NA |
// | UseVectorsParallel             | 4096  |  2,476.04 us | 41.154 us | 38.495 us |  0.15 |        NA |
// | Soonts_MultiplyHigh            | 4096  |  3,321.84 us | 64.082 us | 53.512 us |  0.20 |   1,006 B |
// | User555045_MultiplyAddAdjacent | 4096  |  2,909.79 us | 29.276 us | 27.385 us |  0.18 |     899 B |

// -- `.NET Framework` on X86
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
//   [Host]     : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
//   DefaultJob : .NET Framework 4.8.1 (4.8.9282.0), X64 RyuJIT VectorSize=256
// 
// 
// | Method             | Width | Mean        | Error     | StdDev    | Ratio | RatioSD | Code Size |
// |------------------- |------ |------------:|----------:|----------:|------:|--------:|----------:|
// | Scalar             | 1024  |  1,020.6 us |   5.41 us |   5.06 us |  1.00 |    0.01 |     166 B |
// | UseVectors         | 1024  |  1,501.5 us |  30.02 us |  59.25 us |  1.47 |    0.06 |   6,164 B |
// | UseVectorsParallel | 1024  |    262.9 us |   5.18 us |  10.23 us |  0.26 |    0.01 |        NA |
// |                    |       |             |           |           |       |         |           |
// | Scalar             | 2048  |  4,065.1 us |  70.32 us |  72.21 us |  1.00 |    0.02 |     166 B |
// | UseVectors         | 2048  |  5,781.3 us |  97.26 us | 133.13 us |  1.42 |    0.04 |   6,164 B |
// | UseVectorsParallel | 2048  |  1,052.1 us |  20.18 us |  22.43 us |  0.26 |    0.01 |   6,167 B |
// |                    |       |             |           |           |       |         |           |
// | Scalar             | 4096  | 17,124.3 us | 337.81 us | 515.87 us |  1.00 |    0.04 |     166 B |
// | UseVectors         | 4096  | 23,367.5 us | 454.46 us | 446.34 us |  1.37 |    0.05 |   6,164 B |
// | UseVectorsParallel | 4096  |  3,998.2 us |  79.26 us |  74.14 us |  0.23 |    0.01 |   6,167 B |
