#undef BENCHMARKS_OFF

using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Zyl.VectorTraits;

namespace Zyl.VectorTraits.Sample.Benchmarks.Complexes {
#if BENCHMARKS_OFF
    using BenchmarkAttribute = FakeBenchmarkAttribute;
#else
#endif // BENCHMARKS_OFF

    /// <summary>
    /// Sum of complex multiply (复数乘法求和). C#simd使用Avx类的代码比普通的for循环代码慢，什么原因呢？ https://www.zhihu.com/question/762906402
    /// </summary>
    public class ComplexMultiplySumBenchmark {
        private static readonly Random _random = new Random(1);
        private Complex[] _array;
        private Complex _destination;

        [Params(65536)]
        public int Count { get; set; }

        [GlobalSetup]
        public void Setup() {
            _array = Enumerable.Range(0, Count)
                .Select(_ => {
                    return new Complex(_random.Next(), _random.Next());
                })
                .ToArray();
            // Check.
            bool allowCheck = true;
            if (allowCheck) {
                try {
                    TextWriter writer = Console.Out;
                    writer.WriteLine(string.Format("YGroup2Transpose_AcceleratedTypes:\t{0}", Vectors.YGroup2Transpose_AcceleratedTypes));
                    writer.WriteLine(string.Format("YShuffleG2_AcceleratedTypes:\t{0}", Vectors.YShuffleG2_AcceleratedTypes));
                    writer.WriteLine(string.Format("YShuffleG4X2_AcceleratedTypes:\t{0}", Vectors.YShuffleG4X2_AcceleratedTypes));
                    // CallMul.
                    CallMul();
                    Complex expected = _destination;
                    writer.WriteLine(string.Format("CallMul:\t{0}", expected));
#if NETCOREAPP3_0_OR_GREATER
                    // CallMul2.
                    try {
                        CallMul2();
                        writer.WriteLine(string.Format("CallMul2:\t{0}", _destination));
                    } catch (Exception ex1) {
                        writer.WriteLine("CallMul2: " + ex1.Message);
                    }
#endif // NETCOREAPP3_0_OR_GREATER
                    // UseVectors.
                    UseVectors();
                    writer.WriteLine(string.Format("UseVectors:\t{0}", _destination));
                    // UseVectorsSafe.
                    UseVectorsSafe();
                    writer.WriteLine(string.Format("UseVectorsSafe:\t{0}", _destination));
                    // UseVectorsX2.
                    UseVectorsX2();
                    writer.WriteLine(string.Format("UseVectorsX2:\t{0}", _destination));
#if NET8_0_OR_GREATER
                    // UseVector512sX2.
                    try {
                        UseVector512sX2();
                        writer.WriteLine(string.Format("UseVector512sX2:\t{0}", _destination));
                    } catch (Exception ex1) {
                        writer.WriteLine("UseVector512sX2: " + ex1.Message);
                    }
#endif // NET8_0_OR_GREATER
#if NETCOREAPP3_0_OR_GREATER
                    // Hez2010Simd_Mul2.
                    try {
                        Hez2010Simd_Mul2();
                        writer.WriteLine(string.Format("Hez2010Simd_Mul2:\t{0}", _destination));
                    } catch (Exception ex1) {
                        writer.WriteLine("Hez2010Simd_Mul2: " + ex1.Message);
                    }
#endif // NETCOREAPP3_0_OR_GREATER
#if NET5_0_OR_GREATER
                    // Hez2010Simd.
                    try {
                        Hez2010Simd();
                        writer.WriteLine(string.Format("Hez2010Simd:\t{0}", _destination));
                    } catch (Exception ex1) {
                        writer.WriteLine("Hez2010Simd: " + ex1.Message);
                    }
#endif // NET5_0_OR_GREATER
                } catch (Exception ex) {
                    Debug.WriteLine(ex.ToString());
                }
            }
        }

        [Benchmark(Baseline = true)]
        public void CallMul() {
            _destination = mul(_array);
        }

        public static Complex mul(Complex[] a) {
            Complex c = 0;
            for (int i = 0; i < a.Length; i++) {
                c += a[i] * a[i];
            }

            return c;
        }

#if NETCOREAPP3_0_OR_GREATER

        [Benchmark]
        public void CallMul2() {
            if (!Avx.IsSupported || !Avx2.IsSupported) throw new NotSupportedException("Not support X86's Avx2!");
            _destination = mul2(_array);
        }

        public unsafe static Complex mul2(Complex[] a) {
            Complex r = 0;
            int li = a.Length - 1;
            fixed (Complex* a_ = a) {
                Vector256<double>* sa = (Vector256<double>*)a_;
                var mask = Vector256.Create(0.0, -0.0, 0.0, -0.0);
                int t = a.Length >> 1;
                int left = a.Length - (t << 1);
                Vector256<double> M1 = new Vector256<double>(), M2 = new Vector256<double>();
                Complex* r1 = (Complex*)&M2;
                Complex* r2 = r1 + 1;
                for (int i = 0; i < t; i++) {
                    M1 = Unsafe.Read<Vector256<double>>(sa + i);
                    M2 = Avx.Add(Avx.HorizontalAdd(Avx.Multiply(Avx.Xor(M1, mask), M1), Avx.Multiply(M1, Avx2.Permute4x64(M1, 0b10110001))), M2);
                }
                r = *r1 + *r2;
                if (left > 0) {
                    r += a[li] * a[li];
                }
            }

            return r;
        }

#endif // NETCOREAPP3_0_OR_GREATER

        [Benchmark]
        public void UseVectors() {
            _destination = UseVectorsDo(_array);
        }

        public static unsafe Complex UseVectorsDo(Complex[] numbers) {
            int blockWidth = Vector<double>.Count / 2; // Complex is double*2
            int cntBlock = numbers.Length / blockWidth;
            int cntRem = numbers.Length - (cntBlock * blockWidth);
            // -- Processs body.
            Complex result;
            Vector<double> acc = Vector<double>.Zero;
            Vector<double> mask = Vectors.CreateRotate(0.0, -0.0);
            fixed (Complex* pnumbers = numbers) {
                Vector<double>* p = (Vector<double>*)pnumbers; // Set pointer to numbers[0].
                Vector<double>* pEnd = p + cntBlock;
                while (p < pEnd) {
                    // -- Complex multiply: (a + bi)*(c + di) = (ac – bd) + (ad + bc)i
                    Vector<double> a = *p; // a + bi
                    var c = a; // c + di
                    var e = Vector.Multiply(a, Vector.Xor(c, mask)); // (a*c) + (-b*d)i
                    var f = Vectors.YShuffleG2(c, ShuffleControlG2.YX); // (d) + (c)i
                    f = Vector.Multiply(a, f); // (a*d) + (b*c)i
                    var g = Vectors.YGroup2Transpose(e, f, out var h); // g is {(a*c) + (a*d)i}; h is {(-b*d) + (b*c)i}
                    g += h; // (a*c - b*d) + (a*d + b*c)i
                    // Sum
                    acc += g;
                    // Next
                    ++p;
                }
                // Vector to a Complex.
                double re = 0.0, im = 0.0;
                for (int i = 0; i < Vector<double>.Count; i += 2) {
                    re += acc[i];
                    im += acc[i + 1];
                }
                result = new Complex(re, im);
                // -- Processs remainder.
                Complex* q = (Complex*)pEnd;
                for (int i = 0; i < cntRem; i++) {
                    result += (*q) * (*q);
                    // Next
                    ++q;
                }
            }
            return result;
        }

        [Benchmark]
        public void UseVectorsSafe() {
            _destination = UseVectorsSafeDo(_array);
        }

        public static Complex UseVectorsSafeDo(Complex[] numbers) {
            int blockWidth = Vector<double>.Count / 2; // Complex is double*2
            int cntBlock = numbers.Length / blockWidth;
            int cntRem = numbers.Length - (cntBlock * blockWidth);
            // -- Processs body.
            Vector<double> acc = Vector<double>.Zero;
            Vector<double> mask = Vectors.CreateRotate(0.0, -0.0);
            ref Vector<double> p = ref Unsafe.As<Complex, Vector<double>>(ref numbers[0]); // Set pointer to numbers[0].
            ref Vector<double> pEnd = ref Unsafe.Add(ref p, cntBlock);
            while (Unsafe.IsAddressLessThan(ref p, ref pEnd)) {
                // -- Complex multiply: (a + bi)*(c + di) = (ac – bd) + (ad + bc)i
                Vector<double> a = p; // a + bi
                var c = a; // c + di
                var e = Vector.Multiply(a, Vector.Xor(c, mask)); // (a*c) + (-b*d)i
                var f = Vectors.YShuffleG2(c, ShuffleControlG2.YX); // (d) + (c)i
                f = Vector.Multiply(a, f); // (a*d) + (b*c)i
                var g = Vectors.YGroup2Transpose(e, f, out var h); // g is {(a*c) + (a*d)i}; h is {(-b*d) + (b*c)i}
                g += h; // (a*c - b*d) + (a*d + b*c)i
                // Sum
                acc += g;
                // Next
                p = ref Unsafe.Add(ref p, 1);
            }
            // Vector to a Complex.
            double re = 0.0, im = 0.0;
            for (int i = 0; i < Vector<double>.Count; i += 2) {
                re += acc[i];
                im += acc[i + 1];
            }
            Complex result = new Complex(re, im);
            // -- Processs remainder.
            ref Complex q = ref Unsafe.As<Vector<double>, Complex>(ref pEnd);
            for (int i = 0; i < cntRem; i++) {
                result += q * q;
                // Next
                q = ref Unsafe.Add(ref q, 1);
            }
            return result;
        }

        [Benchmark]
        public void UseVectorsX2() {
            _destination = UseVectorsX2Do(_array);
        }

        public static Complex UseVectorsX2Do(Complex[] numbers) {
            const int batchWidth = 2; // X2
            int blockWidth = Vector<double>.Count * batchWidth / 2; // Complex is double*2
            int cntBlock = numbers.Length / blockWidth;
            int cntRem = numbers.Length - (cntBlock * blockWidth);
            // -- Processs body.
            Vector<double> acc = Vector<double>.Zero;
            Vector<double> acc1 = Vector<double>.Zero;
            Vector<double> mask = Vectors.CreateRotate(0.0, -0.0);
            ref Vector<double> p = ref Unsafe.As<Complex, Vector<double>>(ref numbers[0]); // Set pointer to numbers[0].
            ref Vector<double> pEnd = ref Unsafe.Add(ref p, cntBlock * batchWidth);
            while (Unsafe.IsAddressLessThan(ref p, ref pEnd)) {
                // -- Complex multiply: (a + bi)*(c + di) = (ac – bd) + (ad + bc)i
                Vector<double> a0 = p; // a + bi
                var a1 = Unsafe.Add(ref p, 1);
                var c0 = a0; // c + di
                var c1 = a1;
                var e0 = Vector.Multiply(a0, Vector.Xor(c0, mask)); // (a*c) + (-b*d)i
                var e1 = Vector.Multiply(a1, Vector.Xor(c1, mask));
                var f0 = Vectors.YShuffleG4X2_Const(c0, c1, ShuffleControlG4.YXWZ, out var f1); // (d) + (c)i
                f0 = Vector.Multiply(a0, f0); // (a*d) + (b*c)i
                f1 = Vector.Multiply(a1, f1);
                var g0 = Vectors.YGroup2Transpose(e0, f0, out var h0); // g is {(a*c) + (a*d)i}; h is {(-b*d) + (b*c)i}
                var g1 = Vectors.YGroup2Transpose(e1, f1, out var h1);
                g0 += h0; // (a*c - b*d) + (a*d + b*c)i
                g1 += h1;
                // Sum
                acc += g0;
                acc1 += g1;
                // Next
                p = ref Unsafe.Add(ref p, batchWidth);
            }
            acc += acc1;
            // Vector to a Complex.
            double re = 0.0, im = 0.0;
            for (int i = 0; i < Vector<double>.Count; i += 2) {
                re += acc[i];
                im += acc[i + 1];
            }
            Complex result = new Complex(re, im);
            // -- Processs remainder.
            ref Complex q = ref Unsafe.As<Vector<double>, Complex>(ref pEnd);
            for (int i = 0; i < cntRem; i++) {
                result += q * q;
                // Next
                q = ref Unsafe.Add(ref q, 1);
            }
            return result;
        }

#if NET8_0_OR_GREATER

        [Benchmark]
        public void UseVector512sX2() {
            if (!Vector512s.IsHardwareAccelerated) throw new NotSupportedException("Vector512 does not have hardware acceleration!");
            _destination = UseVector512sX2Do(_array);
        }

        public static Complex UseVector512sX2Do(Complex[] numbers) {
            const int batchWidth = 2; // X2
            int blockWidth = Vector512<double>.Count * batchWidth / 2; // Complex is double*2
            int cntBlock = numbers.Length / blockWidth;
            int cntRem = numbers.Length - (cntBlock * blockWidth);
            // -- Processs body.
            Vector512<double> acc = Vector512<double>.Zero;
            Vector512<double> acc1 = Vector512<double>.Zero;
            Vector512<double> mask = Vector512s.CreateRotate(0.0, -0.0);
            ref Vector512<double> p = ref Unsafe.As<Complex, Vector512<double>>(ref numbers[0]); // Set pointer to numbers[0].
            ref Vector512<double> pEnd = ref Unsafe.Add(ref p, cntBlock * batchWidth);
            while (Unsafe.IsAddressLessThan(ref p, ref pEnd)) {
                // -- Complex multiply: (a + bi)*(c + di) = (ac – bd) + (ad + bc)i
                Vector512<double> a0 = p; // a + bi
                var a1 = Unsafe.Add(ref p, 1);
                var c0 = a0; // c + di
                var c1 = a1;
                var e0 = Vector512s.Multiply(a0, Vector512s.Xor(c0, mask)); // (a*c) + (-b*d)i
                var e1 = Vector512s.Multiply(a1, Vector512s.Xor(c1, mask));
                var f0 = Vector512s.YShuffleG4X2_Const(c0, c1, ShuffleControlG4.YXWZ, out var f1); // (d) + (c)i
                f0 = Vector512s.Multiply(a0, f0); // (a*d) + (b*c)i
                f1 = Vector512s.Multiply(a1, f1);
                var g0 = Vector512s.YGroup2Transpose(e0, f0, out var h0); // g is {(a*c) + (a*d)i}; h is {(-b*d) + (b*c)i}
                var g1 = Vector512s.YGroup2Transpose(e1, f1, out var h1);
                g0 += h0; // (a*c - b*d) + (a*d + b*c)i
                g1 += h1;
                // Sum
                acc += g0;
                acc1 += g1;
                // Next
                p = ref Unsafe.Add(ref p, batchWidth);
            }
            acc += acc1;
            // Vector to a Complex.
            double re = 0.0, im = 0.0;
            for (int i = 0; i < Vector512<double>.Count; i += 2) {
                re += acc[i];
                im += acc[i + 1];
            }
            Complex result = new Complex(re, im);
            // -- Processs remainder.
            ref Complex q = ref Unsafe.As<Vector512<double>, Complex>(ref pEnd);
            for (int i = 0; i < cntRem; i++) {
                result += q * q;
                // Next
                q = ref Unsafe.Add(ref q, 1);
            }
            return result;
        }

#endif // NET8_0_OR_GREATER

        // == From hez2010. https://www.zhihu.com/question/762906402/answer/63205597712

#if NETCOREAPP3_0_OR_GREATER

        [Benchmark]
        public void Hez2010Simd_Mul2() {
            if (!Avx.IsSupported || !Avx2.IsSupported) throw new NotSupportedException("Not support X86's Avx2!");
            _destination = Hez2010.Simd_Mul2(_array);
        }

#if NET5_0_OR_GREATER
        [Benchmark]
        public void Hez2010Simd() {
            if (!Avx.IsSupported || !Avx2.IsSupported) throw new NotSupportedException("Not support X86's Avx2!");
            _destination = Hez2010.Simd(_array);
        }
#endif // NET5_0_OR_GREATER

        /// <summary>
        /// From hez2010. https://godbolt.org/z/Kvo9h4YbE
        /// </summary>
        static class Hez2010 {
            public static unsafe Complex Simd_Mul2(Complex[] numbers) {
                Complex r = 0;
                int li = numbers.Length - 1;
                fixed (Complex* a_ = numbers) {
                    Vector256<double>* sa = (Vector256<double>*)a_;
                    var mask = Vector256.Create(0.0, -0.0, 0.0, -0.0);
                    int t = numbers.Length >> 1;
                    int left = numbers.Length - (t << 1);
                    Vector256<double> M1 = new Vector256<double>(), M2 = new Vector256<double>();
                    for (int i = 0; i < t; i++) {
                        M1 = Unsafe.Read<Vector256<double>>(sa + i);
                        M2 = Avx.Add(Avx.HorizontalAdd(Avx.Multiply(Avx.Xor(M1, mask), M1), Avx.Multiply(M1, Avx2.Permute4x64(M1, 0b10110001))), M2);
                    }
                    //r = new(M2[0] + M2[2], M2[1] + M2[3]); // Need .NET 7.0+
                    r = new Complex(M2.GetElement(0) + M2.GetElement(2), M2.GetElement(1) + M2.GetElement(3));
                    if (left > 0) {
                        r += numbers[li] * numbers[li];
                    }
                }

                return r;
            }

#if NET5_0_OR_GREATER
            public static Complex Simd(Complex[] numbers) {
                ReadOnlySpan<Vector256<double>> vectors = MemoryMarshal.Cast<Complex, Vector256<double>>(numbers);
                Vector256<double> acc = Vector256<double>.Zero;
                for (int i = 0; i < vectors.Length; i++) {
                    acc = Avx.Add(Avx.HorizontalAdd(Avx.Multiply(Avx.Xor(vectors[i], Vector256.Create(0.0, -0.0, 0.0, -0.0)), vectors[i]), Avx.Multiply(vectors[i], Avx2.Permute4x64(vectors[i], 0b10110001))), acc);
                }
                Complex result = new Complex(acc.GetElement(0) + acc.GetElement(2), acc.GetElement(1) + acc.GetElement(3));
                ref Complex current = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(numbers), vectors.Length * Vector256<double>.Count);
                ref Complex end = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(numbers), numbers.Length);
                while (Unsafe.IsAddressLessThan(ref current, ref end)) {
                    result += current * current;
                    current = ref Unsafe.Add(ref current, 1);
                }

                return result;
            }
#endif // NET5_0_OR_GREATER

        }
#endif // NETCOREAPP3_0_OR_GREATER

    }
}

// == Benchmarks result

// -- `.NET8.0` on Arm
// BenchmarkDotNet v0.14.0, macOS Sequoia 15.1.1 (24B91) [Darwin 24.1.0]
// Apple M2, 1 CPU, 8 logical and 8 physical cores
// .NET SDK 8.0.204
//   [Host]     : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD
//   DefaultJob : .NET 8.0.4 (8.0.424.16909), Arm64 RyuJIT AdvSIMD
// 
// 
// | Method           | Count | Mean     | Error    | StdDev   | Ratio | RatioSD |
// |----------------- |------ |---------:|---------:|---------:|------:|--------:|
// | CallMul          | 65536 | 56.30 us | 0.051 us | 0.045 us |  1.00 |    0.00 |
// | CallMul2         | 65536 |       NA |       NA |       NA |     ? |       ? |
// | UseVectors       | 65536 | 56.90 us | 0.468 us | 0.415 us |  1.01 |    0.01 |
// | UseVectorsSafe   | 65536 | 56.32 us | 0.019 us | 0.017 us |  1.00 |    0.00 |
// | UseVectorsX2     | 65536 | 47.18 us | 0.025 us | 0.024 us |  0.84 |    0.00 |
// | UseVector512sX2  | 65536 |       NA |       NA |       NA |     ? |       ? |
// | Hez2010Simd_Mul2 | 65536 |       NA |       NA |       NA |     ? |       ? |
// | Hez2010Simd      | 65536 |       NA |       NA |       NA |     ? |       ? |

// -- `.NET8.0` on X86
// BenchmarkDotNet v0.14.0, Windows 11 (10.0.26100.2605)
// AMD Ryzen 7 7840H w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
// .NET SDK 9.0.101
//   [Host]     : .NET 8.0.11 (8.0.1124.51707), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
//   DefaultJob : .NET 8.0.11 (8.0.1124.51707), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
// 
// 
// | Method           | Count | Mean     | Error    | StdDev   | Ratio | Code Size |
// |----------------- |------ |---------:|---------:|---------:|------:|----------:|
// | CallMul          | 65536 | 44.45 us | 0.329 us | 0.308 us |  1.00 |     128 B |
// | CallMul2         | 65536 | 92.50 us | 0.104 us | 0.087 us |  2.08 |        NA |
// | UseVectors       | 65536 | 22.48 us | 0.068 us | 0.061 us |  0.51 |        NA |
// | UseVectorsSafe   | 65536 | 22.47 us | 0.084 us | 0.070 us |  0.51 |        NA |
// | UseVectorsX2     | 65536 | 17.95 us | 0.080 us | 0.075 us |  0.40 |        NA |
// | UseVector512sX2  | 65536 | 17.26 us | 0.179 us | 0.167 us |  0.39 |        NA |
// | Hez2010Simd_Mul2 | 65536 | 23.69 us | 0.206 us | 0.193 us |  0.53 |        NA |
// | Hez2010Simd      | 65536 | 23.01 us | 0.151 us | 0.134 us |  0.52 |     298 B |
