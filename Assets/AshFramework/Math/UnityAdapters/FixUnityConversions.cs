using System;
using UnityEngine;

namespace AshFramework.Math.Unity
{
    /// <summary>
    /// 提供显式的表现与输入转换边界；将浮点数转为定点数，
    /// 不会使此前的浮点模拟获得确定性。输入量化后应分发相同的原始值。
    /// </summary>
    public static class FixUnityConversions
    {
        public static float ToSingle(this Fix64 value)
        {
            return (float)((double)value.RawValue / Fix64.Scale);
        }

        public static double ToDouble(this Fix64 value)
        {
            return (double)value.RawValue / Fix64.Scale;
        }

        public static Fix64 ToFix64(this float value)
        {
            return ToFix64((double)value);
        }

        public static Fix64 ToFix64(this double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return Fix64.FromRaw(checked((long)(value * Fix64.Scale)));
        }

        public static Vector2 ToUnityVector(this FixVector2 value)
        {
            return new Vector2(value.X.ToSingle(), value.Y.ToSingle());
        }

        public static Vector3 ToUnityVector(this FixVector3 value)
        {
            return new Vector3(value.X.ToSingle(), value.Y.ToSingle(), value.Z.ToSingle());
        }

        public static Quaternion ToUnityQuaternion(this FixQuaternion value)
        {
            return new Quaternion(value.X.ToSingle(), value.Y.ToSingle(), value.Z.ToSingle(), value.W.ToSingle());
        }

        public static FixVector2 ToFixVector(this Vector2 value)
        {
            return new FixVector2(value.x.ToFix64(), value.y.ToFix64());
        }

        public static FixVector3 ToFixVector(this Vector3 value)
        {
            return new FixVector3(value.x.ToFix64(), value.y.ToFix64(), value.z.ToFix64());
        }

        public static FixQuaternion ToFixQuaternion(this Quaternion value)
        {
            return new FixQuaternion(value.x.ToFix64(), value.y.ToFix64(), value.z.ToFix64(), value.w.ToFix64());
        }
    }
}
