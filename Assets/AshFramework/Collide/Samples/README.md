# 碰撞模块示例场景

通过 Unity Editor 创建并保存，用于检查 Body / Shape 配置及 Scene 视图 Gizmos。

## 世界接入说明：CollideWorldOverview

打开 [CollideWorldOverview.unity](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/Samples/Scenes/CollideWorldOverview.unity)，也可使用菜单 `Tools > AshFramework > Collide > 创建或打开世界接入说明场景`。已有文件只打开，不覆盖你的调整；有未保存场景时菜单会停止并提示先保存。

本场景说明当前接口版世界的能力边界：World 与 Body 绑定、几何预览、统一平面、固定覆盖范围和检查按钮均可使用；没有注入具体空间索引，World 保持未初始化，没有有效核心 ID，也没有世界碰撞结果。

World 使用 XY，中心 (0, 0)，半尺寸 (9, 4)，覆盖 X [-9, 9]、Y [-4, 4]。它与各 Body 位于独立层级，通过显式引用绑定，不靠父子位置猜测所属世界。

| 场景对象 | 观察内容 |
| --- | --- |
| 00_说明与操作 | Inspector 中文说明、跳转 World/各 Body、手动刷新源几何、尝试显式 Step |
| 01_World_XY_固定范围 | 固定范围及平面配置、未初始化状态、包含禁用对象的平面检查按钮 |
| 01_正常复合体A | 圆 + 旋转 20° 的矩形，绑定 World，逻辑高度 1 |
| 02_正常圆B | 与 A 几何重叠，逻辑高度 3；说明高度当前不参与过滤，预览重叠不表示世界已检测 |
| 03_禁用Body | 保留显式绑定；灰叉只标记位置，不绘制有效几何或制造 ID |
| 04_禁用且平面不一致 | Body 是 XZ，World 是 XY；即使禁用，平面检查也会报告 |
| 05_越界配置 | 圆心 X=9、半径 0.9，整体范围越过世界右边界；源几何有效，未来注入索引并提交时才由世界暂停 |
| 06_未绑定Body | 有可预览几何，World 引用为空；不被此世界的检查及检测收集 |

建议按以下顺序查看：

1. 使用 Scene 的 2D 视图并开启 Gizmos。青色矩形是固定覆盖范围，绿色是几何预览，灰叉是禁用对象位置标记；选中有效 Body 还能查看已有子 AABB 和整体 AABB。
2. 选择 `00_说明与操作`，读取各例说明。点击“刷新示例几何”，初始配置应显示准备成功 4、禁用 2、失败 0。此操作不提交世界、不做覆盖范围检查，也不执行检测。
3. 点击“选择 World”，再点击“检查平面一致性”。初始应报告 **检查 5 个 Body、平面不一致 1 个**，指向禁用的 04；未绑定的 06 不计入。
4. 回到说明对象，点击“尝试显式 Step”。应显示 `World is not initialized; inject an IBroadPhase2D first.`，不能将其当成成功检测到零碰撞。
5. 可以进入 Play，在 Game 视图查看同样的说明与两个手动按钮。双击 Game 标签放大面板，避免预设 1920×1080 在小窗口缩到 0.25 倍导致文字过小。Game 面板只提供说明；实际形状请看 Scene 的 Gizmos。

这里故意保留错误配置，方便检查和对照，没有自动修复。禁用保留核心身份需要先由外部代码注入索引；本场景只展示绑定和未初始化状态，不用测试 Stub、空索引或暴力索引充当运行实现。没有自动 Step、碰撞事件、推开或位置修正。

场景通过 Editor 菜单和 EditorSceneManager 创建、保存；没有手写场景 YAML、meta 或加入 Build Settings。说明组件及其 Editor 工具位于独立 Samples 程序集，核心和正式 Unity 适配程序集不依赖它们。

2026-10-28 已在本项目打开场景完成 Scene、World 检查按钮、说明 Inspector 和 Play/Game 面板的视觉及操作核对。新增 2 项场景验证，整个碰撞测试程序集原生 EditMode 183/183 通过；CLI 参数兼容问题仍单独记录 blocked，见 [验收清单](../TASK-CHECKLIST.md)。

## 基础几何 Gizmos

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

两个场景的历史验收均由 Editor 重新打开，验证了平面、高度、三种形状和整体范围。本轮 Editor 留在上面的世界接入说明场景。
