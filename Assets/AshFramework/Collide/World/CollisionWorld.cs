using System;
using System.Collections.Generic;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>
    /// 单线程碰撞世界，持有几何副本。提交和生命周期修改仅在 Step 外执行。
    /// 索引由调用方提供空实例并交由本世界独占，不提供默认算法。
    /// </summary>
    public sealed class CollisionWorld : ICollisionWorldSpatialOperations2D
    {
        /// <summary>本世界独有的身份标记。ID 持有此引用，用于拒绝跨世界访问。</summary>
        private readonly object _identity = new object();

        /// <summary>外部注入并由本世界独占的空间索引，只保存 Body ID 和整体 AABB。</summary>
        private readonly IBroadPhase2D _broadPhase;

        /// <summary>有效注册 ID 到 _slots 数组下标的映射；注销时立即删除，使旧 ID 失效。</summary>
        private readonly Dictionary<CollisionBodyId, int> _idToSlot = new Dictionary<CollisionBodyId, int>();

        /// <summary>
        /// 已注册记录的槽位列表，包含暂停的 Body，不包含待回收记录。
        /// 检测遍历此列表，避免扫描数组空槽；删除时用尾项补位，不保证注册顺序。
        /// </summary>
        private readonly List<int> _registered = new List<int>();

        /// <summary>已完成索引清理的空闲槽位栈，注册新 Body 时优先复用其中的记录和容量。</summary>
        private readonly Stack<int> _free = new Stack<int>();

        /// <summary>
        /// 已注销但尚待索引清理的槽位；其 ID 已失效，清理完成前不能进入 _free。
        /// Step 正常移除代理，或失败恢复时 Clear 索引后，再回收这些槽位。
        /// </summary>
        private readonly List<int> _removed = new List<int>();

        /// <summary>单次范围查询的候选 ID 缓冲，每查询一个 Body 前清空并复用。</summary>
        private readonly List<CollisionBodyId> _candidates = new List<CollisionBodyId>();
        private readonly List<CollisionBodyId> _queryResults = new List<CollisionBodyId>();
        private readonly List<CircleSweepShapeHit2D> _sweepShapeHits = new List<CircleSweepShapeHit2D>();
        private readonly List<CircleSweepHit2D> _sweepHits = new List<CircleSweepHit2D>();

        /// <summary>本步精筛命中的规范化配对缓冲；全部检测成功并排序后才复制给调用方。</summary>
        private readonly List<CollisionPair> _pairs = new List<CollisionPair>();
        private readonly List<CompoundContact2D> _compoundContacts = new List<CompoundContact2D>();
        private readonly List<CollisionContact> _contacts = new List<CollisionContact>();
        private readonly CompoundShape2D _teleportBackup = new CompoundShape2D();

        /// <summary>内部记录引用数组，按需扩容；数组下标是可复用的槽位，不是 Body ID。</summary>
        private CollisionBodyRecord[] _slots = new CollisionBodyRecord[16];

        /// <summary>历史分配的最高槽位下标加一，不因回收而减少；不同于容量和当前注册数量。</summary>
        private int _slotCount;

        /// <summary>本世界下一个 ID 数值，从 1 递增，不因槽位复用而回退；0 为无效值。</summary>
        private long _nextId = 1;

        /// <summary>是否正在执行 Step，用于阻止检测中修改世界或嵌套调用 Step；不是线程锁。</summary>
        private bool _stepping;

        /// <summary>
        /// 上次 Step 失败后索引可能只同步了一部分数据；为 true 时，下次必须先 Clear
        /// 并按当前有效记录重新插入，全部同步成功后才能恢复查询。
        /// </summary>
        private bool _needsRebuild;

        /// <summary>固定的世界碰撞覆盖范围，要求 Body 的完整 AABB 位于其中，允许贴边。</summary>
        public Aabb2D Bounds
        {
            get;
        }

        /// <summary>当前注册数量，包含暂停 Body，不包含已注销但尚待回收的记录。</summary>
        public int BodyCount => _idToSlot.Count;

        public CollisionWorld(Aabb2D worldBounds, IBroadPhase2D broadPhase)
        {
            worldBounds.Validate();
            _broadPhase = broadPhase ?? throw new ArgumentNullException(nameof(broadPhase));
            Bounds = worldBounds;
        }

        public CollisionBodyId RegisterBody()
        {
            EnsureNotStepping();
            if (_nextId == long.MaxValue)
            {
                throw new InvalidOperationException("Body ID space exhausted.");
            }

            // slot 是记录数组下标；复用该下标时仍会分配新的 Body ID。
            int slot;
            if (_free.Count > 0)
            {
                slot = _free.Pop();
            }
            else
            {
                if (_slotCount == _slots.Length)
                {
                    Array.Resize(ref _slots, checked(_slots.Length * 2));
                }

                slot = _slotCount++;
                _slots[slot] = new CollisionBodyRecord();
            }

            CollisionBodyRecord record = _slots[slot];
            record.Id = new CollisionBodyId(_identity, _nextId++);
            record.Height = LogicalHeight.Level1;
            record.RegisteredIndex = _registered.Count;
            record.Error = string.Empty;
            _registered.Add(slot);
            _idToSlot.Add(record.Id, slot);
            return record.Id;
        }

        public bool ContainsBody(CollisionBodyId id) => TryGetRecord(id, out _);
        public bool IsBodyActive(CollisionBodyId id) => TryGetRecord(id, out CollisionBodyRecord record) && record.Active;

        public string GetBodyError(CollisionBodyId id) => RequireRecord(id).Error;

        public bool TryGetBodyBounds(CollisionBodyId id, out Aabb2D bounds)
        {
            bounds = default;
            return TryGetRecord(id, out CollisionBodyRecord record) && record.Active && record.Geometry.TryGetBounds(out bounds);
        }

        public bool TryGetLogicalHeight(CollisionBodyId id, out LogicalHeight height)
        {
            height = default;
            if (!TryGetRecord(id, out CollisionBodyRecord record))
            {
                return false;
            }

            height = record.Height;
            return true;
        }

        /// <summary>复制已准备的世界几何。无效数据暂停该 Body；未知 ID 是调用错误。</summary>
        public bool TrySubmitGeometry(CollisionBodyId id, CompoundShape2D source, LogicalHeight height, out string error)
        {
            EnsureNotStepping();
            CollisionBodyRecord record = RequireRecord(id);
            if (height != LogicalHeight.Level1 && height != LogicalHeight.Level2 && height != LogicalHeight.Level3)
            {
                return Reject(record, "Logical height must be 1, 2 or 3.", out error);
            }

            if (source == null || !source.TryGetBounds(out Aabb2D bounds))
            {
                return Reject(record, "Body requires non-empty prepared geometry.", out error);
            }

            if (bounds.Min.X < Bounds.Min.X || bounds.Min.Y < Bounds.Min.Y
                || bounds.Max.X > Bounds.Max.X || bounds.Max.Y > Bounds.Max.Y)
            {
                return Reject(record, "Whole body bounds exceed the world coverage.", out error);
            }

            record.Geometry.CopyFrom(source);
            record.Height = height;
            record.Active = true;
            record.Error = string.Empty;
            error = string.Empty;
            return true;
        }

        public void SuspendBody(CollisionBodyId id)
        {
            EnsureNotStepping();
            CollisionBodyRecord record = RequireRecord(id);
            record.Active = false;
            record.Geometry.Clear();
            record.Error = string.Empty;
        }

        public bool UnregisterBody(CollisionBodyId id)
        {
            EnsureNotStepping();
            if (!TryGetRecord(id, out CollisionBodyRecord record))
            {
                return false;
            }

            int slot = _idToSlot[id];
            // last 是注册列表尾项保存的槽位，用它填补被移除项，并维护其列表位置。
            int last = _registered[_registered.Count - 1];
            _registered[record.RegisteredIndex] = last;
            _slots[last].RegisteredIndex = record.RegisteredIndex;
            _registered.RemoveAt(_registered.Count - 1);
            _idToSlot.Remove(id);
            record.Active = false;
            record.Geometry.Clear();
            _removed.Add(slot);
            return true;
        }

        /// <summary>
        /// 原子替换指定 Body 的世界几何并同步索引。落点重叠仍成功并返回该 Body 的全部接触；
        /// 失败时恢复原几何，清空输出，并让下一次空间操作重建可能不可信的索引。
        /// </summary>
        public bool TryTeleportTo(CollisionBodyId id, CompoundShape2D targetGeometry,
            List<CollisionContact> contacts, out string error)
        {
            EnsureNotStepping();
            if (contacts == null)
            {
                throw new ArgumentNullException(nameof(contacts));
            }

            contacts.Clear();
            CollisionBodyRecord record = RequireRecord(id);
            if (!record.Active)
            {
                error = "Teleport requires an active body.";
                return false;
            }

            if (targetGeometry == null || !targetGeometry.TryGetBounds(out Aabb2D targetBounds))
            {
                error = "Teleport requires non-empty prepared geometry.";
                return false;
            }

            if (!ContainsBounds(targetBounds))
            {
                error = "Teleport target exceeds the world coverage.";
                return false;
            }

            _teleportBackup.CopyFrom(record.Geometry);
            record.Geometry.TryGetBounds(out Aabb2D originalBounds);
            _stepping = true;
            bool geometryReplaced = false;
            try
            {
                SynchronizeIndex();
                record.Geometry.CopyFrom(targetGeometry);
                geometryReplaced = true;
                _broadPhase.Update(id, targetBounds);
                record.IndexedBounds = targetBounds;

                _candidates.Clear();
                _broadPhase.Query(targetBounds, _candidates);
                foreach (CollisionBodyId candidateId in _candidates)
                {
                    if (!TryGetRecord(candidateId, out CollisionBodyRecord other) || !other.Active || !other.Indexed)
                    {
                        throw new InvalidOperationException("Index returned an unknown or inactive body.");
                    }

                    if (candidateId == id)
                    {
                        continue;
                    }

                    CollectTeleportContacts(record, other);
                }

                _contacts.Sort(CompareContacts);
                contacts.AddRange(_contacts);
                record.Error = string.Empty;
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                if (geometryReplaced)
                {
                    record.Geometry.CopyFrom(_teleportBackup);
                    TryRestoreTeleportProxy(record, originalBounds);
                }

                _needsRebuild = true;
                contacts.Clear();
                error = exception.Message;
                return false;
            }
            finally
            {
                _candidates.Clear();
                _compoundContacts.Clear();
                _contacts.Clear();
                _teleportBackup.Clear();
                _stepping = false;
            }
        }

        public bool TryQueryCircle(Circle2D area, List<CollisionBodyId> results, out string error)
        {
            return TryQueryArea(area, default, ShapeKind2D.Circle, results, out error);
        }

        public bool TryQueryAabb(Aabb2D area, List<CollisionBodyId> results, out string error)
        {
            return TryQueryArea(default, area, ShapeKind2D.Aabb, results, out error);
        }

        public bool TrySweepCircle(Circle2D circle, FixVector2 translation, List<CircleSweepHit2D> hits,
            out CircleSweepStatus status, out string error)
        {
            EnsureNotStepping();
            if (hits == null)
            {
                throw new ArgumentNullException(nameof(hits));
            }

            hits.Clear();
            status = CircleSweepStatus.NoHit;
            _stepping = true;
            try
            {
                Aabb2D sweptBounds = GetSweptBounds(circle, translation);
                SynchronizeIndex();
                _candidates.Clear();
                _broadPhase.Query(sweptBounds, _candidates);
                Fix64 earliest = Fix64.One;
                foreach (CollisionBodyId id in _candidates)
                {
                    if (!TryGetRecord(id, out CollisionBodyRecord record) || !record.Active || !record.Indexed)
                    {
                        throw new InvalidOperationException("Index returned an unknown or inactive body.");
                    }

                    CircleSweepStatus candidateStatus = CircleSweep2D.Sweep(
                        circle, translation, record.Geometry, _sweepShapeHits);
                    if (candidateStatus == CircleSweepStatus.InitialOverlap)
                    {
                        _sweepHits.Clear();
                        status = CircleSweepStatus.InitialOverlap;
                        error = string.Empty;
                        return true;
                    }

                    if (candidateStatus != CircleSweepStatus.Hit)
                    {
                        continue;
                    }

                    Fix64 candidateTime = _sweepShapeHits[0].Time;
                    if (status != CircleSweepStatus.Hit || candidateTime < earliest)
                    {
                        _sweepHits.Clear();
                        earliest = candidateTime;
                        status = CircleSweepStatus.Hit;
                    }

                    if (candidateTime == earliest)
                    {
                        foreach (CircleSweepShapeHit2D shapeHit in _sweepShapeHits)
                        {
                            _sweepHits.Add(new CircleSweepHit2D(id, shapeHit.ShapeIndex,
                                shapeHit.Time, shapeHit.Center, shapeHit.Normal));
                        }
                    }
                }

                _sweepHits.Sort();
                CircleSweepHit2D previous = default;
                foreach (CircleSweepHit2D hit in _sweepHits)
                {
                    if (!previous.IsValid || hit != previous)
                    {
                        hits.Add(hit);
                        previous = hit;
                    }
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                _needsRebuild = true;
                hits.Clear();
                status = CircleSweepStatus.NoHit;
                error = exception.Message;
                return false;
            }
            finally
            {
                _candidates.Clear();
                _sweepShapeHits.Clear();
                _sweepHits.Clear();
                _stepping = false;
            }
        }

        /// <summary>
        /// 先同步全部索引，再查询和精筛。清空 results；失败不输出部分结果。
        /// 索引失败后，下次先 Clear 并按当前有效记录重建，不承诺回滚已提交数据。
        /// </summary>
        public bool Step(List<CollisionPair> results, out string error)
        {
            return StepInternal(results, null, out error);
        }

        /// <summary>
        /// 同时输出 Body 对和全部子形状接触，按 Body 对及两个子形状索引排序。失败时两个列表为空。
        /// </summary>
        public bool Step(List<CollisionPair> results, List<CollisionContact> contacts, out string error)
        {
            if (contacts == null)
            {
                throw new ArgumentNullException(nameof(contacts));
            }

            return StepInternal(results, contacts, out error);
        }

        private bool StepInternal(List<CollisionPair> results, List<CollisionContact> contacts, out string error)
        {
            EnsureNotStepping();
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            contacts?.Clear();
            _pairs.Clear();
            _stepping = true;
            try
            {
                SynchronizeIndex();
                foreach (int slot in _registered)
                {
                    // a 是本轮主动发起范围查询的 Body，b 是随后取出的候选 Body。
                    CollisionBodyRecord a = _slots[slot];
                    if (!a.Active)
                    {
                        continue;
                    }

                    a.Geometry.TryGetBounds(out Aabb2D bounds);
                    _candidates.Clear();
                    _broadPhase.Query(bounds, _candidates);
                    foreach (CollisionBodyId id in _candidates)
                    {
                        if (!TryGetRecord(id, out CollisionBodyRecord b) || !b.Active || !b.Indexed)
                        {
                            throw new InvalidOperationException("Index returned an unknown or inactive body.");
                        }

                        if (id.Value <= a.Id.Value)
                        {
                            // 排除自身，并让每个无序对只由较小 ID 的 Body 发起精筛。
                            continue;
                        }

                        if (contacts == null)
                        {
                            if (CompoundCollision2D.Overlaps(a.Geometry, b.Geometry))
                            {
                                _pairs.Add(new CollisionPair(a.Id, id));
                            }
                        }
                        else
                        {
                            CollectPairContacts(a, b);
                        }
                    }
                }

                _pairs.Sort();
                if (contacts != null)
                {
                    _contacts.Sort(CompareContacts);
                    contacts.AddRange(_contacts);
                }

                results.AddRange(_pairs);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                _needsRebuild = true;
                results.Clear();
                contacts?.Clear();
                error = exception.Message;
                return false;
            }
            finally
            {
                _candidates.Clear();
                _pairs.Clear();
                _compoundContacts.Clear();
                _contacts.Clear();
                _stepping = false;
            }
        }

        private void CollectPairContacts(CollisionBodyRecord a, CollisionBodyRecord b)
        {
            CompoundCollision2D.CollectContacts(a.Geometry, b.Geometry, _compoundContacts);
            if (_compoundContacts.Count == 0)
            {
                return;
            }

            CollisionPair pair = new CollisionPair(a.Id, b.Id);
            _pairs.Add(pair);
            foreach (CompoundContact2D contact in _compoundContacts)
            {
                _contacts.Add(new CollisionContact(pair, contact));
            }
        }

        private bool TryQueryArea(Circle2D circle, Aabb2D aabb, ShapeKind2D kind,
            List<CollisionBodyId> results, out string error)
        {
            EnsureNotStepping();
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            _stepping = true;
            try
            {
                WorldShape2D area = kind == ShapeKind2D.Circle
                    ? WorldShape2D.FromCircle(circle)
                    : WorldShape2D.FromAabb(aabb);
                Aabb2D bounds = area.GetBounds();
                SynchronizeIndex();
                _candidates.Clear();
                _broadPhase.Query(bounds, _candidates);
                foreach (CollisionBodyId id in _candidates)
                {
                    if (!TryGetRecord(id, out CollisionBodyRecord record) || !record.Active || !record.Indexed)
                    {
                        throw new InvalidOperationException("Index returned an unknown or inactive body.");
                    }

                    if (OverlapsArea(area, record.Geometry))
                    {
                        _queryResults.Add(id);
                    }
                }

                _queryResults.Sort(CompareBodyIds);
                CollisionBodyId previous = default;
                foreach (CollisionBodyId id in _queryResults)
                {
                    if (id != previous)
                    {
                        results.Add(id);
                        previous = id;
                    }
                }

                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                _needsRebuild = true;
                results.Clear();
                error = exception.Message;
                return false;
            }
            finally
            {
                _candidates.Clear();
                _queryResults.Clear();
                _stepping = false;
            }
        }

        private static bool OverlapsArea(WorldShape2D area, CompoundShape2D geometry)
        {
            for (int i = 0; i < geometry.Count; i++)
            {
                WorldShape2D shape = geometry[i];
                if (Collision2D.Overlaps(area.GetBounds(), shape.GetBounds())
                    && CompoundCollision2D.Overlaps(area, shape))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareBodyIds(CollisionBodyId a, CollisionBodyId b)
        {
            return a.Value.CompareTo(b.Value);
        }

        private static Aabb2D GetSweptBounds(Circle2D circle, FixVector2 translation)
        {
            circle.Validate();
            Aabb2D start = circle.GetBounds();
            Aabb2D end = new Circle2D(circle.Center + translation, circle.Radius).GetBounds();
            FixVector2 min = new FixVector2(
                FixMath.Min(start.Min.X, end.Min.X),
                FixMath.Min(start.Min.Y, end.Min.Y));
            FixVector2 max = new FixVector2(
                FixMath.Max(start.Max.X, end.Max.X),
                FixMath.Max(start.Max.Y, end.Max.Y));
            return new Aabb2D((min + max) / 2, (max - min) / 2);
        }

        private void CollectTeleportContacts(CollisionBodyRecord body, CollisionBodyRecord other)
        {
            CollisionBodyRecord a = body.Id.Value < other.Id.Value ? body : other;
            CollisionBodyRecord b = body.Id.Value < other.Id.Value ? other : body;
            CompoundCollision2D.CollectContacts(a.Geometry, b.Geometry, _compoundContacts);
            if (_compoundContacts.Count == 0)
            {
                return;
            }

            CollisionPair pair = new CollisionPair(a.Id, b.Id);
            foreach (CompoundContact2D contact in _compoundContacts)
            {
                _contacts.Add(new CollisionContact(pair, contact));
            }
        }

        private static int CompareContacts(CollisionContact a, CollisionContact b)
        {
            int pairOrder = a.Pair.CompareTo(b.Pair);
            if (pairOrder != 0)
            {
                return pairOrder;
            }

            int firstShapeOrder = a.Shapes.ShapeIndexA.CompareTo(b.Shapes.ShapeIndexA);
            return firstShapeOrder != 0 ? firstShapeOrder : a.Shapes.ShapeIndexB.CompareTo(b.Shapes.ShapeIndexB);
        }

        private void SynchronizeIndex()
        {
            if (_needsRebuild)
            {
                _broadPhase.Clear();
                foreach (int slot in _registered)
                {
                    _slots[slot].Indexed = false;
                }

                while (_removed.Count > 0)
                {
                    ReleaseRemoved();
                }
            }
            else
            {
                while (_removed.Count > 0)
                {
                    CollisionBodyRecord record = _slots[_removed[_removed.Count - 1]];
                    RemoveIndexed(record);
                    ReleaseRemoved();
                }
            }

            foreach (int slot in _registered)
            {
                CollisionBodyRecord record = _slots[slot];
                if (!record.Active)
                {
                    RemoveIndexed(record);
                    continue;
                }

                record.Geometry.TryGetBounds(out Aabb2D bounds);
                if (!record.Indexed)
                {
                    _broadPhase.Insert(record.Id, bounds);
                    record.Indexed = true;
                }
                else if (!bounds.Center.Equals(record.IndexedBounds.Center)
                    || !bounds.HalfExtents.Equals(record.IndexedBounds.HalfExtents))
                {
                    _broadPhase.Update(record.Id, bounds);
                }

                record.IndexedBounds = bounds;
            }

            _needsRebuild = false;
        }

        private void TryRestoreTeleportProxy(CollisionBodyRecord record, Aabb2D originalBounds)
        {
            try
            {
                if (record.Indexed)
                {
                    _broadPhase.Update(record.Id, originalBounds);
                    record.IndexedBounds = originalBounds;
                }
            }
            catch
            {
                // The authoritative geometry is already restored. The next operation rebuilds the entire index.
            }
        }

        private void RemoveIndexed(CollisionBodyRecord record)
        {
            if (record.Indexed)
            {
                if (!_broadPhase.Remove(record.Id))
                {
                    throw new InvalidOperationException("Index lost a registered proxy.");
                }

                record.Indexed = false;
            }
        }

        private void ReleaseRemoved()
        {
            // index 是待回收列表中的位置，slot 才是记录数组中的位置。
            int index = _removed.Count - 1;
            int slot = _removed[index];
            CollisionBodyRecord record = _slots[slot];
            record.Id = default;
            record.Indexed = false;
            record.IndexedBounds = default;
            record.Error = string.Empty;
            _free.Push(slot);
            _removed.RemoveAt(index);
        }

        private static bool Reject(CollisionBodyRecord record, string reason, out string error)
        {
            record.Active = false;
            record.Geometry.Clear();
            record.Error = reason;
            error = reason;
            return false;
        }

        private bool ContainsBounds(Aabb2D bounds)
        {
            return bounds.Min.X >= Bounds.Min.X && bounds.Min.Y >= Bounds.Min.Y
                && bounds.Max.X <= Bounds.Max.X && bounds.Max.Y <= Bounds.Max.Y;
        }

        private bool TryGetRecord(CollisionBodyId id, out CollisionBodyRecord record)
        {
            record = null;
            if (!id.BelongsTo(_identity) || !_idToSlot.TryGetValue(id, out int slot))
            {
                return false;
            }

            record = _slots[slot];
            return true;
        }

        private CollisionBodyRecord RequireRecord(CollisionBodyId id)
        {
            if (!TryGetRecord(id, out CollisionBodyRecord record))
            {
                throw new ArgumentException("Body ID is unknown, removed or belongs to another world.", nameof(id));
            }

            return record;
        }

        private void EnsureNotStepping()
        {
            if (_stepping)
            {
                throw new InvalidOperationException("World mutation or nested Step during detection is forbidden.");
            }
        }
    }
}
