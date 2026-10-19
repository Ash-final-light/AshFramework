using System.Globalization;
using System.Numerics;
using AshACT.Attributes;
using Framework.FixedPoint;

namespace AshACT.Unity
{
    /// <summary>配置文本最多四位小数；显示从 Raw 用整数运算就近取偶，不经过浮点数。</summary>
    public static class DecimalValue
    {
        public static bool TryParse(string text, out FP64 value)
        {
            value = default;
            if (string.IsNullOrEmpty(text) || text.Length > 32)
            {
                return false;
            }

            int dot = text.IndexOf('.');
            if (dot >= 0 && text.Length - dot - 1 > 4)
            {
                return false;
            }

            return AttributeMath.FromDecimalText(text, out value) == AttributeError.None;
        }

        public static string Format(FP64 value)
        {
            BigInteger magnitude = BigInteger.Abs(new BigInteger(value.Raw));
            BigInteger units = BigInteger.DivRem(magnitude * 10000, FP64.OneRaw, out BigInteger remainder);
            int midpoint = (remainder * 2).CompareTo(FP64.OneRaw);
            if (midpoint > 0 || (midpoint == 0 && !units.IsEven))
            {
                units++;
            }

            string sign = value.Raw < 0 && units != 0 ? "-" : "";
            return sign + (units / 10000).ToString(CultureInfo.InvariantCulture) + "." +
                ((int)(units % 10000)).ToString("D4", CultureInfo.InvariantCulture);
        }
    }
}
