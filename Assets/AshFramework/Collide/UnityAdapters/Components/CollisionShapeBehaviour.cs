using System;
using AshFramework.Math;
using UnityEngine;

namespace AshFramework.Collide.Unity
{
    /// <summary>形状配置基类。局部中心位于当前 Shape 的局部 XY 或 XZ 平面。</summary>
    public abstract class CollisionShapeBehaviour : MonoBehaviour
    {
        [SerializeField]
        private Vector2 _localCenter;

        private CollisionBodyBehaviour _lastOwner;

        public Vector2 LocalCenter
        {
            get => _localCenter;
            set
            {
                if (!ShapeGeometryPreparation.IsFinite(value.x) || !ShapeGeometryPreparation.IsFinite(value.y))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _localCenter = value;
                NotifyConfigurationChanged();
            }
        }

        public CollisionBodyBehaviour Owner
        {
            get
            {
                for (Transform current = transform; current != null; current = current.parent)
                {
                    CollisionBodyBehaviour body = current.GetComponent<CollisionBodyBehaviour>();
                    if (body != null)
                    {
                        return body;
                    }
                }

                return null;
            }
        }

        public bool TryBuildWorldShape(CollisionPlane plane, out WorldShape2D shape, out string error)
        {
            shape = default;
            error = string.Empty;
            if (!isActiveAndEnabled)
            {
                error = "Shape is disabled or inactive.";
                return false;
            }

            if (!ShapeGeometryPreparation.TryReadFrame(transform, _localCenter, plane,
                out FixTransform2D pose, out FixVector2 scale, out error))
            {
                return false;
            }

            try
            {
                shape = CreateWorldShape(pose, scale);
                return true;
            }
            catch (ArgumentException exception)
            {
                error = exception.Message;
                return false;
            }
            catch (OverflowException exception)
            {
                error = exception.Message;
                return false;
            }
            catch (InvalidOperationException exception)
            {
                error = exception.Message;
                return false;
            }
        }

        protected abstract WorldShape2D CreateWorldShape(FixTransform2D pose, FixVector2 scale);

        protected void NotifyConfigurationChanged()
        {
            if (_lastOwner != null)
            {
                _lastOwner.RequestRebuild();
            }

            _lastOwner = Owner;
            if (_lastOwner != null)
            {
                _lastOwner.RequestRebuild();
            }
        }

        protected virtual void OnEnable()
        {
            NotifyConfigurationChanged();
        }

        protected virtual void OnDisable()
        {
            NotifyConfigurationChanged();
        }

        protected virtual void OnTransformParentChanged()
        {
            NotifyConfigurationChanged();
        }

        protected virtual void OnDestroy()
        {
            if (_lastOwner != null)
            {
                _lastOwner.RequestRebuild();
            }

            _lastOwner = null;
        }

        protected virtual void OnValidate()
        {
            // OnValidate 可能由加载线程调用，只改已有拥有者的托管脏标记。
            if (!ReferenceEquals(_lastOwner, null))
            {
                _lastOwner.RequestRebuild();
            }
        }
    }
}
