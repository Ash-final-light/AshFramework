namespace AshFramework.Events.Tests
{
    [EventInterface(EEventGroup.GroupLogic)]
    public interface IEventBridgeTest
    {
        void Notify(int current, int maximum);
        void Ping();
        void Six(int a, int b, int c, int d, int e, int f);
    }
}
