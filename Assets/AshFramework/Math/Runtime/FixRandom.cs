using System;
using System.Collections.Generic;
using System.Numerics;

namespace AshFramework.Math
{
    /// <summary>
    /// 使用 SplitMix64 算法，明确采用溢出回绕运算，并支持状态恢复。
    /// 复现结果要求算法、状态和调用顺序一致；不用于密码学用途。
    /// </summary>
    public sealed class FixRandom
    {
        public const int AlgorithmVersion = 1;
        public ulong State { get; private set; }

        public FixRandom(ulong seed)
        {
            State = seed;
        }

        public void RestoreState(ulong state)
        {
            State = state;
        }

        public ulong NextUInt64()
        {
            unchecked
            {
                State += 0x9E3779B97F4A7C15UL;
                ulong value = State;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return value ^ (value >> 31);
            }
        }

        public uint NextUInt32()
        {
            return (uint)(NextUInt64() >> 32);
        }

        /// <summary>通过拒绝采样，在 [0, exclusiveMax) 区间内生成无偏整数样本。</summary>
        public ulong NextUInt64(ulong exclusiveMax)
        {
            if (exclusiveMax == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            }

            ulong threshold = unchecked(0UL - exclusiveMax) % exclusiveMax;
            ulong value;
            do
            {
                value = NextUInt64();
            }
            while (value < threshold);

            return value % exclusiveMax;
        }

        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            }

            return (int)NextUInt64((ulong)exclusiveMax);
        }

        /// <summary>在 [inclusiveMin, exclusiveMax) 区间内采样；上下界相等时返回该值，不消耗随机状态。</summary>
        public int Range(int inclusiveMin, int exclusiveMax)
        {
            if (inclusiveMin > exclusiveMax)
            {
                throw new ArgumentException("Minimum exceeds maximum.");
            }

            if (inclusiveMin == exclusiveMax)
            {
                return inclusiveMin;
            }

            ulong width = (ulong)((long)exclusiveMax - inclusiveMin);
            return (int)((long)inclusiveMin + (long)NextUInt64(width));
        }

        public Fix64 NextUnit()
        {
            return Fix64.FromRaw((long)(NextUInt64() >> 32));
        }

        public Fix64 Range(Fix64 inclusiveMin, Fix64 exclusiveMax)
        {
            if (inclusiveMin > exclusiveMax)
            {
                throw new ArgumentException("Minimum exceeds maximum.");
            }

            if (inclusiveMin == exclusiveMax)
            {
                return inclusiveMin;
            }

            ulong width = (ulong)((BigInteger)exclusiveMax.RawValue - inclusiveMin.RawValue);
            return Fix64.FromWideRaw((BigInteger)inclusiveMin.RawValue + NextUInt64(width));
        }

        public void Shuffle<T>(IList<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            for (int index = items.Count - 1; index > 0; index--)
            {
                int other = NextInt(index + 1);
                T value = items[index];
                items[index] = items[other];
                items[other] = value;
            }
        }

        public FixVector2 OnUnitCircle()
        {
            return FixVector2.FromAngle(NextUnit() * FixMath.TwoPi);
        }

        /// <summary>按面积均匀采样，最终结果受定点数量化影响。</summary>
        public FixVector2 InsideUnitCircle()
        {
            Fix64 radius = FixMath.Sqrt(NextUnit());
            return OnUnitCircle() * radius;
        }

        /// <summary>按表面积均匀采样，最终结果受定点数量化影响。</summary>
        public FixVector3 OnUnitSphere()
        {
            Fix64 z = NextUnit() * 2 - 1;
            Fix64 radius = FixMath.Sqrt(FixMath.Max(0, 1 - z * z));
            FixVector2 circle = OnUnitCircle() * radius;
            return new FixVector3(circle.X, circle.Y, z);
        }
    }
}
