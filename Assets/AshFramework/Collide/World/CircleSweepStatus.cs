namespace AshFramework.Collide
{
    /// <summary>单圆连续平移扫掠的几何结果；操作失败由 TrySweepCircle 的返回值表达。</summary>
    public enum CircleSweepStatus
    {
        NoHit = 0,
        Hit = 1,
        InitialOverlap = 2
    }
}
