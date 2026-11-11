# 碰撞模块示例场景

通过 Unity Editor 创建并保存，用于检查 Body / Shape 配置及 Scene 视图 Gizmos。

## 复合体接触示例

打开 [CollideCompoundContacts.unity](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/Samples/Scenes/CollideCompoundContacts.unity)。在 Inspector 初始化均匀网格，再执行 Step。半径为 1 的圆位于 `x = 1.5`，同一个墙 Body 的上下两个子矩形都从 `x = 2` 开始，结果为 **1 个 Body 对、2 条深度为 0.5 的接触**。

开启 Scene Gizmos 查看黄色方向箭头和深度。箭头放在对应两个子形状的包围盒中心之间。Play 中移开或禁用圆，再 Step，配对和接触都会清空。

## 均匀网格检测示例：CollideWorldOverview

打开 [CollideWorldOverview.unity](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/Samples/Scenes/CollideWorldOverview.unity)，也可使用菜单 `Tools > AshFramework > Collide > 创建或打开世界接入说明场景`。已有文件只打开，不覆盖你的调整；有未保存场景时菜单会停止并提示先保存。

本场景通过 IBroadPhase2D 显式注入 UniformGridBroadPhase2D。编辑状态刚打开时尚未初始化；进入 Play 后，说明组件的 Start 创建网格并调用 World.Initialize。检测仍须手动点击 Step，没有自动检测循环。Inspector 也提供初始化按钮，可在编辑状态手动验证。

World 使用 XY，中心 (0, 0)，半尺寸 (9, 4)，覆盖 X [-9, 9]、Y [-4, 4]。示例格子边长 2，构成 9 列 × 4 行；该值只用于本例，不是性能推荐。它与各 Body 位于独立层级，通过显式引用绑定，不靠父子位置猜测所属世界。

| 场景对象 | 观察内容 |
| --- | --- |
| 00_说明与操作 | 中文说明、显式注入网格、手动 Step、碰撞对及 Body ID/暂停原因；Play 中可移开/还原、禁用/启用 B |
| 01_World_XY_固定范围 | 固定范围及平面配置、初始化状态、包含禁用对象的平面检查按钮 |
| 01_正常复合体A | 圆 + 旋转 20° 的矩形，绑定 World，逻辑高度 1 |
| 02_正常圆B | 与 A 几何重叠，逻辑高度 3；Step 后得到 A/B 配对，高度当前不参与过滤 |
| 03_禁用Body | 初始化后取得 ID，禁用时暂停并保留身份；灰叉只标记位置 |
| 04_禁用且平面不一致 | Body 是 XZ，World 是 XY；即使禁用，平面检查也会报告 |
| 05_越界配置 | 圆心 X=9、半径 0.9，整体范围越过世界右边界；Step 暂停它并显示错误，其他有效 Body 继续 |
| 06_未绑定Body | 有可预览几何，World 引用为空；不被此世界的检查及检测收集 |

建议按以下顺序查看：

1. 使用 Scene 的 2D 视图并开启 Gizmos。青框是固定范围，绿色是几何预览，灰叉是禁用位置标记；初始化后增加淡蓝网格线。过密网格（列数+行数超过 1024）省略内部线，检测不受影响。
2. 进入 Play，示例 Start 显示“已显式注入均匀网格：9 列 × 4 行”。双击 Game 标签放大面板，避免固定 1920×1080 在小窗口缩小后文字难读。
3. 点击“执行显式 Step”，应显示 **1 对**，列出 A/B 名称和 ID；Scene 增加红色配对连线。列表和连线表示上次成功 Step 的配对，不是自动实时检测。
4. 点击“移开 / 还原 B”，再 Step：得到 0 对；再还原并 Step：恢复 1 对。移动本身不刷新检测结果。
5. 点击“禁用 / 启用 B”，再 Step：禁用时得到 0 对，恢复后得到 1 对，B 的 ID 保持不变。面板同时显示禁用对象及越界对象的暂停原因，未绑定对象没有此世界 ID。
6. 在 Inspector 选择 World 检查平面，仍应报告 **检查 5 个、平面不一致 1 个**。禁用的 XZ 对象包含在检查内；若启用它并 Step，会因平面不一致暂停。
7. “刷新示例几何”保留用于对照：初始配置成功 4、禁用 2、失败 0，只改源缓存，不提交或检测。编辑状态可先尝试未初始化的 Step 观察明确失败，再点击“显式初始化均匀网格”和 Step。

这里故意保留错误配置，方便检查和对照，没有自动修复。网格由示例创建并通过接口注入，正式 World 不默认选择网格；没有测试 Stub、空索引或暴力兜底。没有自动 Step、碰撞事件、推开或位置修正。运行时初始化状态不会保存进场景，域重载后需重新初始化。

场景通过 Editor 菜单和 EditorSceneManager 创建、保存；没有手写场景 YAML、meta 或加入 Build Settings。说明组件及其 Editor 工具位于独立 Samples 程序集，核心和正式 Unity 适配程序集不依赖它们。

场景初版的 183 项测试记录保留在验收清单。后续网格阶段增加 19 项索引及 7 项接入验证，最终原生 EditMode 209/209 通过，并完成上述 Play 手动操作核对。最新实际验证记录见 [验收清单](../TASK-CHECKLIST.md)。

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
