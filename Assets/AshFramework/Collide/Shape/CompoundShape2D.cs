using System;
using System.Collections.Generic;
using System.Numerics;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>
    /// 单一拥有者更新的复合体容器。复制输入并复用内部容量，不暴露可写数组；不支持并发更新。
    /// 空容器无包围盒，与任何复合体均不重叠。
    /// </summary>
    public sealed class CompoundShape2D
    {
        private WorldShape2D[] _shapes = Array.Empty<WorldShape2D>();
        private Aabb2D _bounds;

        public int Count { get; private set; }

        public WorldShape2D this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _shapes[index];
            }
        }

        /// <summary>先验证所有输入及合并范围，失败时保持原数据不变。</summary>
        public void SetShapes(IReadOnlyList<WorldShape2D> shapes)
        {
            if (shapes == null)
            {
                throw new ArgumentNullException(nameof(shapes));
            }

            int count = shapes.Count;
            if (count == 0)
            {
                Clear();
                return;
            }

            Aabb2D first = shapes[0].GetBounds();
            FixVector2 min = first.Min;
            FixVector2 max = first.Max;
            for (int i = 1; i < count; i++)
            {
                Aabb2D bounds = shapes[i].GetBounds();
                FixVector2 shapeMin = bounds.Min;
                FixVector2 shapeMax = bounds.Max;
                min = new FixVector2(FixMath.Min(min.X, shapeMin.X), FixMath.Min(min.Y, shapeMin.Y));
                max = new FixVector2(FixMath.Max(max.X, shapeMax.X), FixMath.Max(max.Y, shapeMax.Y));
            }

            MergeAxis(min.X.RawValue, max.X.RawValue, out Fix64 centerX, out Fix64 extentX);
            MergeAxis(min.Y.RawValue, max.Y.RawValue, out Fix64 centerY, out Fix64 extentY);
            Aabb2D merged = new Aabb2D(new FixVector2(centerX, centerY), new FixVector2(extentX, extentY));
            if (_shapes.Length < count)
            {
                _shapes = new WorldShape2D[count];
            }

            for (int i = 0; i < count; i++)
            {
                _shapes[i] = shapes[i];
            }

            if (Count > count)
            {
                Array.Clear(_shapes, count, Count - count);
            }

            Count = count;
            _bounds = merged;
        }

        public bool TryGetBounds(out Aabb2D bounds)
        {
            bounds = _bounds;
            return Count > 0;
        }

        public void Clear()
        {
            Array.Clear(_shapes, 0, Count);
            Count = 0;
            _bounds = default;
        }

        private static void MergeAxis(long min, long max, out Fix64 center, out Fix64 extent)
        {
            BigInteger half = ((BigInteger)max - min + 1) / 2;
            BigInteger midpoint = (BigInteger)min + half;
            if (midpoint + half > long.MaxValue)
            {
                midpoint = (BigInteger)max - half;
            }

            // 奇数 raw 跨度需要向一侧多覆盖一个单位；最终半尺寸仍须可表示。
            if (half > long.MaxValue || midpoint - half < long.MinValue)
            {
                throw new OverflowException("Compound bounds cannot be represented by Aabb2D.");
            }

            center = Fix64.FromRaw((long)midpoint);
            extent = Fix64.FromRaw((long)half);
        }
    }
}
