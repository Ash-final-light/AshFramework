// 定点数学函数：FPMath.cs
//
// 基于 FP64 的确定性数学函数。
//   平方根：通过 Math128 对由高低两个 ulong 组成的整数执行牛顿迭代。
//   正弦与余弦：在 [0, π/2] 上建立 1024 项查找表，结合线性插值和
//               象限对称性覆盖完整定义域。
//   二参数反正切：在 [0, 1] 上建立 1024 项查找表，结合八分区化简求值。
//
// 查找表在静态构造函数中仅使用 FP64 运算生成：正弦使用泰勒展开，
// 反正切先通过恒等式化简，再使用泰勒展开。
// 生成过程不调用双精度 Math.Sin / Math.Atan，因此查找表在不同机器、
// .NET 运行时及处理器架构上逐比特一致，
// 包括 Mono / CoreCLR / NativeAOT 和 ARM / x64。
//
// 仅 PI / PI_OVER_2 / PI_TIMES_2 / Deg2Rad / Rad2Deg 这些上层常量
// 通过 FP64.FromDouble(Math.PI * c) 计算。Math.PI 是编译期双精度常量，
// 且对此常量执行 Math.Round 的行为由 IEEE 754 语义完整规定，
// 因此这些常量的计算同样具有确定性。

using System;

namespace Framework.FixedPoint
{
    /// <summary>
    /// 定点数学函数集合：Sqrt、Sin/Cos、Atan2 等。所有结果跨平台逐比特一致。
    /// </summary>
    public static class FPMath
    {
        /// <summary>圆周率 π。</summary>
        public static readonly FP64 PI = FP64.FromDouble(Math.PI);

        /// <summary>2π。</summary>
        public static readonly FP64 PI_TIMES_2 = FP64.FromDouble(Math.PI * 2.0);

        /// <summary>π/2。</summary>
        public static readonly FP64 PI_OVER_2 = FP64.FromDouble(Math.PI * 0.5);

        /// <summary>角度转弧度的乘数 (π/180)。</summary>
        public static readonly FP64 Deg2Rad = FP64.FromDouble(Math.PI / 180.0);

        /// <summary>弧度转角度的乘数 (180/π)。</summary>
        public static readonly FP64 Rad2Deg = FP64.FromDouble(180.0 / Math.PI);

        private const int TrigLutSize = 1024;

        /// <summary>sin 的 LUT，索引 0..TrigLutSize 覆盖 [0, π/2]，存 Q32.32 Raw。</summary>
        private static readonly long[] mSinLut;

        /// <summary>atan 的 LUT，索引 0..TrigLutSize 覆盖 [0, 1]，存 Q32.32 Raw。</summary>
        private static readonly long[] mAtanLut;

        static FPMath()
        {
            mSinLut = new long[TrigLutSize + 1];
            FP64 sinStep = PI_OVER_2 / FP64.FromInt(TrigLutSize);
            for (int i = 0; i <= TrigLutSize; i++)
            {
                FP64 angle = sinStep * FP64.FromInt(i);
                mSinLut[i] = TaylorSin(angle).Raw;
            }
            // 强制设置精确端点，避免尾项舍入误差影响 SinLookup 提前返回的标准值。
            mSinLut[0] = 0;
            mSinLut[TrigLutSize] = FP64.OneRaw;

            mAtanLut = new long[TrigLutSize + 1];
            FP64 atanStep = FP64.One / FP64.FromInt(TrigLutSize);
            for (int i = 0; i <= TrigLutSize; i++)
            {
                FP64 x = atanStep * FP64.FromInt(i);
                mAtanLut[i] = TaylorAtan(x).Raw;
            }
            mAtanLut[0] = 0;
        }

        /// <summary>
        /// sin(x) 的泰勒展开，<c>x ∈ [0, π/2]</c>，使用 9 项保证 Q32.32 精度。
        /// </summary>
        private static FP64 TaylorSin(FP64 x)
        {
            if (x.Raw <= 0)
            {
                return FP64.Zero;
            }
            FP64 x2 = x * x;
            FP64 term = x;
            FP64 sum = x;
            bool subtract = true;
            for (int n = 3; n <= 17; n += 2)
            {
                term = term * x2 / FP64.FromInt((n - 1) * n);
                sum = subtract ? sum - term : sum + term;
                subtract = !subtract;
            }
            return sum;
        }

        /// <summary>
        /// atan(x) 的"先化简再泰勒"实现，<c>x ∈ [0, 1]</c>。
        /// 利用恒等式 <c>atan(x) = 2 * atan(x / (1 + sqrt(1 + x²)))</c> 把 x 折半到约 ≤ 0.25 后再展开，确保快速收敛。
        /// </summary>
        private static FP64 TaylorAtan(FP64 x)
        {
            if (x.Raw <= 0)
            {
                return FP64.Zero;
            }
            int reductions = 0;
            FP64 cur = x;
            FP64 quarter = FP64.FromRaw(FP64.OneRaw / 4);
            while (cur > quarter && reductions < 4)
            {
                FP64 sqrt = Sqrt(FP64.One + cur * cur);
                cur = cur / (FP64.One + sqrt);
                reductions++;
            }
            FP64 cur2 = cur * cur;
            FP64 term = cur;
            FP64 sum = cur;
            bool subtract = true;
            for (int n = 3; n <= 17; n += 2)
            {
                term = term * cur2;
                FP64 piece = term / FP64.FromInt(n);
                sum = subtract ? sum - piece : sum + piece;
                subtract = !subtract;
            }
            for (int i = 0; i < reductions; i++)
            {
                sum = sum + sum;
            }
            return sum;
        }

        // 基础运算 -----------------------------------------------------------

        /// <summary>绝对值。</summary>
        public static FP64 Abs(FP64 v) => v.Raw >= 0 ? v : FP64.FromRaw(-v.Raw);

        /// <summary>符号：负返回 -1，零返回 0，正返回 1。</summary>
        public static int Sign(FP64 v) => v.Raw == 0 ? 0 : (v.Raw > 0 ? 1 : -1);

        /// <summary>取较小值。</summary>
        public static FP64 Min(FP64 a, FP64 b) => a.Raw < b.Raw ? a : b;

        /// <summary>取较大值。</summary>
        public static FP64 Max(FP64 a, FP64 b) => a.Raw > b.Raw ? a : b;

        /// <summary>把 <paramref name="v"/> 限制在 <c>[min, max]</c>。</summary>
        public static FP64 Clamp(FP64 v, FP64 min, FP64 max)
        {
            if (v.Raw < min.Raw)
            {
                return min;
            }
            if (v.Raw > max.Raw)
            {
                return max;
            }
            return v;
        }

        /// <summary>把 <paramref name="v"/> 限制在 <c>[0, 1]</c>。</summary>
        public static FP64 Clamp01(FP64 v)
        {
            if (v.Raw < 0)
            {
                return FP64.Zero;
            }
            if (v.Raw > FP64.OneRaw)
            {
                return FP64.One;
            }
            return v;
        }

        /// <summary>
        /// 线性插值 <c>a + (b-a)*t</c>。<paramref name="t"/> 不会被钳制，需要钳制可先 <see cref="Clamp01"/>。
        /// </summary>
        public static FP64 Lerp(FP64 a, FP64 b, FP64 t) => a + (b - a) * t;

        /// <summary>近似判等：<c>|a-b| ≤ epsilon</c>。</summary>
        public static bool Approximately(FP64 a, FP64 b, FP64 epsilon)
            => Abs(a - b) <= epsilon;

        // 平方根 -------------------------------------------------------------

        /// <summary>
        /// 平方根，对 <c>v.Raw &lt;&lt; 32</c> 的 128 位整数做牛顿迭代，
        /// 收敛到 <c>floor(sqrt(v.Raw &lt;&lt; 32))</c>，确定性。
        /// 因为 <c>v.Raw &gt;= 0</c>，128 位中间值始终是无符号，故牛顿迭代里的 x 一直能装下 64 位。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="v"/> 为负。</exception>
        public static FP64 Sqrt(FP64 v)
        {
            if (v.Raw < 0)
            {
                throw new ArgumentException("Sqrt of negative", nameof(v));
            }
            if (v.Raw == 0)
            {
                return FP64.Zero;
            }

            // n = v.Raw << 32, 表示为 (nHi, nLo) 的无符号 128 位整数。
            ulong nLo = (ulong)v.Raw << FP64.FractionalBits;
            ulong nHi = (ulong)v.Raw >> (64 - FP64.FractionalBits);

            // bits = ceil(log2(n+1))：算出 n 的最高有效位位置，给牛顿迭代一个紧的初值。
            int bits = 0;
            if (nHi != 0UL)
            {
                bits = 64;
                ulong t = nHi;
                while (t > 0UL)
                {
                    bits++;
                    t >>= 1;
                }
            }
            else
            {
                ulong t = nLo;
                while (t > 0UL)
                {
                    bits++;
                    t >>= 1;
                }
            }

            // 初值 x = 2^((bits+1)/2)，对 v.Raw ≤ long.MaxValue, n < 2^95, x < 2^48，64 位足够装。
            ulong x = 1UL << ((bits + 1) >> 1);

            ulong prev;
            do
            {
                prev = x;
                // x = (x + n/x) >> 1; n/x 的商最终在 2^48 级别，能装入 64 位。
                ulong q = Math128.DivU128By64(nHi, nLo, x, out _);
                x = (x + q) >> 1;
            }
            while (x < prev);

            return FP64.FromRaw((long)prev);
        }

        // 正弦与余弦 ---------------------------------------------------------

        /// <summary>
        /// 正弦。先把弧度化简到 [0, 2π)，再按四个象限映射到 [0, π/2] 上查表并线性插值。
        /// </summary>
        public static FP64 Sin(FP64 radians)
        {
            long twoPi = PI_TIMES_2.Raw;
            long r = radians.Raw % twoPi;
            if (r < 0)
            {
                r += twoPi;
            }

            long halfPi = PI_OVER_2.Raw;
            long pi = PI.Raw;
            long threeHalfPi = pi + halfPi;

            bool negate;
            long reduced;
            if (r < halfPi)
            {
                reduced = r;
                negate = false;
            }
            else if (r < pi)
            {
                reduced = pi - r;
                negate = false;
            }
            else if (r < threeHalfPi)
            {
                reduced = r - pi;
                negate = true;
            }
            else
            {
                reduced = twoPi - r;
                negate = true;
            }

            long val = SinLookup(reduced, halfPi);
            return FP64.FromRaw(negate ? -val : val);
        }

        /// <summary>余弦，等价于 <c>Sin(x + π/2)</c>。</summary>
        public static FP64 Cos(FP64 radians) => Sin(radians + PI_OVER_2);

        /// <summary>查 [0, halfPi] 区间的 sin LUT。<paramref name="angle"/> 是 Q32.32 Raw。</summary>
        private static long SinLookup(long angle, long halfPi)
        {
            if (angle <= 0)
            {
                return 0;
            }
            if (angle >= halfPi)
            {
                return mSinLut[TrigLutSize];
            }

            // mul = angle * TrigLutSize，angle < halfPi (~6.7e9)，TrigLutSize = 1024，
            // 因此 mul < 2^43 仍能装入 long。
            long mul = angle * TrigLutSize;

            // idxScaled = mul << 32，表示为无符号 128 位 (numHi, numLo)。mul 非负。
            ulong numLo = (ulong)mul << FP64.FractionalBits;
            ulong numHi = (ulong)mul >> (64 - FP64.FractionalBits);

            ulong q = Math128.DivU128By64(numHi, numLo, (ulong)halfPi, out _);
            int index = (int)(q >> FP64.FractionalBits);
            long frac = (long)(q & ((ulong)FP64.OneRaw - 1UL));

            if (index >= TrigLutSize)
            {
                return mSinLut[TrigLutSize];
            }

            long a = mSinLut[index];
            long b = mSinLut[index + 1];
            // delta = (b - a) * frac，差值小，但 64×64 仍可能超 long，用 MulFull + 算术右移。
            Math128.MulFull(b - a, frac, out ulong deltaHi, out ulong deltaLo);
            return a + Math128.Shr128ToLong(deltaHi, deltaLo, FP64.FractionalBits);
        }

        // 二参数反正切 -------------------------------------------------------

        /// <summary>
        /// 二参数反正切。根据 <c>|y| ≤ |x|</c> 选择直接查表还是通过 <c>π/2 − atan(|x|/|y|)</c> 化简，
        /// 再按 (x, y) 的符号决定最终象限。
        /// </summary>
        public static FP64 Atan2(FP64 y, FP64 x)
        {
            if (y.Raw == 0 && x.Raw == 0)
            {
                return FP64.Zero;
            }

            long ay = y.Raw >= 0 ? y.Raw : -y.Raw;
            long ax = x.Raw >= 0 ? x.Raw : -x.Raw;

            long a;
            if (ax == 0)
            {
                a = PI_OVER_2.Raw;
            }
            else if (ay <= ax)
            {
                long t = MakeRatio(ay, ax);
                a = AtanLookup(t);
            }
            else
            {
                long t = MakeRatio(ax, ay);
                a = PI_OVER_2.Raw - AtanLookup(t);
            }

            bool xNeg = x.Raw < 0;
            bool yNeg = y.Raw < 0;
            long result;
            if (!xNeg && !yNeg)
            {
                result = a;
            }
            else if (!xNeg && yNeg)
            {
                result = -a;
            }
            else if (xNeg && !yNeg)
            {
                result = PI.Raw - a;
            }
            else
            {
                result = a - PI.Raw;
            }

            return FP64.FromRaw(result);
        }

        /// <summary>单参数反正切，等价于 <c>Atan2(v, 1)</c>。</summary>
        public static FP64 Atan(FP64 v) => Atan2(v, FP64.One);

        /// <summary>
        /// 把 <c>num/den</c> 折算为 Q32.32 Raw（调用约束：<c>0 ≤ num ≤ den</c>，结果落在 [0, 1]）。
        /// </summary>
        private static long MakeRatio(long num, long den)
        {
            // 分子 num << 32 表示为无符号 128 位 (numHi, numLo)。num 非负。
            ulong numLo = (ulong)num << FP64.FractionalBits;
            ulong numHi = (ulong)num >> (64 - FP64.FractionalBits);
            ulong q = Math128.DivU128By64(numHi, numLo, (ulong)den, out _);
            return (long)q;
        }

        /// <summary>查 [0, π/4) 的 atan LUT。<paramref name="t"/> 是 Q32.32 Raw 且属于 [0, 1]。</summary>
        private static long AtanLookup(long t)
        {
            if (t <= 0)
            {
                return 0;
            }
            if (t >= FP64.OneRaw)
            {
                return mAtanLut[TrigLutSize];
            }

            // t ≤ 2^32，TrigLutSize = 1024 = 2^10，乘积 ≤ 2^42，仍在 long 范围。
            long idxScaled = t * TrigLutSize;
            int index = (int)(idxScaled >> FP64.FractionalBits);
            long frac = idxScaled & (FP64.OneRaw - 1);

            if (index >= TrigLutSize)
            {
                return mAtanLut[TrigLutSize];
            }

            long a = mAtanLut[index];
            long b = mAtanLut[index + 1];
            Math128.MulFull(b - a, frac, out ulong deltaHi, out ulong deltaLo);
            return a + Math128.Shr128ToLong(deltaHi, deltaLo, FP64.FractionalBits);
        }
    }
}
