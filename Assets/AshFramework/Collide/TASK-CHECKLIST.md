# Collide 交付与验证记录

日期：2026-10-27。目标项目：`D:/UnityProject/AshFramework`。

## 任务 1：精筛核心实现范围

| 内容 | 状态 |
| --- | --- |
| Circle2D、Aabb2D、Obb2D 世界几何与正尺寸验证 | 已实现 |
| 三类形状 AABB 及 OBB 向外取整 | 已实现 |
| 六种无序配对、九个有序 Overlaps 重载 | 已实现 |
| 相切算相交；拒绝 default 与越界包围盒 | 已实现 |
| 宽整数平方/投影与量化轴长度补偿 | 已实现 |
| 核心 noEngineReferences、无 unsafe、复用现有 Math | 已配置，构建通过；隔离断言已提供 |
| 28 个 EditMode 测试用例 | 本项目原生 Test Runner 已执行，28/28 通过，无失败或跳过 |
| Body/Shape 组件、逻辑高度、世界、事件和空间划分 | 后续任务，未实现；平面适配见下方任务 3 |

核心共 5 个 C# 文件，测试 1 个 C# 文件。未修改 Math、第三方包或 Unity 生成的 sln/csproj；Unity 自动导入并生成新程序集工程与元数据。原有 `ProjectSettings/ProjectSettings.asset` 修改保持不动。

## 验证结果

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 唯一 ready Editor 与规范化绝对项目路径 | passed | [环境诊断](D:/UnityProject/AshFramework/.codex/runs/20261027T103938Z-71fe4dd2/report.md) |
| 最终 C# 布局、生成解决方案源码覆盖及构建、whitespace、IDE0011 | passed | [最终 C# 验证](D:/UnityProject/AshFramework/.codex/runs/20261027T104844Z-77c76512/report.md) |
| 工作流结构与其自身 40 项测试 | passed | 同上；这不是几何测试结果 |
| 最终代码的 AshFramework Editor 编译 | passed | [编译终态](D:/UnityProject/AshFramework/.codex/runs/20261027T104844Z-94c28479/report.md)：completed、failed=false、errors=[] |
| CLI 运行筛选后的精筛测试 | blocked | [Editor 工作流](D:/UnityProject/AshFramework/.codex/runs/20261027T104357Z-431772f8/report.md)：CLI 无法向 Pipeline 0.3.1-exp.1 传递参数，测试未启动 |
| 同一 Editor 原生 Test Runner 界面验收 | passed，28/28 | 用户解锁后，在 AshFramework 的 EditMode 选择本程序集并 Run Selected；[界面验收记录](D:/UnityProject/AshFramework/.codex/runs/20261027-collide-native-test-runner/report.md) |
| 源码换行、行尾空白、程序集 JSON | passed | 最终文件检查；未把编译当作几何正确性证明 |
| Player/IL2CPP、跨平台与性能 | 未执行 | 不在本轮已验证范围 |

未升级工具/包，未关闭 Editor，未重发无筛选测试，未启用其他测试框架替代 Unity 测试。CLI blocked 与原生界面验收分别记录，未把原 blocked 工作流报告修改为 passed。

## 原生界面验收结果

在用户确认解锁后，重新选定 AshFramework 窗口，打开 Unity Test Runner 的 EditMode，选中 `AshFramework.Collide.Tests.Editor.dll` 并 Run Selected。完成后计数为通过 28、失败 0；按本程序集名称过滤并查看顶部和底部的全部目标用例，均为绿色通过。界面另有 86 个未运行项，对应未选中的数学库测试，不属于本轮目标测试。

用例包括 1000 组固定种子的六类配对独立参考对照、300 组交换/平移检查、300 组普通及极小 OBB 保守范围检查，以及相切、一个 raw 间隙、包含、斜轴分离、大坐标、无效输入和程序集隔离。循环次数是测试内部样本数，不计作独立测试用例数量。

本轮测试无需修改 C# 源码。任务 1 的源码、编译和当前 Windows Unity Editor 内的几何测试验收已完成；目标 Player/IL2CPP、跨平台及性能仍需后续按实际需求验证。

## 任务 3：XY/XZ 平面适配

已实现：

- 核心 CollisionPlane 枚举和 CollisionPlaneProjection.Project/Embed/TryGetRotation。
- XZ 对应逻辑二维 XY；补回世界 Y 时显式传入 normalCoordinate，与角色逻辑高度 1/2/3 无关。
- 独立 AshFramework.Collide.Unity 程序集，提供 TryReadRigidPose。
- 读取世界平移与旋转、判断平面法线、输出单位缩放、失败输出 Identity，且不回写 Transform。
- 22 项定点映射测试、28 项 Unity 姿态适配测试；原精筛测试 28 项保持通过。

当前总计核心 7 个 C#、Unity 适配 1 个 C#、测试 3 个 C#；共 11 个。未新增包或修改 Math 核心；现有测试 asmdef 仅增加对 Unity 适配程序集的引用。

| 检查 | 状态 | 证据 |
| --- | --- | --- |
| 目标项目唯一 ready Editor | passed | [本轮环境诊断](D:/UnityProject/AshFramework/.codex/runs/20261027T125252Z-c2eba9ad/report.md) |
| 全部 11 个 C# 布局、解决方案覆盖与构建、whitespace、IDE0011 | passed | [本轮 C# 验证](D:/UnityProject/AshFramework/.codex/runs/20261027T125713Z-3928e40f/report.md) |
| 最终 Editor 编译 | passed | [编译终态](D:/UnityProject/AshFramework/.codex/runs/20261027T125713Z-84482005/report.md)：completed、failed=false、errors=[] |
| CLI 筛选测试 | blocked，未启动 | [本轮 Editor 工作流](D:/UnityProject/AshFramework/.codex/runs/20261027T125557Z-81bd19e3/report.md)：同一参数兼容性问题 |
| 原生 Test Runner，当前项目 EditMode，Run Selected | passed：78/78 | [本轮界面验收记录](D:/UnityProject/AshFramework/.codex/runs/20261027-plane-native-test-runner/report.md) |

原生界面计数为通过 78、失败 0，目标程序集全部通过；另有 86 项未选择的数学库测试未运行。测试期间临时创建的 GameObject 由 TearDown 清理，没有保存场景资源。用户误按 Escape 后重新授权继续控制，重连电脑控制会话后完成了本轮运行。

当前阶段不自动读取 Transform 缩放，不处理形状烘焙、复合体或逻辑高度规则。核心数值操作与 Unity 浮点输入边界保持分离；目标平台构建、性能及完整游戏状态确定性仍待后续验证。

## 任务 2：Body / Shape 与复合体

在后续用户确认后新增 WorldShape2D、CompoundShape2D、CompoundCollision2D、LogicalHeight，以及 Body/CircleShape/BoxShape 组件和共享的世界几何准备逻辑。Task 3 的 TryReadRigidPose 仍只读刚体姿态；任务 2 的组件准备层单独处理支持的缩放与局部中心。

- 最近 Body 归属、同物体及孙级收集、嵌套 Body 边界（包括 disabled Body）。
- 同级顺序只影响收集索引；移动后显式刷新，深层结构变更后显式重建。
- 正缩放、圆的均匀缩放、矩形正交轴；拒绝剪切、零/负缩放及不支持的姿态。
- 整体 AABB 覆盖全部子形状，奇数 raw 区间向外取整；不支持的整体范围明确失败。
- 缓存复制输入、复用容量；核心 SetShapes 验证失败保持旧数据，Body 刷新失败主动清空旧数据。
- Body 单值逻辑高度默认为 1，仅验证 1/2/3，无高度匹配或类别过滤。
- 没有自动检测循环、注册、事件、空间划分或 Gizmos；没有保存新的场景/Prefab。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| AshFramework 唯一 ready Editor、工具诊断 | passed | [本轮诊断](D:/UnityProject/AshFramework/.codex/runs/20261027T133934Z-0e4e1720/report.md) |
| 23 个 C# 源码的布局、解决方案覆盖与构建、whitespace、IDE0011 | passed | [本轮 C# 验证](D:/UnityProject/AshFramework/.codex/runs/20261027T134035Z-8f7449b8/report.md) |
| Editor 编译 | passed | [Editor 工作流](D:/UnityProject/AshFramework/.codex/runs/20261027T133941Z-39ce4e5b/report.md)：completed、failed=false、errors=[] |
| CLI 筛选测试 | blocked，未执行 | 同上，仍为 CLI/Pipeline 参数兼容性问题 |
| 同项目原生 Test Runner，EditMode | passed，118/118 | [本轮原生验收](D:/UnityProject/AshFramework/.codex/runs/20261027-body-native-test-runner/report.md) |

新增测试为 CompoundShapeTests 17 项、CollisionBodyBehaviourTests 23 项；连同已有 78 项共 118 项。通过 118、失败 0、目标跳过 0；另外 86 项未选择的数学库用例没有运行。所有临时测试对象由 TearDown 清理，测试不在参考项目运行。

当前仅在 Windows Unity Editor 验证。未执行 Player/IL2CPP、跨平台、性能、场景配置视觉验收；后续 Gizmos 阶段再做可视化检查。原有 ProjectSettings 修改保留，未新增依赖、修改 Math、第三方代码或手改 Unity 生成工程文件。

## 任务 6：形状与 AABB Gizmos

已新增仅在 Editor 编译的 AshFramework.Collide.Editor 程序集，包含独立预览容器与 DrawGizmo 绘制器。运行时核心、Body/Shape 组件没有增加绘制回调或 UnityEditor 依赖。

- 绿色 Circle/AABB/OBB 轮廓；选中所属 Body 或内部对象时增加黄色子 AABB、青色整体 AABB，以及平面、高度、形状数标签。
- 圆使用 64 段闭合折线；OBB 使用实际量化轴生成四角，矩形和包围盒不重复应用 Transform 矩阵。
- XZ 统一显示在 Body 世界 Y 平面，XY 统一显示在 Body 世界 Z 平面；逻辑高度不改变显示位置。
- 每次预览重新读取当前层级和姿态；不改写检测缓存、列表或 LastError，失效预览不保留旧几何。
- 最近 Body 归属、嵌套边界、禁用形状/Body 与已有几何准备规则一致。
- 选中无效 Body 或未归属 Shape 时显示红色提示；绘制恢复共享 Gizmos/Handles 状态。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 规范化项目路径、唯一 ready Editor | passed | [本轮诊断](D:/UnityProject/AshFramework/.codex/runs/20261027T135000Z-e7dac313/report.md) |
| 最终 26 个 C# 的布局、生成解决方案覆盖与构建、whitespace、IDE0011 | passed | [最终 C# 验证](D:/UnityProject/AshFramework/.codex/runs/20261027T140509Z-33827910/report.md) |
| 最终 Editor 编译 | passed | [Editor 工作流](D:/UnityProject/AshFramework/.codex/runs/20261027T140441Z-d756b80e/report.md) |
| CLI 筛选测试 | blocked，未启动 | 同上，CLI/Pipeline 参数兼容性问题保持单独记录 |
| 最终源码，同项目原生 Test Runner / EditMode | passed，130/130 | [原生与视觉验收记录](D:/UnityProject/AshFramework/.codex/runs/20261027-gizmos-native-validation/report.md) |
| 当前项目 Scene 实际绘制 | passed，限定以下范围 | 同上：XZ 顶视图及斜视图、XY、选中与未选中、无效圆缩放、标签颜色 |
| 场景清理 | passed | [清理后场景状态](D:/UnityProject/AshFramework/.codex/runs/20261027T140609Z-f1e3a3cd/report.md)：唯一未保存场景，isDirty=false、rootCount=1 |

新增 CollisionGizmoPreviewTests 12 项，包括六种形状/平面轮廓、局部中心和旋转、复合范围、层级边界、禁用对象、无效预览清除，以及不改写运行时缓存/错误信息。总数 130，通过 130、失败 0、目标跳过 0；另外 86 项未选择的数学库测试未执行。

视觉检查最初尝试附加场景时，Unity 因原场景未保存而拒绝；独立 Preview Scene 未完成视觉验收，已清理。最终在原场景中使用标记 DontSave 的临时对象检查，未保存场景/Prefab，随后清理对象、恢复选择和 Scene 视图，删除辅助脚本及 meta。Console 中保留首次场景创建失败的历史错误，没有清空用户日志。删除辅助脚本后生成工程曾暂时引用旧文件，首次构建失败记录保留；经 Unity 刷新自动同步后最终构建通过，没有手改 csproj/sln。

未执行 Player/IL2CPP、跨平台、运行时性能、Prefab Stage 或 PlayMode 的视觉验收；没有新增碰撞循环、过滤、注册、事件或空间划分。任务 2 的未提交修改和已有 ProjectSettings 修改保持保留。

## 后续：保留 Gizmos 示例场景

按用户要求，将上轮临时演示重建并通过 Unity Editor 保存为 Samples/Scenes/CollideGizmosXZ.unity 和 CollideGizmosXY.unity。每个场景包含一个 Level3 Body、局部中心偏移的圆、30° 旋转矩形、轴对齐矩形和正交相机。没有手改场景 YAML 或 meta，没有加入 Build Settings。

两个场景保存后均重新打开，通过 Editor 中的实际组件验证：平面及逻辑高度正确，RefreshGeometry 成功，收集数量为 3，形状顺序为 Circle/OBB/AABB，整体 AABB 有效。当前 Editor 保留打开 XZ 场景，Body 已选中，Scene 为 Top 视角并开启 Gizmos；保存辅助脚本已移出 Assets，源码归档在忽略的执行记录中。

- [场景与查看说明](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/Samples/README.md)。
- [最终 Editor 编译及测试入口记录](D:/UnityProject/AshFramework/.codex/runs/20261027T141436Z-97d2d07e/report.md)：编译 passed，CLI 筛选测试仍为参数兼容 blocked。
- [保存后当前场景状态](D:/UnityProject/AshFramework/.codex/runs/20261027T141520Z-ecb8a6f7/report.md)。
- [原生创建与重新打开验证记录](D:/UnityProject/AshFramework/.codex/runs/20261027-gizmos-saved-examples/report.md)。

此轮只保留示例场景和使用说明，没有修改碰撞业务 C#；未重复运行上轮已经通过的 130 项原生测试，也不把上轮结果记为本轮新测试。场景验证包含实际重新加载和 XZ 可视确认；没有执行 Player 或 PlayMode。

## 2026-10-28：第一阶段接口版核心世界

已完成 IBroadPhase2D、带世界归属的 CollisionBodyId、规范化 CollisionPair、内部记录和 CollisionWorld；使用记录数组、ID 映射、已注册槽位列表与待回收槽位，禁用保留身份，移除使旧 ID 立即失效但等待索引清理后才复用存储。公开 CompoundShape2D.CopyFrom 复制数值与缓存 AABB，不暴露世界的可变几何。

提交有效几何后，Step 统一同步全部索引再查询和精筛；最终结果排序。Body 数据错误暂停该对象，索引异常导致整步失败，下一步 Clear 重建后再查询。禁止检测中重入。没有具体空间算法、Unity 世界绑定、事件、位置修正、Jobs、unsafe 或 Math 修改。

| 检查 | 状态 | 证据 |
| --- | --- | --- |
| 目标项目唯一 ready Editor | passed | [最终目标确认](D:/UnityProject/AshFramework/.codex/runs/20261028T072408Z-d7784f62/report.md) |
| C# 布局、生成解决方案覆盖与构建、whitespace、IDE0011 | passed | [最终 C# 验证](D:/UnityProject/AshFramework/.codex/runs/20261028T072109Z-d64af993/report.md) |
| 最终 Editor 编译 | passed，completed/failed=false/errors=[] | [编译查询](D:/UnityProject/AshFramework/.codex/runs/20261028T072423Z-b6739428/report.md) |
| 目标用例发现 | passed，EditMode 158，Explicit 0 | [发现结果](D:/UnityProject/AshFramework/.codex/runs/20261028T072419Z-688b45c9/report.md) |
| 本项目原生 Test Runner | passed，158/158，失败 0，目标跳过 0 | [原生验收记录](D:/UnityProject/AshFramework/.codex/runs/20261028-core-world-native/report.md) |
| CLI 筛选测试 | blocked，参数兼容导致未执行 | [Editor 工作流](D:/UnityProject/AshFramework/.codex/runs/20261028T071723Z-a7ace6c5/report.md) |
| Player/IL2CPP、跨平台、性能、第二阶段 Unity 行为 | 未执行 | 不属于本轮验收 |

新增世界用例 27 项、复制用例 1 项，与既有 130 项一起运行。测试 Stub 仅返回预设候选和模拟失败，不提供运行时暴力或空索引兜底。覆盖同范围不同几何、相切、源清空不影响副本、全部同步先于查询、暂停恢复、槽位复用、多世界、重入、各索引操作错误、已经算出部分配对后失败及失败恢复。

首次构建因 Unity 生成工程未同步新增文件而 blocked；用 Unity 原生 Open C# Project 自动同步后重新检查通过。原 blocked 记录保留。新 meta 由 Editor 导入生成；没有手改工程文件或场景资源，没有安装或升级依赖。原有 ProjectSettings 变更保留，不自动提交。

## 第二阶段：Unity 世界绑定、生命周期与检查按钮

用户在第一阶段提交 9a639d7 后确认继续。新增 CollisionWorldBehaviour、World/Body Inspector、CollisionWorldValidation 及 23 项 Unity 接入测试；Body 增加显式绑定和生命周期。继续使用既有程序集边界，核心没有新增 Unity 引用，没有具体空间算法、事件或位置修正。

| 检查 | 状态 | 证据 |
| --- | --- | --- |
| 目标项目唯一 ready Editor | passed | [目标确认](D:/UnityProject/AshFramework/.codex/runs/20261028T091544Z-80bb1cd1/report.md) |
| C# 布局、生成解决方案覆盖、构建及格式 | passed | [C# 验证](D:/UnityProject/AshFramework/.codex/runs/20261028T091650Z-eaccdd9c/report.md) |
| Editor 编译 | passed | [Editor 工作流](D:/UnityProject/AshFramework/.codex/runs/20261028T091746Z-d9f90ae6/report.md) |
| 本项目原生 Test Runner，EditMode | passed，181/181，失败 0，目标跳过 0 | [本轮原生记录](D:/UnityProject/AshFramework/.codex/runs/20261028-unity-world-native/report.md) |
| CLI 筛选测试 | blocked，参数兼容导致未执行 | 同上 Editor 工作流，未去掉筛选器或升级依赖 |
| Inspector 外观视觉验收 | 未执行 | 只读按钮的查找、禁用对象、零对象和缓存不变逻辑已测试 |
| Player/IL2CPP、跨平台、性能 | 未执行 | 不作为本轮通过范围 |

本轮在原生 EditMode 选中 AshFramework.Collide.Tests.Editor.dll，Run Selected 后 181 通过、0 失败，另外 86 项未选数学测试未运行。用例覆盖未初始化、两种初始化/绑定顺序、禁用/未激活序列化绑定发现、身份保持、解绑/转移、销毁清理、XY/XZ 刷新与全部同步先于查询、Body 错误暂停/恢复、索引失败恢复、World 禁用及固定配置、非法输入、显式发现和重入拒绝。

初始化和显式 RefreshBindings 才全场景发现；每步复用已跟踪对象遍历缓冲，未实现完整增量脏标记优化。OnValidate 保持只标脏；运行时用 BindWorld，工具绕过入口的批量序列化修改后显式 RefreshBindings。Inspector 修改和当前 Inspector 的撤销/重做在主线程接入。没有 Update/FixedUpdate 自动检测。

新 meta 由 Unity 导入生成，生成工程由 Unity Open C# Project 同步，没有手改 csproj/sln、资源或场景，没有安装/升级工具及包。既有 ProjectSettings 修改保留；本阶段独立提交，说明场景另行记录。
