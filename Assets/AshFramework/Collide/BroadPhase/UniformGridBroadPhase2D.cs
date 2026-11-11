using System;
using System.Collections.Generic;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>
    /// 固定范围均匀网格。完整 AABB 登记到全部覆盖格，查询按闭区间过滤并去重。
    /// 参考 LightweightPhysics 的跨格登记和缓冲复用；使用本项目的逐项维护接口。
    /// 单线程、无查询中修改；无默认暴力回退。
    /// </summary>
    public sealed class UniformGridBroadPhase2D : IBroadPhase2D
    {
        private readonly Dictionary<CollisionBodyId, Proxy> _proxies = new Dictionary<CollisionBodyId, Proxy>();
        private readonly List<Proxy>[] _cells;
        // 桶总数不超过格子数；预留池容量，使移除和 Clear 归还桶时不再扩容。
        private readonly List<List<Proxy>> _bucketPool;
        private readonly List<CollisionBodyId> _queryBuffer = new List<CollisionBodyId>();
        private readonly long _minX;
        private readonly long _minY;
        private readonly long _maxX;
        private readonly long _maxY;
        private readonly ulong _cellRaw;
        private uint _queryGeneration;

        private sealed class Proxy
        {
            internal readonly CollisionBodyId Id;
            internal Aabb2D Bounds;
            internal CellRange Range;
            internal uint QueryGeneration;

            internal Proxy(CollisionBodyId id, Aabb2D bounds, CellRange range)
            {
                Id = id;
                Bounds = bounds;
                Range = range;
            }
        }

        private readonly struct CellRange
        {
            internal readonly int MinX;
            internal readonly int MinY;
            internal readonly int MaxX;
            internal readonly int MaxY;
            private readonly bool _valid;

            internal CellRange(int minX, int minY, int maxX, int maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
                _valid = true;
            }

            internal bool Contains(int x, int y)
            {
                return _valid && x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
            }
        }

        public Aabb2D Bounds { get; }
        public Fix64 CellSize { get; }
        public int Columns { get; }
        public int Rows { get; }
        public int CellCount => _cells.Length;
        public int BodyCount => _proxies.Count;

        /// <summary>
        /// maxCellCount 限制格子表规模，超限明确拒绝，不增大格子或截断范围。
        /// 最后一行/列可小于 CellSize；世界最大边界归入最后一格。
        /// </summary>
        public UniformGridBroadPhase2D(Aabb2D worldBounds, Fix64 cellSize, int maxCellCount = 65536)
        {
            worldBounds.Validate();
            if (cellSize <= Fix64.Zero || maxCellCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size and maximum cell count must be positive.");
            }

            Bounds = worldBounds;
            CellSize = cellSize;
            FixVector2 min = worldBounds.Min;
            FixVector2 max = worldBounds.Max;
            _minX = min.X.RawValue;
            _minY = min.Y.RawValue;
            _maxX = max.X.RawValue;
            _maxY = max.Y.RawValue;
            _cellRaw = (ulong)cellSize.RawValue;
            ulong columns = DivideUp(Distance(_maxX, _minX), _cellRaw);
            ulong rows = DivideUp(Distance(_maxY, _minY), _cellRaw);
            if (columns > (ulong)maxCellCount || rows > (ulong)maxCellCount
                || columns * rows > (ulong)maxCellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), "Grid exceeds the configured maximum cell count.");
            }

            Columns = (int)columns;
            Rows = (int)rows;
            int count = checked(Columns * Rows);
            _cells = new List<Proxy>[count];
            _bucketPool = new List<List<Proxy>>(count);
        }

        public void Insert(CollisionBodyId id, Aabb2D bounds)
        {
            RequireValidId(id);
            if (_proxies.ContainsKey(id))
            {
                throw new ArgumentException("Body is already indexed.", nameof(id));
            }

            CellRange range = RequireContainedRange(bounds);
            Proxy proxy = new Proxy(id, bounds, range);
            PrepareNewCells(range, default);
            // 字典扩容和桶容量准备都在发布格子登记前完成。
            _proxies.Add(id, proxy);
            AddToCells(proxy, range, default);
        }

        public void Update(CollisionBodyId id, Aabb2D bounds)
        {
            RequireValidId(id);
            if (!_proxies.TryGetValue(id, out Proxy proxy))
            {
                throw new KeyNotFoundException("Cannot update a body that is not indexed.");
            }

            CellRange range = RequireContainedRange(bounds);
            CellRange previous = proxy.Range;
            PrepareNewCells(range, previous);
            AddToCells(proxy, range, previous);
            RemoveFromCells(proxy, previous, range);
            proxy.Range = range;
            proxy.Bounds = bounds;
        }

        public bool Remove(CollisionBodyId id)
        {
            if (!_proxies.TryGetValue(id, out Proxy proxy))
            {
                return false;
            }

            RemoveFromCells(proxy, proxy.Range, default);
            _proxies.Remove(id);
            return true;
        }

        public void Query(Aabb2D bounds, List<CollisionBodyId> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            bounds.Validate();
            FixVector2 min = bounds.Min;
            FixVector2 max = bounds.Max;
            if (max.X.RawValue < _minX || max.Y.RawValue < _minY
                || min.X.RawValue > _maxX || min.Y.RawValue > _maxY)
            {
                return;
            }

            CellRange range = RangeFor(min, max);
            NextQueryGeneration();
            _queryBuffer.Clear();
            for (int y = range.MinY; y <= range.MaxY; y++)
            {
                for (int x = range.MinX; x <= range.MaxX; x++)
                {
                    List<Proxy> bucket = _cells[y * Columns + x];
                    if (bucket == null)
                    {
                        continue;
                    }

                    foreach (Proxy proxy in bucket)
                    {
                        if (proxy.QueryGeneration == _queryGeneration)
                        {
                            continue;
                        }

                        proxy.QueryGeneration = _queryGeneration;
                        if (Collision2D.Overlaps(proxy.Bounds, bounds))
                        {
                            _queryBuffer.Add(proxy.Id);
                        }
                    }
                }
            }

            // 仅追加本次去重的结果，不排序/清空调用方原有部分。
            results.AddRange(_queryBuffer);
        }

        public void Clear()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                List<Proxy> bucket = _cells[i];
                if (bucket != null)
                {
                    bucket.Clear();
                    _bucketPool.Add(bucket);
                    _cells[i] = null;
                }
            }

            _proxies.Clear();
            _queryBuffer.Clear();
            _queryGeneration = 0;
        }

        private void PrepareNewCells(CellRange range, CellRange previous)
        {
            for (int y = range.MinY; y <= range.MaxY; y++)
            {
                for (int x = range.MinX; x <= range.MaxX; x++)
                {
                    if (previous.Contains(x, y))
                    {
                        continue;
                    }

                    int index = y * Columns + x;
                    List<Proxy> bucket = _cells[index];
                    if (bucket == null)
                    {
                        int last = _bucketPool.Count - 1;
                        if (last >= 0)
                        {
                            bucket = _bucketPool[last];
                            _bucketPool.RemoveAt(last);
                        }
                        else
                        {
                            bucket = new List<Proxy>();
                        }

                        // 准备失败最多留下空桶，不改变任何 Body 的逻辑登记。
                        _cells[index] = bucket;
                    }

                    if (bucket.Capacity == bucket.Count)
                    {
                        bucket.Capacity = System.Math.Max(4, checked(bucket.Count * 2));
                    }
                }
            }
        }

        private void AddToCells(Proxy proxy, CellRange range, CellRange previous)
        {
            for (int y = range.MinY; y <= range.MaxY; y++)
            {
                for (int x = range.MinX; x <= range.MaxX; x++)
                {
                    if (!previous.Contains(x, y))
                    {
                        _cells[y * Columns + x].Add(proxy);
                    }
                }
            }
        }

        private void RemoveFromCells(Proxy proxy, CellRange previous, CellRange retained)
        {
            for (int y = previous.MinY; y <= previous.MaxY; y++)
            {
                for (int x = previous.MinX; x <= previous.MaxX; x++)
                {
                    if (retained.Contains(x, y))
                    {
                        continue;
                    }

                    int index = y * Columns + x;
                    List<Proxy> bucket = _cells[index];
                    bucket.Remove(proxy);
                    if (bucket.Count == 0)
                    {
                        _cells[index] = null;
                        _bucketPool.Add(bucket);
                    }
                }
            }
        }

        private CellRange RequireContainedRange(Aabb2D bounds)
        {
            bounds.Validate();
            FixVector2 min = bounds.Min;
            FixVector2 max = bounds.Max;
            if (min.X.RawValue < _minX || min.Y.RawValue < _minY
                || max.X.RawValue > _maxX || max.Y.RawValue > _maxY)
            {
                throw new ArgumentOutOfRangeException(nameof(bounds), "Whole body bounds must be inside the grid coverage.");
            }

            return RangeFor(min, max);
        }

        private CellRange RangeFor(FixVector2 min, FixVector2 max)
        {
            return new CellRange(Cell(min.X.RawValue, _minX, _maxX, Columns),
                Cell(min.Y.RawValue, _minY, _maxY, Rows),
                Cell(max.X.RawValue, _minX, _maxX, Columns),
                Cell(max.Y.RawValue, _minY, _maxY, Rows));
        }

        private int Cell(long raw, long minimum, long maximum, int count)
        {
            if (raw <= minimum)
            {
                return 0;
            }

            if (raw >= maximum)
            {
                return count - 1;
            }

            return (int)(Distance(raw, minimum) / _cellRaw);
        }

        // 已知 high >= low，差可跨过 long.MaxValue，使用无符号差避免有符号溢出。
        private static ulong Distance(long high, long low)
        {
            return unchecked((ulong)high - (ulong)low);
        }

        private static ulong DivideUp(ulong value, ulong divisor)
        {
            return value / divisor + (value % divisor == 0 ? 0UL : 1UL);
        }

        private static void RequireValidId(CollisionBodyId id)
        {
            if (!id.IsValid)
            {
                throw new ArgumentException("A valid world body ID is required.", nameof(id));
            }
        }

        private void NextQueryGeneration()
        {
            if (_queryGeneration == uint.MaxValue)
            {
                foreach (Proxy proxy in _proxies.Values)
                {
                    proxy.QueryGeneration = 0;
                }

                _queryGeneration = 1;
            }
            else
            {
                _queryGeneration++;
            }
        }
    }
}
