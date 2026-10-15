# AshFramework Log

运行时命名空间：`AshFramework.Logging`。支持 Unity 2022.3；Runtime 与 Editor 分程序集。

## 使用

```csharp
using AshFramework.Logging;

// 未初始化也有最小 Console 回退。需要文件或自定义配置时，在主线程启动入口调用：
LogSystem.Initialize(new LogConfig
{
    EnableFile = true,                  // 空目录自动使用 persistentDataPath/Logs
    ConsoleMinLevel = LogLevel.Debug,
    FileMinLevel = LogLevel.Info
});

LogUtil.Info("打开背包", LogCategories.UI, gameObject);
LogUtil.InfoFormat(LogCategories.Network, "连接服务器：{0}", address);
LogUtil.Warning("配置缺失", LogCategories.Resource);
LogUtil.Exception(exception, LogCategories.Framework);
LogUtil.LogColor(LogColor.Cyan, "初始化完成", LogCategories.Battle);
```

所有 Debug 重载仅在 `UNITY_EDITOR`、`DEVELOPMENT_BUILD`、`ASH_LOG_DEBUG` 任意一个宏存在时保留。其他等级使用运行时门槛。昂贵字符串构造可放在 `LogUtil.IsEnabled(...)` 判断内。

可使用 `Tools > AshFramework > Logging > Settings` 显式创建设置资源。资源位于本模块的 Resources 中，播放/Player 启动时自动读取。已有手动 Initialize 优先；运行中修改设置在下次初始化生效。没有资源时不自动启用文件输出。

自定义输出器实现本模块的 `ILogger`，传给 `LogSystem.Initialize(config, logger)`。系统接管其生命周期；并发 Log 必须安全，Flush 必须遵守时间预算，Dispose 必须幂等且不无限等待。

## Console 筛选

打开 `Tools > AshFramework > Logging > Console Filter`，可将窗口停靠在 Console 附近。

- 分类选择写入 `[Ash][C:UI]` 这样的检索前缀。
- “全部框架日志”检索 `[Ash][C:`。
- “全部 Console 日志”明确清空搜索。
- “恢复原搜索”恢复本轮筛选前的输入；手动改写搜索后不会覆盖新输入。
- 自定义分类可添加到 LogCategories 常量或设置的 CustomCategories；使用字母、数字、_、.、-，最长 64 字符。
- 筛选只改变显示，不影响其他分类生成或写文件。

采用筛选窗口。有兼容工具栏事件的版本会自动增加入口；反射搜索不可用时可从菜单复制搜索词手动粘贴。

分类与任意关键词精确组合、多分类 OR 和分类计数不在本版范围。Debug/Info 共用原生普通日志按钮，Error/Fatal/Exception 共用原生错误按钮。

## 线程与文件

主线程 Console 输出同步进行；工作线程记录排到主线程显示队列，Unity 原生堆栈可能指向转发位置。显式 Exception 保留原始异常文本和堆栈。对象 Context 不在工作线程解析。

文件后端使用有界队列、错误等级预留容量和单写入线程。正常退出、退出播放、重编译时尝试排空并关闭；可手动调用 `LogSystem.Shutdown(2000)` 取得完成结果。超时返回 false，文件流仍由消费者负责后续关闭；强杀进程或断电不保证末尾日志。

文件按大小滚动，只清理符合本后端完整会话命名规则的旧文件，请使用专用目录。满载时可能丢弃记录并生成摘要。WebGL Player 禁用文件线程后端。文件仅保存经过本系统的日志，不自动采集第三方 Debug.Log 或 Unity 自动异常。

测试位于 `Tests/Editor`，覆盖搜索恢复、原生搜索读写、配置快照、输出器异常隔离、并发记录、文件排空和滚动保留。

已在 Unity 2022.3.62f1c1 通过全部 14 项 EditMode 测试；Runtime 无 UnityEditor 引用编译通过，正式包宏条件下 Debug/DebugFormat 的调用与参数副作用裁剪验证通过。窗口视觉布局尚未手动验收。
