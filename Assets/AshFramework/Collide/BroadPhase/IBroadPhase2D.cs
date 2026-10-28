using System.Collections.Generic;

namespace AshFramework.Collide
{
    /// <summary>
    /// 世界独占的 Body 整体 AABB 索引。插入和更新失败不得留下部分修改。
    /// 不读取 Unity 对象、形状、高度或玩法规则；不支持并发或查询中修改。
    /// </summary>
    public interface IBroadPhase2D
    {
        /// <summary>插入有效范围；重复 ID 必须报错。</summary>
        void Insert(CollisionBodyId id, Aabb2D bounds);

        /// <summary>更新已有 ID，不隐式插入；成功后不得残留旧位置。</summary>
        void Update(CollisionBodyId id, Aabb2D bounds);

        /// <summary>移除已有 ID；不存在时返回 false。</summary>
        bool Remove(CollisionBodyId id);

        /// <summary>
        /// 追加所有相交及相切候选，允许额外候选。本次追加无重复、不保证顺序；
        /// 不清空列表，不负责排除列表原有元素。失败必须报错，不能截断结果。
        /// </summary>
        void Query(Aabb2D bounds, List<CollisionBodyId> results);

        /// <summary>清空全部索引项，保留配置。</summary>
        void Clear();
    }
}
