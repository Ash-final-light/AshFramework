using System;
using AshFramework.Math;
using AshFramework.Math.Unity;
using UnityEngine;

namespace AshFramework.Collide.Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("AshFramework/Collide/Box Shape")]
    public sealed class BoxShapeBehaviour : CollisionShapeBehaviour
    {
        [SerializeField]
        private Vector2 _halfExtents = new Vector2(0.5f, 0.5f);

        public Vector2 HalfExtents
        {
            get => _halfExtents;
            set
            {
                if (!ShapeGeometryPreparation.IsFinite(value.x) || !ShapeGeometryPreparation.IsFinite(value.y)
                    || value.x <= 0 || value.y <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _halfExtents = value;
                NotifyConfigurationChanged();
            }
        }

        protected override WorldShape2D CreateWorldShape(FixTransform2D pose, FixVector2 scale)
        {
            if (!ShapeGeometryPreparation.IsFinite(_halfExtents.x) || !ShapeGeometryPreparation.IsFinite(_halfExtents.y)
                || _halfExtents.x <= 0 || _halfExtents.y <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(HalfExtents));
            }

            FixVector2 halfExtents = FixVector2.Scale(_halfExtents.ToFixVector(), scale);
            Obb2D box = new Obb2D(pose.Position, halfExtents, pose.Rotation);
            if (box.AxisX.X == Fix64.Zero || box.AxisX.Y == Fix64.Zero)
            {
                return WorldShape2D.FromAabb(box.GetBounds());
            }

            return WorldShape2D.FromObb(box);
        }
    }
}
