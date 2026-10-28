using System;
using System.Collections.Generic;
using AshFramework.Math.Unity;
using UnityEngine;

namespace AshFramework.Collide.Unity
{
    /// <summary>
    /// Unity 数据准备入口。显式注入空的独占索引后，由外部逻辑帧调用 Step。
    /// 不自动检测，不创建默认空间算法；世界范围使用绝对平面坐标，与本对象 Transform 无关。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("AshFramework/Collide/Collision World")]
    public sealed class CollisionWorldBehaviour : MonoBehaviour
    {
        [SerializeField]
        private CollisionPlane _plane = CollisionPlane.XZ;

        [SerializeField]
        private Vector2 _boundsCenter = Vector2.zero;

        [SerializeField]
        private Vector2 _boundsHalfExtents = new Vector2(100, 100);

        // Unity 绑定与核心身份的唯一映射。未初始化的绑定只有记录，没有有效 ID。
        private readonly Dictionary<CollisionBodyBehaviour, BodyRegistration> _bodies =
            new Dictionary<CollisionBodyBehaviour, BodyRegistration>();

        // 遍历前复用快照，允许准备阶段清理解绑、换世界及已销毁对象。
        private readonly List<CollisionBodyBehaviour> _bodyBuffer = new List<CollisionBodyBehaviour>();
        private CollisionWorld _core;
        private IBroadPhase2D _broadPhase;
        private CollisionPlane _initializedPlane;
        private Vector2 _initializedCenter;
        private Vector2 _initializedHalfExtents;
        private bool _stepping;

        private sealed class BodyRegistration
        {
            internal CollisionBodyId Id;
            internal string Error = string.Empty;
        }

        public bool IsInitialized => _core != null;
        public CollisionPlane Plane => _plane;
        public Vector2 BoundsCenter => _boundsCenter;
        public Vector2 BoundsHalfExtents => _boundsHalfExtents;
        public int BoundBodyCount => _bodies.Count;
        public string LastError { get; private set; } = string.Empty;

        /// <summary>初始化前配置固定范围；Vector2 的第二维分别对应 Y 或 Z。</summary>
        public void Configure(CollisionPlane plane, Vector2 boundsCenter, Vector2 boundsHalfExtents)
        {
            EnsureNotStepping();
            if (IsInitialized)
            {
                throw new InvalidOperationException("World configuration is fixed after initialization.");
            }

            ReadBounds(plane, boundsCenter, boundsHalfExtents);
            _plane = plane;
            _boundsCenter = boundsCenter;
            _boundsHalfExtents = boundsHalfExtents;
        }

        /// <summary>只初始化一次；同时发现显式绑定的禁用/未激活场景 Body。</summary>
        public void Initialize(IBroadPhase2D broadPhase)
        {
            EnsureNotStepping();
            if (IsInitialized)
            {
                throw new InvalidOperationException("World has already been initialized.");
            }

            Aabb2D bounds = ReadBounds(_plane, _boundsCenter, _boundsHalfExtents);
            _core = new CollisionWorld(bounds, broadPhase);
            _broadPhase = broadPhase;
            _initializedPlane = _plane;
            _initializedCenter = _boundsCenter;
            _initializedHalfExtents = _boundsHalfExtents;
            foreach (BodyRegistration registration in _bodies.Values)
            {
                registration.Id = _core.RegisterBody();
            }

            RefreshBindings();
            LastError = string.Empty;
        }

        /// <summary>
        /// 低频发现入口，包含禁用对象。初始化自动调用；绕过 BindWorld 的批量序列化改动后显式调用。
        /// 不在每步执行全场景查找；运行时新建/换世界应使用 Body.BindWorld。
        /// </summary>
        public void RefreshBindings()
        {
            EnsureNotStepping();
            ReconcileTrackedBodies();
            CollisionBodyBehaviour[] candidates = Resources.FindObjectsOfTypeAll<CollisionBodyBehaviour>();
            foreach (CollisionBodyBehaviour body in candidates)
            {
                if (body.gameObject.scene.IsValid() && body.gameObject.scene.isLoaded && body.World == this)
                {
                    body.SynchronizeWorldBinding();
                }
            }
        }

        public bool TryGetBodyId(CollisionBodyBehaviour body, out CollisionBodyId id)
        {
            id = default;
            if (body == null || !_bodies.TryGetValue(body, out BodyRegistration registration))
            {
                return false;
            }

            id = registration.Id;
            return id.IsValid;
        }

        public bool IsBodyActive(CollisionBodyBehaviour body)
        {
            return TryGetBodyId(body, out CollisionBodyId id) && _core.IsBodyActive(id);
        }

        public string GetBodyError(CollisionBodyBehaviour body)
        {
            if (body == null || !_bodies.TryGetValue(body, out BodyRegistration registration))
            {
                throw new ArgumentException("Body is not bound to this world.", nameof(body));
            }

            return registration.Error;
        }

        /// <summary>先准备所有对象，再统一同步索引及检测；失败时结果为空。Body 数据错误不终止其他对象。</summary>
        public bool Step(List<CollisionPair> results, out string error)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            EnsureNotStepping();
            results.Clear();
            ReconcileTrackedBodies();
            if (!IsInitialized)
            {
                return Fail("World is not initialized; inject an IBroadPhase2D first.", out error);
            }

            if (!isActiveAndEnabled)
            {
                return Fail("World is disabled or inactive.", out error);
            }

            if (_plane != _initializedPlane || !_boundsCenter.Equals(_initializedCenter)
                || !_boundsHalfExtents.Equals(_initializedHalfExtents))
            {
                SuspendAllBodies();
                return Fail("World configuration changed after initialization; restore the fixed configuration.", out error);
            }

            _stepping = true;
            try
            {
                foreach (KeyValuePair<CollisionBodyBehaviour, BodyRegistration> entry in _bodies)
                {
                    PrepareBody(entry.Key, entry.Value);
                }

                bool success = _core.Step(results, out error);
                LastError = error;
                return success;
            }
            finally
            {
                _stepping = false;
            }
        }

        internal void EnsureNotStepping()
        {
            if (_stepping)
            {
                throw new InvalidOperationException("Cannot change Unity world bindings during Step.");
            }
        }

        internal void AttachBody(CollisionBodyBehaviour body)
        {
            EnsureNotStepping();
            if (!_bodies.ContainsKey(body))
            {
                _bodies.Add(body, new BodyRegistration
                {
                    Id = _core == null ? default : _core.RegisterBody()
                });
            }
        }

        internal void DetachBody(CollisionBodyBehaviour body)
        {
            // Unity 生命周期可能来自检测期间的外部销毁；下一步会清理失效引用。
            if (_stepping)
            {
                return;
            }

            if (_bodies.TryGetValue(body, out BodyRegistration registration))
            {
                if (_core != null)
                {
                    _core.UnregisterBody(registration.Id);
                }

                _bodies.Remove(body);
            }
        }

        internal void SuspendBody(CollisionBodyBehaviour body)
        {
            if (!_stepping && _core != null && _bodies.TryGetValue(body, out BodyRegistration registration))
            {
                _core.SuspendBody(registration.Id);
                registration.Error = "Body is disabled or inactive.";
            }
        }

        private void PrepareBody(CollisionBodyBehaviour body, BodyRegistration registration)
        {
            if (!body.isActiveAndEnabled)
            {
                _core.SuspendBody(registration.Id);
                registration.Error = "Body is disabled or inactive.";
            }
            else if (body.Plane != _initializedPlane)
            {
                _core.SuspendBody(registration.Id);
                registration.Error = "Body plane differs from the world plane.";
            }
            else if (!body.RefreshGeometry())
            {
                _core.SuspendBody(registration.Id);
                registration.Error = body.LastError;
            }
            else
            {
                _core.TrySubmitGeometry(registration.Id, body.Geometry, body.LogicalHeight, out registration.Error);
            }
        }

        private void ReconcileTrackedBodies()
        {
            _bodyBuffer.Clear();
            _bodyBuffer.AddRange(_bodies.Keys);
            foreach (CollisionBodyBehaviour body in _bodyBuffer)
            {
                if (body == null)
                {
                    DetachBody(body);
                }
                else
                {
                    body.SynchronizeWorldBinding();
                }
            }
        }

        private void SuspendAllBodies()
        {
            if (_core == null || _stepping)
            {
                return;
            }

            foreach (BodyRegistration registration in _bodies.Values)
            {
                _core.SuspendBody(registration.Id);
            }
        }

        private static Aabb2D ReadBounds(CollisionPlane plane, Vector2 center, Vector2 halfExtents)
        {
            if (plane != CollisionPlane.XY && plane != CollisionPlane.XZ)
            {
                throw new ArgumentOutOfRangeException(nameof(plane));
            }

            return new Aabb2D(center.ToFixVector(), halfExtents.ToFixVector());
        }

        private bool Fail(string message, out string error)
        {
            LastError = message;
            error = message;
            return false;
        }

        private void OnDisable()
        {
            SuspendAllBodies();
        }

        private void OnDestroy()
        {
            // 清空本世界独占的索引；不替调用方 Dispose 其可能拥有的其他资源。
            try
            {
                _broadPhase?.Clear();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }

            _bodies.Clear();
            _bodyBuffer.Clear();
            _core = null;
            _broadPhase = null;
        }
    }
}
