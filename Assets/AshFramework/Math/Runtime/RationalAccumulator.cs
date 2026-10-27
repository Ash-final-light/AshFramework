using System;
using System.Numerics;

namespace AshFramework.Math
{
    /// <summary>
    /// 跨调用保留整数除法余数，适用于速率、数量和时间的累计分配。
    /// 不负责帧调度或读取时钟；商向零截断。
    /// </summary>
    public sealed class RationalAccumulator
    {
        public long Denominator { get; }
        public long Remainder { get; private set; }

        public RationalAccumulator(long denominator, long remainder = 0)
        {
            if (denominator <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(denominator));
            }

            Denominator = denominator;
            RestoreRemainder(remainder);
        }

        public void RestoreRemainder(long remainder)
        {
            if (remainder <= -Denominator || remainder >= Denominator)
            {
                throw new ArgumentOutOfRangeException(nameof(remainder));
            }

            Remainder = remainder;
        }

        public long Accumulate(long numerator)
        {
            BigInteger total = (BigInteger)Remainder + numerator;
            long result = (long)(total / Denominator);
            Remainder = (long)(total % Denominator);
            return result;
        }
    }
}
