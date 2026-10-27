using System;
using AshFramework.Math;
using UnityEngine;

namespace AshFramework.Collide.Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("AshFramework/Collide/Circle Shape")]
    public sealed class CircleShapeBehaviour : CollisionShapeBehaviour
    {
        [SerializeField]
        [Min(0)]
        private float _radius = 0.5f;

        public float Radius
        {
            get => _radius;
            set
            {
                if (!ShapeGeometryPreparation.IsFinite(value) || value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _radius = value;
                NotifyConfigurationChanged();
            }
        }

        protected override WorldShape2D CreateWorldShape(FixTransform2D pose, FixVector2 scale)
        {
            Fix64 maximum = FixMath.Max(scale.X, scale.Y);
            if (FixMath.Abs(scale.X - scale.Y) > maximum * Fix64.FromRatio(1, 100000))
            {
                throw new InvalidOperationException("A circle requires uniform in-plane world scale.");
            }

            if (!ShapeGeometryPreparation.IsFinite(_radius) || _radius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Radius));
            }

            Fix64 radius = AshFramework.Math.Unity.FixUnityConversions.ToFix64(_radius) * maximum;
            return WorldShape2D.FromCircle(new Circle2D(pose.Position, radius));
        }
    }
}
