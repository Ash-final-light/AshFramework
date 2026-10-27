using System;
using System.Globalization;
using System.Numerics;

namespace AshFramework.Math
{
    /// <summary>
    /// 有符号 Q32.32 定点数。运算结果向零截断，溢出时抛出异常。
    /// 序列化以原始整数值为准，不通过浮点数转换。
    /// </summary>
    [Serializable]
    public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>, IFormattable
    {
        public const int FractionalBits = 32;
        public const long Scale = 1L << FractionalBits;
        public const int FormatVersion = 1;

        public static readonly Fix64 Zero = FromRaw(0);
        public static readonly Fix64 One = FromRaw(Scale);
        public static readonly Fix64 Half = FromRaw(Scale / 2);
        public static readonly Fix64 Epsilon = FromRaw(1);
        public static readonly Fix64 MinValue = FromRaw(long.MinValue);
        public static readonly Fix64 MaxValue = FromRaw(long.MaxValue);

        public long RawValue { get; }

        private Fix64(long rawValue)
        {
            RawValue = rawValue;
        }

        public static Fix64 FromRaw(long rawValue)
        {
            return new Fix64(rawValue);
        }

        public static Fix64 FromInt(long value)
        {
            return FromWideRaw((BigInteger)value * Scale);
        }

        public static Fix64 FromRatio(long numerator, long denominator)
        {
            if (denominator == 0)
            {
                throw new DivideByZeroException();
            }

            return FromWideRaw((BigInteger)numerator * Scale / denominator);
        }

        public static Fix64 FromDecimal(decimal value)
        {
            return Parse(value.ToString(CultureInfo.InvariantCulture));
        }

        public static Fix64 Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (!TryParse(text, out Fix64 result))
            {
                throw new FormatException("Expected an invariant decimal within the Q32.32 range.");
            }

            return result;
        }

        /// <summary>接受可选的正负号和小数点，不接受数字分组或指数表示。</summary>
        public static bool TryParse(string text, out Fix64 result)
        {
            result = Zero;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            int index = 0;
            bool negative = false;
            if (text[0] == '-' || text[0] == '+')
            {
                negative = text[0] == '-';
                index++;
            }

            BigInteger digits = BigInteger.Zero;
            BigInteger divisor = BigInteger.One;
            bool afterPoint = false;
            bool hasDigit = false;
            for (; index < text.Length; index++)
            {
                char character = text[index];
                if (character == '.' && !afterPoint)
                {
                    afterPoint = true;
                    continue;
                }

                if (character < '0' || character > '9')
                {
                    return false;
                }

                hasDigit = true;
                digits = digits * 10 + character - '0';
                if (afterPoint)
                {
                    divisor *= 10;
                }
            }

            if (!hasDigit)
            {
                return false;
            }

            BigInteger raw = digits * Scale / divisor;
            if (negative)
            {
                raw = -raw;
            }

            if (raw < long.MinValue || raw > long.MaxValue)
            {
                return false;
            }

            result = FromRaw((long)raw);
            return true;
        }

        public long TruncateToInt()
        {
            return RawValue / Scale;
        }

        public long FloorToInt()
        {
            long quotient = RawValue / Scale;
            return RawValue < 0 && RawValue % Scale != 0 ? quotient - 1 : quotient;
        }

        public long CeilToInt()
        {
            long quotient = RawValue / Scale;
            return RawValue > 0 && RawValue % Scale != 0 ? quotient + 1 : quotient;
        }

        /// <summary>四舍五入到整数，中点值向远离零的方向取整。</summary>
        public long RoundToInt()
        {
            BigInteger absolute = BigInteger.Abs(RawValue);
            long rounded = (long)((absolute + Scale / 2) / Scale);
            return RawValue < 0 ? -rounded : rounded;
        }

        public decimal ToDecimal()
        {
            return (decimal)RawValue / Scale;
        }

        public int CompareTo(Fix64 other)
        {
            return RawValue.CompareTo(other.RawValue);
        }

        public bool Equals(Fix64 other)
        {
            return RawValue == other.RawValue;
        }

        public override bool Equals(object obj)
        {
            return obj is Fix64 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((int)RawValue ^ (int)(RawValue >> 32));
        }

        /// <summary>返回不受区域设置影响的精确十进制文本；Parse(ToString()) 保留全部原始位。</summary>
        public override string ToString()
        {
            BigInteger absolute = BigInteger.Abs(RawValue);
            BigInteger integer = absolute / Scale;
            BigInteger remainder = absolute % Scale;
            string sign = RawValue < 0 ? "-" : string.Empty;
            if (remainder.IsZero)
            {
                return sign + integer.ToString(CultureInfo.InvariantCulture);
            }

            string fraction = (remainder * BigInteger.Pow(5, FractionalBits))
                .ToString(CultureInfo.InvariantCulture)
                .PadLeft(FractionalBits, '0')
                .TrimEnd('0');
            return sign + integer.ToString(CultureInfo.InvariantCulture) + "." + fraction;
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format) || format == "G" || format == "R")
            {
                return ToString();
            }

            return ToDecimal().ToString(format, formatProvider ?? CultureInfo.InvariantCulture);
        }

        internal static Fix64 FromWideRaw(BigInteger raw)
        {
            return FromRaw((long)raw);
        }

        public static Fix64 operator +(Fix64 left, Fix64 right)
        {
            return FromRaw(checked(left.RawValue + right.RawValue));
        }

        public static Fix64 operator -(Fix64 left, Fix64 right)
        {
            return FromRaw(checked(left.RawValue - right.RawValue));
        }

        public static Fix64 operator -(Fix64 value)
        {
            return FromRaw(checked(-value.RawValue));
        }

        public static Fix64 operator *(Fix64 left, Fix64 right)
        {
            return FromWideRaw((BigInteger)left.RawValue * right.RawValue / Scale);
        }

        public static Fix64 operator /(Fix64 left, Fix64 right)
        {
            if (right.RawValue == 0)
            {
                throw new DivideByZeroException();
            }

            return FromWideRaw((BigInteger)left.RawValue * Scale / right.RawValue);
        }

        public static Fix64 operator %(Fix64 left, Fix64 right)
        {
            return FromWideRaw((BigInteger)left.RawValue % right.RawValue);
        }

        public static bool operator ==(Fix64 left, Fix64 right) => left.RawValue == right.RawValue;
        public static bool operator !=(Fix64 left, Fix64 right) => left.RawValue != right.RawValue;
        public static bool operator <(Fix64 left, Fix64 right) => left.RawValue < right.RawValue;
        public static bool operator >(Fix64 left, Fix64 right) => left.RawValue > right.RawValue;
        public static bool operator <=(Fix64 left, Fix64 right) => left.RawValue <= right.RawValue;
        public static bool operator >=(Fix64 left, Fix64 right) => left.RawValue >= right.RawValue;
        public static implicit operator Fix64(int value) => FromInt(value);
        public static explicit operator Fix64(long value) => FromInt(value);
    }
}
