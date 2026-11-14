using System.Collections.Generic;
using AshFramework.Math;

namespace AshFramework.Collide
{
    /// <summary>
    /// 碰撞世界的原子传送、当前位置范围查询和单圆平移扫掠契约。
    /// 所有成功操作都基于当前已提交几何同步空间索引；失败时清空输出且不发布部分结果。
    /// </summary>
    public interface ICollisionWorldSpatialOperations2D
    {
        /// <summary>
        /// 原子替换指定 Body 的世界几何并同步其索引代理。成功时复制 targetGeometry，清空并填充
        /// contacts；接触只包含指定 Body 在落点与其他 Body 的接触，按 Body 对和子形状索引排序。
        /// 落点重叠不使传送失败。失败时 contacts 为空，Body 几何、活动状态和权威位置保持原值。
        /// 传送不生成连续路径；Unity 适配层只可在成功后提交 Transform，并结束旧位置的渐进修正。
        /// </summary>
        bool TryTeleportTo(CollisionBodyId id, CompoundShape2D targetGeometry,
            List<CollisionContact> contacts, out string error);

        /// <summary>
        /// 查询与圆相交或相切的 Body。查询前同步当前已提交几何，使用 BroadPhase 获取候选后精筛；
        /// 成功时 results 按 Body ID 递增、每个 Body 最多一次。失败时 results 为空。
        /// </summary>
        bool TryQueryCircle(Circle2D area, List<CollisionBodyId> results, out string error);

        /// <summary>
        /// 查询与轴对齐矩形相交或相切的 Body。查询前同步当前已提交几何，使用 BroadPhase 获取候选后精筛；
        /// 成功时 results 按 Body ID 递增、每个 Body 最多一次。失败时 results 为空。
        /// 查询范围允许部分超出世界覆盖范围。
        /// </summary>
        bool TryQueryAabb(Aabb2D area, List<CollisionBodyId> results, out string error);

        /// <summary>
        /// 将单圆沿 translation 对固定 Body 做连续平移扫掠。hits 只包含最早时刻的全部命中，
        /// 按 Body ID 和子形状索引排序。相切算接触；从相切边界向外或沿切线移动不构成阻挡。
        /// 初始正深度穿透返回 InitialOverlap 且 hits 为空，交由位置修正处理。零位移若未初始穿透
        /// 返回 NoHit。excludedBody 为 default 时不排除候选；有效值必须属于当前世界且仍注册，
        /// 成功查询时忽略该 Body，异世界或已注销 ID 使操作失败。失败时 hits 为空、status 为 NoHit。
        /// </summary>
        bool TrySweepCircle(Circle2D circle, FixVector2 translation, CollisionBodyId excludedBody,
            List<CircleSweepHit2D> hits, out CircleSweepStatus status, out string error);
    }
}
