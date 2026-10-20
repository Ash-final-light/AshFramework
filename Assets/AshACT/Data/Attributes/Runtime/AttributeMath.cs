using System.Numerics;
using Framework.FixedPoint;

namespace AshACT.Attributes
{
    /// <summary>
    /// 带溢出检查的 Q32.32 运算；使用 Unity 的 .NET Standard 环境自带的 BigInteger 保存完整中间值。
    /// 所有计算均不使用 FP64 算术运算符，也不经过浮点数转换。
    /// </summary>
    public static class AttributeMath
    {
        public static readonly FP64 ConfigurationMin = FP64.FromInt(-1000000000);
        public static readonly FP64 ConfigurationMax = FP64.FromInt(1000000000);

        public static bool IsConfigurationValue(FP64 value)
        {
            return value >= ConfigurationMin && value <= ConfigurationMax;
        }

        public static AttributeError Add(FP64 left, FP64 right, out FP64 result)
        {
            return FromWide((BigInteger)left.Raw + right.Raw, out result);
        }

        public static AttributeError Subtract(FP64 left, FP64 right, out FP64 result)
        {
            return FromWide((BigInteger)left.Raw - right.Raw, out result);
        }

        public static AttributeError Multiply(FP64 left, FP64 right, out FP64 result)
        {
            return FromWide(((BigInteger)left.Raw * right.Raw) >> FP64.FractionalBits, out result);
        }

        public static AttributeError Divide(FP64 left, FP64 right, out FP64 result)
        {
            result = default;
            if (right.Raw == 0)
            {
                return AttributeError.DivideByZero;
            }

            BigInteger numerator = (BigInteger)left.Raw << FP64.FractionalBits;
            BigInteger quotient = BigInteger.DivRem(numerator, right.Raw, out BigInteger remainder);
            if (remainder != 0 && (left.Raw < 0) != (right.Raw < 0))
            {
                quotient--;
            }

            return FromWide(quotient, out result);
        }

        /// <summary>
        /// 将不受区域设置影响的十进制文本直接解析为精确分数，正好居中时取偶数舍入。
        /// 接受可选正负号、整数数字，以及可选的小数点和其后的小数数字。
        /// 拒绝指数、空白和分隔符；导出器可另行限制为四位小数显示精度。
        /// </summary>
        public static AttributeError FromDecimalText(string text, out FP64 result)
        {
            result = default;
            if (string.IsNullOrEmpty(text))
            {
                return AttributeError.InvalidConfiguration;
            }

            int start = text[0] == '+' || text[0] == '-' ? 1 : 0;
            bool negative = text[0] == '-';
            bool fraction = false;
            int integerDigits = 0;
            int fractionalDigits = 0;
            BigInteger numerator = BigInteger.Zero;
            BigInteger denominator = BigInteger.One;
            for (int i = start; i < text.Length; i++)
            {
                char digit = text[i];
                if (digit == '.' && !fraction && integerDigits > 0)
                {
                    fraction = true;
                    continue;
                }

                if (digit < '0' || digit > '9')
                {
                    return AttributeError.InvalidConfiguration;
                }

                numerator = numerator * 10 + (digit - '0');
                if (fraction)
                {
                    fractionalDigits++;
                    denominator *= 10;
                }
                else
                {
                    integerDigits++;
                }
            }

            if (integerDigits == 0 || (fraction && fractionalDigits == 0))
            {
                return AttributeError.InvalidConfiguration;
            }

            if (numerator > (BigInteger)1000000000 * denominator)
            {
                return AttributeError.InvalidConfiguration;
            }

            BigInteger raw = BigInteger.DivRem(numerator << FP64.FractionalBits, denominator, out BigInteger remainder);
            int midpoint = (remainder * 2).CompareTo(denominator);
            if (midpoint > 0 || (midpoint == 0 && !raw.IsEven))
            {
                raw++;
            }

            return FromWide(negative ? -raw : raw, out result);
        }

        public static FP64 Clamp(FP64 value, FP64 minimum, FP64 maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        private static AttributeError FromWide(BigInteger raw, out FP64 result)
        {
            result = default;
            if (raw < long.MinValue || raw > long.MaxValue)
            {
                return AttributeError.Overflow;
            }

            result = FP64.FromRaw((long)raw);
            return AttributeError.None;
        }
    }
}
