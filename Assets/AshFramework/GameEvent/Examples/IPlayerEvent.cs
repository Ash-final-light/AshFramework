namespace AshFramework.Events.Examples
{
    /// <summary>手动生成事件桥的最小使用示例；不会自动初始化或发送事件。</summary>
    [EventInterface(EEventGroup.GroupLogic)]
    public interface IPlayerEvent
    {
        void HpChanged(int currentHp, int maxHp);
    }
}
