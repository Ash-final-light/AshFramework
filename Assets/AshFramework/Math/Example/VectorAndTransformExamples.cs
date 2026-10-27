using System;

namespace AshFramework.Math.Example
{
    /// <summary>展示向量、旋转、矩阵、二维变换及通用几何计算。</summary>
    public static class VectorAndTransformExamples
    {
        public static void Run(Action<string> log)
        {
            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            log("【向量、旋转与变换】");

            FixVector2 vector = new FixVector2(3, 4);
            FixVector2 direction = vector.Normalized;
            log($"向量 {vector} 的长度={vector.Magnitude}，单位方向约为 (0.6,0.8)：{direction}");

            // 目标与当前位置重合时不能获得方向，TryNormalize 会明确返回 false。
            bool hasDirection = FixVector2.Zero.TryNormalize(out FixVector2 zeroDirection);
            FixVector2 fallback = FixVector2.Zero.NormalizeOrFallback(FixVector2.Right);
            log($"零向量归一化成功：{hasDirection}；输出={zeroDirection}；后备方向={fallback}");

            FixVector2 moved = FixVector2.MoveTowards(FixVector2.Zero, new FixVector2(10, 0), 3);
            FixVector2 limited = FixVector2.ClampMagnitude(vector, 2);
            log($"向 (10,0) 最多移动 3 个单位：{moved}；限制 (3,4) 的长度为 2：{limited}");

            // 仅判断范围时使用 WithinDistance，避免大距离平方收窄到 Fix64 后溢出。
            bool withinRange = FixVector3.WithinDistance(FixVector3.Zero, new FixVector3(3, 0, 4), 5);
            log($"(3,0,4) 是否在原点的 5 单位范围内：{withinRange}");

            FixVector3 projected = FixVector3.ProjectOnPlane(new FixVector3(2, 3, 4), FixVector3.Up);
            FixVector2 reflected = FixVector2.Reflect(new FixVector2(1, -1), FixVector2.Up);
            log($"投影到法线为 Up 的平面：{projected}；二维反射方向：{reflected}");

            FixQuaternion rotation = FixQuaternion.AngleAxis(FixMath.HalfPi, FixVector3.Forward);
            FixVector3 rotated = rotation * FixVector3.Right;
            FixVector3 restored = rotation.Inverse() * rotated;
            log($"绕 Z 轴旋转 90 度后 Right 约为 Up：{rotated}；逆旋转约为 Right：{restored}");

            // 四元数角度和向量角度均使用弧度，显示度数时再做转换。
            FixQuaternion halfway = FixQuaternion.Slerp(FixQuaternion.Identity, rotation, Fix64.Half);
            Fix64 halfwayAngle = FixQuaternion.Angle(FixQuaternion.Identity, halfway);
            log($"单位旋转到 90 度旋转的中点约为 {FixMath.RadiansToDegrees(halfwayAngle)} 度");

            FixMatrix3x3 matrix = FixMatrix3x3.FromQuaternion(rotation);
            FixVector3 matrixRotated = matrix * FixVector3.Right;
            log($"同一旋转矩阵得到的方向：{matrixRotated}");

            // 二维点先缩放，再逆时针旋转，最后平移；(1,0) 预期变成约 (10,22)。
            FixTransform2D parent = new FixTransform2D(new FixVector2(10, 20),
                FixMath.HalfPi, new FixVector2(2, 3));
            FixVector2 world = parent.TransformPoint(FixVector2.Right);
            FixVector2 local = parent.InverseTransformPoint(world);
            log($"局部点 (1,0) 转世界坐标：{world}；转回局部坐标：{local}");

            // 非均匀缩放与旋转组合可能产生剪切，因此组合结果保留为矩阵。
            FixTransform2D child = new FixTransform2D(new FixVector2(1, 2),
                FixMath.DegreesToRadians(30), FixVector2.One);
            FixMatrix3x3 combined = parent.Compose(child);
            log($"父子组合后的点：{combined.TransformPoint(FixVector2.Right)}");

            FixVector2 nearest = FixGeometry.ClosestPointOnSegment(new FixVector2(2, 3),
                FixVector2.Zero, new FixVector2(4, 0));
            log($"点 (2,3) 到线段 [(0,0),(4,0)] 的最近点：{nearest}");

            bool hasWeights = FixGeometry.TryBarycentric(new FixVector2(Fix64.Half, Fix64.Half),
                FixVector2.Zero, new FixVector2(2, 0), new FixVector2(0, 2), out FixVector3 weights);
            log($"三角形重心坐标计算成功：{hasWeights}；权重预期为 (0.5,0.25,0.25)：{weights}");

            FixVector2 curvePoint = FixGeometry.QuadraticBezier(FixVector2.Zero,
                new FixVector2(1, 2), new FixVector2(2, 0), Fix64.Half);
            log($"二次 Bezier 的中点预期为 (1,1)：{curvePoint}");
        }
    }
}
