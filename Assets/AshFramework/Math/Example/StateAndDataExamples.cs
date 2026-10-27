using System;
using System.Collections.Generic;

namespace AshFramework.Math.Example
{
    /// <summary>展示随机状态恢复、精确累计、二进制读写和稳定校验。</summary>
    public static class StateAndDataExamples
    {
        public static void Run(Action<string> log)
        {
            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            log("【随机、累计与数据读写】");

            FixRandom random = new FixRandom(123);
            ulong savedState = random.State;
            int first = random.Range(-3, 4);
            FixVector2 firstPoint = random.InsideUnitCircle();

            // 恢复状态后必须按相同顺序调用相同方法，才能得到相同序列。
            random.RestoreState(savedState);
            int replay = random.Range(-3, 4);
            FixVector2 replayPoint = random.InsideUnitCircle();
            log($"随机整数恢复一致：{first == replay}；圆内采样恢复一致：{firstPoint == replayPoint}");

            List<int> items = new List<int> { 1, 2, 3, 4, 5 };
            random.Shuffle(items);
            log($"固定种子的洗牌结果：{string.Join(", ", items)}");

            // 将一个单位分成 60 份。保留余数后，60 次累计恰好得到一个单位。
            RationalAccumulator portions = new RationalAccumulator(60);
            Fix64 accumulated = Fix64.Zero;
            for (int index = 0; index < 60; index++)
            {
                accumulated += Fix64.FromRaw(portions.Accumulate(Fix64.Scale));
            }

            log($"60 次余数累计结果={accumulated}；是否精确等于 1：{accumulated == Fix64.One}；余数={portions.Remainder}");

            FixVector3 position = new FixVector3(Fix64.Parse("1.5"), -2, 3);
            FixQuaternion rotation = FixQuaternion.AngleAxis(FixMath.HalfPi, FixVector3.Up);
            byte[] bytes = new byte[FixBinary.ScalarSize + FixBinary.Vector3Size + FixBinary.QuaternionSize];

            // FixBinary 本身不带头部；示例显式写入版本，之后依次写位置和旋转。
            int positionOffset = FixBinary.ScalarSize;
            int rotationOffset = positionOffset + FixBinary.Vector3Size;
            FixBinary.WriteInt64(bytes, 0, Fix64.FormatVersion);
            FixBinary.Write(bytes, positionOffset, position);
            FixBinary.Write(bytes, rotationOffset, rotation);

            long version = FixBinary.ReadInt64(bytes, 0);
            if (version != Fix64.FormatVersion)
            {
                throw new InvalidOperationException("示例数据的定点格式版本不匹配。");
            }

            FixVector3 restoredPosition = FixBinary.ReadVector3(bytes, positionOffset);
            FixQuaternion restoredRotation = FixBinary.ReadQuaternion(bytes, rotationOffset);
            log($"二进制往返：位置完全一致={position == restoredPosition}；旋转完全一致={rotation == restoredRotation}");

            // 由调用者固定字段顺序；若保存更多状态，校验时也应按约定加入。
            StableHash64 originalHash = new StableHash64();
            originalHash.AddInt64(Fix64.FormatVersion);
            originalHash.Add(position);
            originalHash.Add(rotation);
            StableHash64 restoredHash = new StableHash64();
            restoredHash.AddInt64(version);
            restoredHash.Add(restoredPosition);
            restoredHash.Add(restoredRotation);
            log($"原始数据与恢复数据的校验值一致：{originalHash.Value == restoredHash.Value}；校验值={originalHash.Value:X16}");

            // 哈希用于快速定位差异，精确一致性仍应按原始数据验证。
            log("示例数据仅保存在内存中，不会写入文件或发送网络消息。");
        }
    }
}
