using System;
using System.Numerics;

namespace AshFramework.Math
{
    /// <summary>提供确定性的标量数学计算；三角函数涉及的角度统一使用弧度。</summary>
    public static class FixMath
    {
        public static readonly Fix64 Pi = Fix64.FromRaw(13493037705L);
        public static readonly Fix64 HalfPi = Fix64.FromRaw(6746518852L);
        public static readonly Fix64 TwoPi = Fix64.FromRaw(26986075409L);

        private const long WidePi = 884279719003555L;
        private const long WideHalfPi = 442139859501778L;
        private const long CordicGain = 170926505739102L;
        private const int WideShift = 16;
        private static readonly long[] CordicAngles =
        {
            221069929750889L, 130505199945453L, 68955363498242L, 35002819193903L,
            17569333089919L, 8793231387230L, 4397688649582L, 2198978517948L,
            1099506035422L, 549755114839L, 274877819563L, 137438942549L,
            68719475371L, 34359738197L, 17179869163L, 8589934589L,
            4294967296L, 2147483648L, 1073741824L, 536870912L,
            268435456L, 134217728L, 67108864L, 33554432L,
            16777216L, 8388608L, 4194304L, 2097152L,
            1048576L, 524288L, 262144L, 131072L,
            65536L, 32768L, 16384L, 8192L,
            4096L, 2048L, 1024L, 512L,
            256L, 128L, 64L, 32L,
            16L, 8L, 4L, 2L
        };

        public static Fix64 Abs(Fix64 value)
        {
            return value < Fix64.Zero ? -value : value;
        }

        public static int Sign(Fix64 value)
        {
            return value.RawValue.CompareTo(0L);
        }

        public static Fix64 Min(Fix64 left, Fix64 right) => left <= right ? left : right;
        public static Fix64 Max(Fix64 left, Fix64 right) => left >= right ? left : right;

        public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max)
        {
            if (min > max)
            {
                throw new ArgumentException("Minimum exceeds maximum.");
            }

            return value < min ? min : value > max ? max : value;
        }

        public static Fix64 Clamp01(Fix64 value)
        {
            return Clamp(value, Fix64.Zero, Fix64.One);
        }

        public static bool Approximately(Fix64 left, Fix64 right, Fix64 tolerance)
        {
            if (tolerance < Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            }

            return BigInteger.Abs((BigInteger)left.RawValue - right.RawValue) <= tolerance.RawValue;
        }

        public static Fix64 Floor(Fix64 value) => Fix64.FromInt(value.FloorToInt());
        public static Fix64 Ceil(Fix64 value) => Fix64.FromInt(value.CeilToInt());
        public static Fix64 Round(Fix64 value) => Fix64.FromInt(value.RoundToInt());

        public static Fix64 Sqrt(Fix64 value)
        {
            if (value < Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return Fix64.FromWideRaw(IntegerSqrt((BigInteger)value.RawValue * Fix64.Scale));
        }

        internal static BigInteger IntegerSqrt(BigInteger value)
        {
            if (value < BigInteger.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value <= BigInteger.One)
            {
                return value;
            }

            byte[] bytes = value.ToByteArray();
            int bits = (bytes.Length - 1) * 8;
            byte highest = bytes[bytes.Length - 1];
            while (highest != 0)
            {
                bits++;
                highest >>= 1;
            }

            BigInteger current = BigInteger.One << ((bits + 1) / 2);
            BigInteger next = (current + value / current) / 2;
            while (next < current)
            {
                current = next;
                next = (current + value / current) / 2;
            }

            return current;
        }

        public static Fix64 Lerp(Fix64 from, Fix64 to, Fix64 t)
        {
            return LerpUnclamped(from, to, Clamp01(t));
        }

        public static Fix64 LerpUnclamped(Fix64 from, Fix64 to, Fix64 t)
        {
            BigInteger delta = (BigInteger)to.RawValue - from.RawValue;
            return Fix64.FromWideRaw(from.RawValue + delta * t.RawValue / Fix64.Scale);
        }

        public static Fix64 InverseLerp(Fix64 from, Fix64 to, Fix64 value)
        {
            if (from == to)
            {
                return Fix64.Zero;
            }

            BigInteger raw = ((BigInteger)value.RawValue - from.RawValue) * Fix64.Scale
                / ((BigInteger)to.RawValue - from.RawValue);
            raw = BigInteger.Max(BigInteger.Zero, BigInteger.Min(Fix64.Scale, raw));
            return Fix64.FromWideRaw(raw);
        }

        public static Fix64 MoveTowards(Fix64 current, Fix64 target, Fix64 maxDelta)
        {
            if (maxDelta < Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDelta));
            }

            BigInteger delta = (BigInteger)target.RawValue - current.RawValue;
            if (BigInteger.Abs(delta) <= maxDelta.RawValue)
            {
                return target;
            }

            return current + (delta.Sign > 0 ? maxDelta : -maxDelta);
        }

        /// <summary>计算非负模，返回值位于 [0, length) 区间。</summary>
        public static Fix64 Repeat(Fix64 value, Fix64 length)
        {
            if (length <= Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            long raw = value.RawValue % length.RawValue;
            return Fix64.FromRaw(raw < 0 ? raw + length.RawValue : raw);
        }

        public static Fix64 PingPong(Fix64 value, Fix64 length)
        {
            if (length <= Fix64.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            BigInteger period = (BigInteger)length.RawValue * 2;
            BigInteger raw = value.RawValue % period;
            if (raw.Sign < 0)
            {
                raw += period;
            }

            return Fix64.FromWideRaw(raw <= length.RawValue ? raw : period - raw);
        }

        public static Fix64 SmoothStep(Fix64 from, Fix64 to, Fix64 t)
        {
            t = Clamp01(t);
            return LerpUnclamped(from, to, t * t * (3 - 2 * t));
        }

        public static Fix64 Pow(Fix64 value, int exponent)
        {
            long remaining = exponent;
            if (remaining < 0)
            {
                value = Fix64.One / value;
                remaining = -remaining;
            }

            Fix64 result = Fix64.One;
            while (remaining != 0)
            {
                if ((remaining & 1) != 0)
                {
                    result *= value;
                }

                remaining >>= 1;
                if (remaining != 0)
                {
                    value *= value;
                }
            }

            return result;
        }

        public static Fix64 DegreesToRadians(Fix64 degrees)
        {
            return Fix64.FromWideRaw((BigInteger)degrees.RawValue * WidePi / (180L << 48));
        }

        public static Fix64 RadiansToDegrees(Fix64 radians)
        {
            return Fix64.FromWideRaw((BigInteger)radians.RawValue * (180L << 48) / WidePi);
        }

        public static Fix64 NormalizeRadians(Fix64 radians)
        {
            Fix64 result = Repeat(radians, TwoPi);
            return result >= Pi ? result - TwoPi : result;
        }

        public static Fix64 NormalizeDegrees(Fix64 degrees)
        {
            return Repeat(degrees, 360);
        }

        /// <summary>计算范围为 [-π, π) 的最短有符号角差，半周转向选择负方向。</summary>
        public static Fix64 DeltaAngle(Fix64 current, Fix64 target)
        {
            BigInteger delta = ((BigInteger)target.RawValue - current.RawValue) % TwoPi.RawValue;
            if (delta.Sign < 0)
            {
                delta += TwoPi.RawValue;
            }

            if (delta >= Pi.RawValue)
            {
                delta -= TwoPi.RawValue;
            }

            return Fix64.FromWideRaw(delta);
        }

        public static Fix64 MoveTowardsAngle(Fix64 current, Fix64 target, Fix64 maxDelta)
        {
            Fix64 delta = DeltaAngle(current, target);
            return NormalizeRadians(NormalizeRadians(current) + MoveTowards(Fix64.Zero, delta, maxDelta));
        }

        public static Fix64 LerpAngle(Fix64 from, Fix64 to, Fix64 t)
        {
            return NormalizeRadians(NormalizeRadians(from) + DeltaAngle(from, to) * Clamp01(t));
        }

        /// <summary>使用固定的 Q48 整数常量执行 CORDIC 算法，运行时不通过浮点数生成查找表。</summary>
        public static void SinCos(Fix64 radians, out Fix64 sin, out Fix64 cos)
        {
            BigInteger angle = ((BigInteger)radians.RawValue << WideShift) % (WidePi * 2);
            if (angle > WidePi)
            {
                angle -= WidePi * 2;
            }
            else if (angle < -WidePi)
            {
                angle += WidePi * 2;
            }

            long z = (long)angle;
            long flip = 1;
            if (z > WideHalfPi)
            {
                z -= WidePi;
                flip = -1;
            }
            else if (z < -WideHalfPi)
            {
                z += WidePi;
                flip = -1;
            }

            long x = CordicGain;
            long y = 0;
            for (int i = 0; i < CordicAngles.Length; i++)
            {
                long previousX = x;
                if (z >= 0)
                {
                    x -= y >> i;
                    y += previousX >> i;
                    z -= CordicAngles[i];
                }
                else
                {
                    x += y >> i;
                    y -= previousX >> i;
                    z += CordicAngles[i];
                }
            }

            sin = Fix64.FromRaw(flip * y / (1L << WideShift));
            cos = Fix64.FromRaw(flip * x / (1L << WideShift));
        }

        public static Fix64 Sin(Fix64 radians)
        {
            SinCos(radians, out Fix64 sin, out _);
            return sin;
        }

        public static Fix64 Cos(Fix64 radians)
        {
            SinCos(radians, out _, out Fix64 cos);
            return cos;
        }

        public static Fix64 Tan(Fix64 radians)
        {
            SinCos(radians, out Fix64 sin, out Fix64 cos);
            return sin / cos;
        }

        /// <summary>输入为 (0,0) 时返回零，其他情况下返回 [-π, π] 范围内的角度。</summary>
        public static Fix64 Atan2(Fix64 y, Fix64 x)
        {
            BigInteger maximum = BigInteger.Max(BigInteger.Abs(x.RawValue), BigInteger.Abs(y.RawValue));
            if (maximum.IsZero)
            {
                return Fix64.Zero;
            }

            long vx = (long)((BigInteger)x.RawValue * (1L << 48) / maximum);
            long vy = (long)((BigInteger)y.RawValue * (1L << 48) / maximum);
            long z = 0;
            if (vx < 0)
            {
                z = vy >= 0 ? WidePi : -WidePi;
                vx = -vx;
                vy = -vy;
            }

            for (int i = 0; i < CordicAngles.Length; i++)
            {
                long previousX = vx;
                if (vy > 0)
                {
                    vx += vy >> i;
                    vy -= previousX >> i;
                    z += CordicAngles[i];
                }
                else if (vy < 0)
                {
                    vx -= vy >> i;
                    vy += previousX >> i;
                    z -= CordicAngles[i];
                }
            }

            return Fix64.FromRaw(z / (1L << WideShift));
        }

        public static Fix64 Atan(Fix64 value)
        {
            return Atan2(value, Fix64.One);
        }

        public static Fix64 Asin(Fix64 value)
        {
            ValidateUnit(value);
            return Atan2(value, Sqrt(Max(Fix64.Zero, Fix64.One - value * value)));
        }

        public static Fix64 Acos(Fix64 value)
        {
            ValidateUnit(value);
            return Atan2(Sqrt(Max(Fix64.Zero, Fix64.One - value * value)), value);
        }

        private static void ValidateUnit(Fix64 value)
        {
            if (value < -Fix64.One || value > Fix64.One)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Expected a value in [-1,1].");
            }
        }
    }
}
