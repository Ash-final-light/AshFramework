using System;

namespace AshFramework.Math.Example
{
    /// <summary>
    /// 展示定点数的构造、运算、取整、插值和角度计算。
    /// 使用整数、分数或十进制文本构造逻辑数据，避免先经过浮点计算。
    /// </summary>
    public static class ScalarExamples
    {
        public static void Run(Action<string> log)
        {
            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            log("【定点数与标量计算】");

            // 三种构造方式都表示 1.5；FromRaw 接收 Q32.32 格式的原始整数。
            Fix64 fromRatio = Fix64.FromRatio(3, 2);
            Fix64 fromText = Fix64.Parse("1.5");
            Fix64 fromRaw = Fix64.FromRaw(6442450944L);
            log($"分数构造={fromRatio}，文本构造={fromText}，原始值构造={fromRaw}");
            log($"三种构造结果一致：{fromRatio == fromText && fromText == fromRaw}");

            Fix64 sum = fromRatio + 2;
            Fix64 product = fromRatio * 2;
            Fix64 quotient = Fix64.FromInt(3) / 2;
            log($"1.5+2={sum}，1.5×2={product}，3÷2={quotient}");

            // 负数取整必须区分向下、向上、向零和四舍五入。
            Fix64 negative = Fix64.Parse("-1.5");
            log($"-1.5 取整：向下={negative.FloorToInt()}，向上={negative.CeilToInt()}，"
                + $"向零={negative.TruncateToInt()}，四舍五入={negative.RoundToInt()}");

            Fix64 limited = FixMath.Clamp(12, 0, 10);
            Fix64 midpoint = FixMath.Lerp(0, 10, Fix64.Half);
            Fix64 extrapolated = FixMath.LerpUnclamped(0, 10, 2);
            Fix64 next = FixMath.MoveTowards(0, 10, 3);
            log($"限制 12 到 [0,10]：{limited}；中点={midpoint}；外插={extrapolated}；移动一步={next}");
            log($"平方根 sqrt(9)={FixMath.Sqrt(9)}；整数幂 2^-3={FixMath.Pow(2, -3)}");

            // 角度工具使用弧度；外部度数应显式转换，不能直接传给 Sin 或 Cos。
            Fix64 radians = FixMath.DegreesToRadians(30);
            FixMath.SinCos(radians, out Fix64 sin, out Fix64 cos);
            log($"30 度的 sin 约为 0.5：{sin}；cos 约为 0.866025：{cos}");

            Fix64 currentAngle = FixMath.DegreesToRadians(350);
            Fix64 targetAngle = FixMath.DegreesToRadians(10);
            Fix64 delta = FixMath.DeltaAngle(currentAngle, targetAngle);
            Fix64 nextAngle = FixMath.MoveTowardsAngle(currentAngle, targetAngle, FixMath.DegreesToRadians(5));
            log($"350 度转向 10 度：最短角差约为 {FixMath.RadiansToDegrees(delta)} 度");
            log($"最多转动 5 度后的朝向约为 {FixMath.RadiansToDegrees(nextAngle)} 度，等价于 355 度");

            // 序列化用精确文本或 RawValue；显示用的小数格式不保证按位往返。
            string exactText = fromRatio.ToString();
            bool parsed = Fix64.TryParse(exactText, out Fix64 restored);
            log($"精确文本往返成功：{parsed && restored.RawValue == fromRatio.RawValue}；RawValue={restored.RawValue}");
        }
    }
}
