# Gizmos 示例场景

通过 Unity Editor 创建并保存，用于检查 Body / Shape 配置及 Scene 视图 Gizmos。

| 场景 | 配置 | 查看方式 |
| --- | --- | --- |
| [CollideGizmosXZ.unity](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/Samples/Scenes/CollideGizmosXZ.unity) | 3D 世界 XZ 平面，Body 世界 Y=2 | 关闭 Scene 的 2D 模式，使用 Top 视角 |
| [CollideGizmosXY.unity](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/Samples/Scenes/CollideGizmosXY.unity) | 2D 世界 XY 平面，Body 世界 Z=2 | 使用 Scene 的 2D 视图 |

每个场景包含一个 Body（逻辑高度 Level3）和三个子形状：

- Circle：半径 0.9，局部位置 X=-2，Local Center=(0.25, 0.15)。
- OBB：半尺寸 (1.25, 0.6)，平面局部位置 (1.4, 0.5)，平面内旋转 30°。
- AABB：半尺寸 (0.65, 0.4)，平面局部位置 (0, -2)，保持轴对齐。

开启 Gizmos，选择场景中的 Body 或其子物体，查看绿色形状、黄色子 AABB、青色整体 AABB 及配置标签。取消选择后只显示绿色轮廓。Level3 只表示逻辑高度，不把 Body 移到世界高度 3。

这些是几何配置示例，主要在 Scene 视图使用。场景包含正交相机，没有添加可渲染模型、自动碰撞检测或碰撞事件脚本；直接进入 Play 不会产生交互演示。没有加入 Build Settings。

两个场景保存后均由 Editor 重新打开，验证了平面、高度、三种形状和整体范围。当前 Editor 保留打开 XZ 示例。
