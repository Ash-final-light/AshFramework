namespace Framework.FixedPoint
{
    public static class FPUtils
    {
        public static void Prewarm()
        {
            // 调用部分方法预热即时编译，避免高频代码路径首次调用时出现卡顿。
            _ = FPMath.Sin(FP64.Zero);
        }
    }
}
