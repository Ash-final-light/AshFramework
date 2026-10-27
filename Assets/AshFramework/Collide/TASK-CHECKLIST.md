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
