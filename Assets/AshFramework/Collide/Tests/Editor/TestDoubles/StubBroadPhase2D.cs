using System;
using System.Collections.Generic;

namespace AshFramework.Collide.Tests
{
    /// <summary>只返回预设 ID，不计算空间关系。仅验证世界协调与错误恢复。</summary>
    internal sealed class StubBroadPhase2D : IBroadPhase2D
    {
        internal readonly Dictionary<CollisionBodyId, Aabb2D> Entries = new Dictionary<CollisionBodyId, Aabb2D>();
        internal readonly List<CollisionBodyId> Candidates = new List<CollisionBodyId>();
        internal readonly List<CollisionBodyId> InvalidCandidates = new List<CollisionBodyId>();
        internal readonly List<string> Calls = new List<string>();
        internal string FailOperation;
        internal int FailureDelay;
        internal Action QueryCallback;

        private void Record(string operation)
        {
            Calls.Add(operation);
            if (FailOperation == operation)
            {
                if (FailureDelay > 0)
                {
                    FailureDelay--;
                    return;
                }

                FailOperation = null;
                throw new InvalidOperationException("Injected " + operation + " failure.");
            }
        }

        public void Insert(CollisionBodyId id, Aabb2D bounds)
        {
            Record("Insert");
            Entries.Add(id, bounds);
        }

        public void Update(CollisionBodyId id, Aabb2D bounds)
        {
            Record("Update");
            if (!Entries.ContainsKey(id))
            {
                throw new InvalidOperationException("Missing proxy.");
            }

            Entries[id] = bounds;
        }

        public bool Remove(CollisionBodyId id)
        {
            Record("Remove");
            return Entries.Remove(id);
        }

        public void Query(Aabb2D bounds, List<CollisionBodyId> results)
        {
            Record("Query");
            QueryCallback?.Invoke();
            foreach (CollisionBodyId id in Candidates)
            {
                if (Entries.ContainsKey(id))
                {
                    results.Add(id);
                }
            }

            results.AddRange(InvalidCandidates);
        }

        public void Clear()
        {
            Record("Clear");
            Entries.Clear();
        }
    }
}
