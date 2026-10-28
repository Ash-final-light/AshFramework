using System;
using System.Collections.Generic;
using UnityEngine;
using HeightLevel = AshFramework.Collide.LogicalHeight;

namespace AshFramework.Collide.Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("AshFramework/Collide/Collision Body")]
    public sealed class CollisionBodyBehaviour : MonoBehaviour
    {
        [SerializeField]
        private CollisionWorldBehaviour _world;

        // 实际接入的世界，用于清理旧绑定；不在 Body 上重复保存核心 ID。
        private CollisionWorldBehaviour _boundWorld;

        [SerializeField]
        private CollisionPlane _plane = CollisionPlane.XZ;

        [SerializeField]
        private HeightLevel _logicalHeight = HeightLevel.Level1;

        private readonly List<CollisionShapeBehaviour> _shapes = new List<CollisionShapeBehaviour>();
        private readonly List<WorldShape2D> _prepared = new List<WorldShape2D>();
        private readonly CompoundShape2D _geometry = new CompoundShape2D();
        private bool _needsRebuild = true;
        private bool _hasGeometry;

        public string LastError { get; private set; } = string.Empty;
        public int ShapeCount => _shapes.Count;
        public bool HasGeometry => _hasGeometry && !_needsRebuild && isActiveAndEnabled;
        public CollisionWorldBehaviour World => _world;

        /// <summary>主线程显式绑定；禁用对象也可绑定，换世界时旧身份立即失效。</summary>
        public void BindWorld(CollisionWorldBehaviour world)
        {
            if (_boundWorld != null)
            {
                _boundWorld.EnsureNotStepping();
            }

            if (world != null)
            {
                world.EnsureNotStepping();
            }

            _world = world;
            SynchronizeWorldBinding();
        }

        // OnValidate 只标脏；由 Inspector、生命周期或世界准备阶段在主线程协调绑定。
        internal void SynchronizeWorldBinding()
        {
            if (_boundWorld != _world)
            {
                if (_boundWorld != null)
                {
                    _boundWorld.DetachBody(this);
                }

                _boundWorld = _world;
            }

            if (_boundWorld != null)
            {
                _boundWorld.AttachBody(this);
            }
        }

        public CollisionPlane Plane
        {
            get => _plane;
            set
            {
                if (value != CollisionPlane.XY && value != CollisionPlane.XZ)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _plane = value;
                RequestRebuild();
            }
        }

        public HeightLevel LogicalHeight
        {
            get => _logicalHeight;
            set
            {
                if (!IsValidHeight(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _logicalHeight = value;
            }
        }

        /// <summary>最后成功刷新后的拥有者缓存，不应由外部调用者修改。</summary>
        public CompoundShape2D Geometry
        {
            get
            {
                if (!HasGeometry)
                {
                    throw new InvalidOperationException("Refresh body geometry before reading it.");
                }

                return _geometry;
            }
        }

        public CollisionShapeBehaviour GetShape(int index)
        {
            return _shapes[index];
        }

        /// <summary>仅标记脏状态，可安全从 OnValidate 使用。</summary>
        public void RequestRebuild()
        {
            _needsRebuild = true;
        }

        public void RebuildShapes()
        {
            _geometry.Clear();
            _hasGeometry = false;
            _shapes.Clear();
            CollisionShapeBehaviour[] candidates = GetComponentsInChildren<CollisionShapeBehaviour>(true);
            foreach (CollisionShapeBehaviour shape in candidates)
            {
                if (shape.isActiveAndEnabled && shape.Owner == this)
                {
                    _shapes.Add(shape);
                }
            }

            _needsRebuild = false;
        }

        /// <summary>
        /// 在检测前显式刷新。移动只重建世界几何；增删或深层换父后调用 RebuildShapes。
        /// 失败时清空缓存并记录错误，不保留半个复合体或旧碰撞范围。
        /// </summary>
        public bool RefreshGeometry()
        {
            if (!isActiveAndEnabled)
            {
                return Fail("Body is disabled or inactive.");
            }

            if ((_plane != CollisionPlane.XY && _plane != CollisionPlane.XZ) || !IsValidHeight(_logicalHeight))
            {
                return Fail("Body plane or logical height is invalid.");
            }

            if (_needsRebuild)
            {
                RebuildShapes();
            }

            if (_shapes.Count == 0)
            {
                return Fail("Body has no active owned shapes.");
            }

            _prepared.Clear();
            foreach (CollisionShapeBehaviour shape in _shapes)
            {
                if (shape == null || !shape.isActiveAndEnabled || shape.Owner != this)
                {
                    RequestRebuild();
                    return Fail("Shape ownership changed; rebuild the body shape list.");
                }

                if (!shape.TryBuildWorldShape(_plane, out WorldShape2D worldShape, out string error))
                {
                    return Fail(shape.name + ": " + error);
                }

                _prepared.Add(worldShape);
            }

            try
            {
                _geometry.SetShapes(_prepared);
            }
            catch (ArgumentException exception)
            {
                return Fail(exception.Message);
            }
            catch (OverflowException exception)
            {
                return Fail(exception.Message);
            }

            _hasGeometry = true;
            LastError = string.Empty;
            return true;
        }

        public bool TryGetBounds(out Aabb2D bounds)
        {
            bounds = default;
            return HasGeometry && _geometry.TryGetBounds(out bounds);
        }

        private bool Fail(string error)
        {
            _geometry.Clear();
            _prepared.Clear();
            _hasGeometry = false;
            LastError = error;
            return false;
        }

        private static bool IsValidHeight(HeightLevel height)
        {
            return height == HeightLevel.Level1 || height == HeightLevel.Level2 || height == HeightLevel.Level3;
        }

        private void OnEnable()
        {
            RequestRebuild();
            RefreshGeometry();
            SynchronizeWorldBinding();
        }

        private void OnDisable()
        {
            Fail("Body is disabled or inactive.");
            RequestRebuild();
            if (_boundWorld != null)
            {
                _boundWorld.SuspendBody(this);
            }
        }

        private void OnDestroy()
        {
            if (_boundWorld != null)
            {
                _boundWorld.DetachBody(this);
            }

            _boundWorld = null;
        }

        private void OnValidate()
        {
            RequestRebuild();
        }

        private void OnTransformChildrenChanged()
        {
            RequestRebuild();
        }

        [ContextMenu("Refresh Collision Geometry")]
        private void RefreshFromContextMenu()
        {
            RebuildShapes();
            RefreshGeometry();
        }
    }
}
