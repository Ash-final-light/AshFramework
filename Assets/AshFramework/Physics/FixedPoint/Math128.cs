// 宽整数辅助运算：Math128.cs
//
// 使用 long / ulong 实现的精简 128 位整数辅助运算，
// 使 FP64 / FPMath 能在缺少 System.Int128 的 Unity 环境中使用，
// 包括 Mono / IL2CPP 和 .NET Standard 2.1。
//
// 确定性要求：
//   * 仅使用整数算术和位运算。ECMA-335 / C# 规范保证这些运算在符合规范的
//     运行时中逐比特一致，包括 CoreCLR / Mono / IL2CPP / NativeAOT，
//     以及 x64 / ARM64 / 32 位 ARM 平台。
//   * 所有依赖二进制补码回绕的操作均显式放在 unchecked 块中，
//     包括 ~x + 1、low - d 的下溢、MulFull 内的大数加法，
//     以及将大于 long.MaxValue 的 ulong 窄化转换为 long。
//     因此，即使调用方启用了 CheckForOverflowUnderflow 编译选项，
//     或在 checked 块中调用，也不会影响这些操作。
//
// 仅提供两种基础运算：
//   1) 64×64 → 128 有符号乘法，用于 FP64 乘法和 FPMath 的查表插值。
//   2) 128 ÷ 64 → 64 带余数的无符号除法，用于 FP64 除法、Sqrt、
//      查找表索引计算和 MakeRatio。

using System;

namespace Framework.FixedPoint
{
    /// <summary>
    /// FP64 / FPMath 内部使用的 128 位整数辅助：所有运算都只依赖 <see cref="long"/> /
    /// <see cref="ulong"/>，因此可同时跑在 .NET 8（服务器）与 Unity Mono / IL2CPP
    /// （.NET Standard 2.1）上，不依赖 .NET 7+ 的 <c>System.Int128</c>。
        /// 所有需要二进制补码回绕的步骤均显式使用 <c>unchecked</c>，
    /// 与调用方的 <c>checked</c>/<c>unchecked</c> 上下文解耦，跨运行时位级一致。
    /// </summary>
    internal static class Math128
    {
        /// <summary>
        /// 有符号 64×64 → 128 乘法，结果以 <c>(high, low)</c> 二元组形式返回，
        /// 满足 <c>value = ((long)high) * 2^64 + low</c>（128 位二补码）。
        /// </summary>
        internal static void MulFull(long a, long b, out ulong high, out ulong low)
        {
            // 全段显式 unchecked：取负公式 ~x + 1（x 为 0 时回绕到 0）、
            // ulong→long 隐式转换、以及 high 的累加在 checked 上下文里都会抛 OverflowException。
            unchecked
            {
                bool neg = (a < 0) ^ (b < 0);
                ulong ua = a < 0 ? (~(ulong)a + 1UL) : (ulong)a;
                ulong ub = b < 0 ? (~(ulong)b + 1UL) : (ulong)b;

                // 按竖式乘法计算 32×32 部分积，每个部分积均可放入 64 位整数。
                ulong aLo = ua & 0xFFFFFFFFUL;
                ulong aHi = ua >> 32;
                ulong bLo = ub & 0xFFFFFFFFUL;
                ulong bHi = ub >> 32;

                ulong p00 = aLo * bLo;
                ulong p01 = aLo * bHi;
                ulong p10 = aHi * bLo;
                ulong p11 = aHi * bHi;

                ulong middle = (p00 >> 32) + (p01 & 0xFFFFFFFFUL) + (p10 & 0xFFFFFFFFUL);
                low = (p00 & 0xFFFFFFFFUL) | (middle << 32);
                high = p11 + (p01 >> 32) + (p10 >> 32) + (middle >> 32);

                if (neg)
                {
                    // 对 128 位整数按二进制补码规则取负。
                    ulong newLow = ~low + 1UL;
                    ulong newHigh = ~high + (newLow == 0UL ? 1UL : 0UL);
                    low = newLow;
                    high = newHigh;
                }
            }
        }

        /// <summary>
        /// 把 128 位有符号值 <c>(high, low)</c> 算术右移 <paramref name="shift"/> 位后
        /// 截取低 64 位（与 <c>(long)(Int128 &gt;&gt; shift)</c> 行为等价）。
        /// 仅支持 <c>0 &lt;= shift &lt;= 63</c>，覆盖本库的所有调用点。
        /// </summary>
        internal static long Shr128ToLong(ulong high, ulong low, int shift)
        {
            // ulong → long 在 checked 下对 > long.MaxValue 的位模式会抛。
            unchecked
            {
                if (shift == 0)
                {
                    return (long)low;
                }
                // 将 high 的低位填入结果的高位部分，
                // 将 low 右移后填入结果的低位部分。
                ulong combined = (low >> shift) | (high << (64 - shift));
                return (long)combined;
            }
        }

        /// <summary>
        /// 无符号 128÷64 → 64 整除，附带 64 位余数。
        /// 实现采用比特级长除法（128 次迭代），简单可靠；性能足以满足客户端预测路径。
        /// 调用方需保证商能装进 64 位（即 <paramref name="nHi"/> &lt; <paramref name="d"/>），
        /// 否则商高 64 位会被丢弃。
        /// </summary>
        /// <exception cref="DivideByZeroException"><paramref name="d"/> 为 0。</exception>
        internal static ulong DivU128By64(ulong nHi, ulong nLo, ulong d, out ulong remainder)
        {
            if (d == 0UL)
            {
                throw new DivideByZeroException();
            }

            // 快速路径：被除数可放入 64 位整数时，直接使用原生除法和取余运算。
            if (nHi == 0UL)
            {
                remainder = nLo % d;
                return nLo / d;
            }

            // 长除法主循环：最高位被截掉时，newR - d 是 ulong 下的回绕减法。
            // （数学上 (2r+bit) - d ≥ 0，因为 2r+bit 是 65 位整数且 ≥ d；在 ulong 里
            // 表现为下溢，需要显式 unchecked 绕开 checked 检查）。
            unchecked
            {
                ulong q = 0UL;
                ulong r = 0UL;

                // 高 64 位只更新余数 r；此处产生的商位会超出 64 位，因此丢弃。
                // 调用方须保证最终商能放入 64 位整数。
                for (int i = 63; i >= 0; i--)
                {
                    ulong bit = (nHi >> i) & 1UL;
                    ulong topBitOfR = r >> 63;
                    ulong newR = (r << 1) | bit;
                    if (topBitOfR != 0UL || newR >= d)
                    {
                        r = newR - d;
                    }
                    else
                    {
                        r = newR;
                    }
                }

                // 处理低 64 位时，开始记录商的各个位。
                for (int i = 63; i >= 0; i--)
                {
                    ulong bit = (nLo >> i) & 1UL;
                    ulong topBitOfR = r >> 63;
                    ulong newR = (r << 1) | bit;
                    if (topBitOfR != 0UL || newR >= d)
                    {
                        r = newR - d;
                        q |= 1UL << i;
                    }
                    else
                    {
                        r = newR;
                    }
                }

                remainder = r;
                return q;
            }
        }
    }
}
