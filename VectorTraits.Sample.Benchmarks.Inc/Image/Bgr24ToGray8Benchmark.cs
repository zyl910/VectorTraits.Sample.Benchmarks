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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
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
                    long totalByte = Width * Height * 3;
                    double percentageByteDifference;
                    // Baseline
                    ScalarDo(_sourceBitmapData, _expectedBitmapData);
                    // PeterParallelScalar
                    PeterParallelScalar();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentageByteDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of PeterParallelScalar: {0}/{1}={2}, max={3}, percentage={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentageByteDifference));
#if NETCOREAPP3_0_OR_GREATER
                    // PeterParallelScalar
                    PeterParallelSimd();
                    totalDifference = SumDifference(_expectedBitmapData, _destinationBitmapData, out countByteDifference, out maxDifference);
                    averageDifference = (countByteDifference > 0) ? (double)totalDifference / countByteDifference : 0;
                    percentageByteDifference = 100.0 * countByteDifference / totalByte;
                    writer.WriteLine(string.Format("Difference of PeterParallelSimd: {0}/{1}={2}, max={3}, percentage={4:0.000000}%", totalDifference, countByteDifference, averageDifference, maxDifference, percentageByteDifference));
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


        // == From Peter Cordes. https://stackoverflow.com/questions/77603639/why-simd-only-improves-performance-by-only-a-little-bit-for-rgb-to-grayscale-wi

        [Benchmark]
        public void PeterParallelScalar() {
            Peter.GrayViaParallel(_sourceBitmapData, _destinationBitmapData);
        }

#if NETCOREAPP3_0_OR_GREATER
        [Benchmark]
        public unsafe void PeterParallelSimd() {
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
