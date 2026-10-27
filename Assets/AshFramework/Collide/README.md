# AshFramework.Collide：精筛核心与平面适配

当前实现纯 C# 二维几何精筛。核心程序集为 `AshFramework.Collide`，只引用现有 `AshFramework.Math` 和 .NET 基础库，不引用 UnityEngine，不使用 unsafe。

任务 3 已增加定点 XY/XZ 映射和独立的 `AshFramework.Collide.Unity` 适配程序集，后者负责显式读取 Unity 世界姿态。

## 已实现范围

- 世界空间 `Circle2D`、`Aabb2D`、`Obb2D` 不可变形状。
- 每种形状的 `GetBounds()`；AABB 提供 `Min`、`Max`。
- `Collision2D.Overlaps` 的六种无序配对、九个有序重载。
- 独立 EditMode 几何测试，包含固定种子的随机参考对照。
- `CollisionPlaneProjection` 的 Project、Embed、TryGetRotation。
- `UnityCollisionPlaneAdapter.TryReadRigidPose`，读取 Transform 世界平移和平面旋转。

当前不包含 Body/Shape 挂载组件、复合体收集、逻辑高度、类别过滤、碰撞世界、事件、空间划分、射线、法线/穿透深度及 Gizmos。这些后续模块可以共用本核心。

## 数据与判定约定

| 项目 | 约定 |
| --- | --- |
| 数值 | `AshFramework.Math.Fix64` / `FixVector2`，Q32.32 |
| 坐标 | 已准备好的二维世界坐标，核心不读取 Transform |
| 圆 | Center 为圆心，Radius 必须大于零 |
| 矩形 | Center 为中心，HalfExtents 为正半尺寸；完整尺寸为其两倍 |
| OBB 旋转 | 逆时针弧度，不把传入角度当作度数 |
| 边界 | 相切和边界接触均算相交，无隐藏接触容差 |
| 默认值 | `default` 形状无效，`IsValid=false`；检测及取包围盒抛出 ArgumentException |
| 非法尺寸 | 构造时抛出 ArgumentOutOfRangeException |
| 坐标范围 | 整个 AABB 必须可由 Fix64 表示，超范围构造抛出 OverflowException |

调用者负责组合父子变换并准备世界形状。局部矩形旋转后应准备为 OBB；矩形的外接 AABB 是保守范围，不能代替原矩形的精筛几何。

## 使用示例

```csharp
using AshFramework.Collide;
using AshFramework.Math;

Circle2D circle = new Circle2D(new FixVector2(0, 0), 1);
Aabb2D aabb = new Aabb2D(new FixVector2(2, 0), new FixVector2(1, 1));
Obb2D obb = new Obb2D(
    new FixVector2(2, 0),
    new FixVector2(1, 1),
    FixMath.DegreesToRadians(45));

bool touchesAabb = Collision2D.Overlaps(circle, aabb);
bool overlapsObb = Collision2D.Overlaps(circle, obb);
Aabb2D obbBounds = obb.GetBounds();
FixVector2 minimum = obbBounds.Min;
FixVector2 maximum = obbBounds.Max;
```

`touchesAabb` 为 true：圆的右端与 AABB 左边界相接。所有有序类型组合均可直接调用：

| 无序配对 | 算法 | 反序调用 |
| --- | --- | --- |
| Circle–Circle | 宽整数距离平方比较 | 支持 |
| Circle–AABB | 最近点距离平方比较 | 支持 |
| Circle–OBB | 补偿量化轴长度的局部投影距离比较 | 支持 |
| AABB–AABB | 两轴闭区间重叠 | 支持 |
| AABB–OBB | 双方四条轴的 SAT | 支持 |
| OBB–OBB | 双方四条轴的 SAT | 支持 |

这里使用强类型重载完成配对选择，没有未注册时默默返回 false 的类型分派表。后续增加通用 Shape 数据时，应在适配层显式分派到这些入口，并对不支持类型报错。

## OBB 的精度与保守包围盒

OBB 使用固定整数三角函数生成轴：`AxisX=(cos,sin)`、`AxisY=(-sin,cos)`。对于明确输入的 `0`、`±FixMath.HalfPi`、`±FixMath.Pi` 使用精确坐标轴，保证这些常用轴对齐姿态的接触一致。其他角度按 SinCos 的量化结果定义几何，不声称等同于无限精度三角函数。

在实际采用的量化轴下，矩形点集定义为：

```text
P = Center + AxisX × u + AxisY × v
|u| ≤ HalfExtents.X
|v| ≤ HalfExtents.Y
```

上述乘加作为有理几何理解，内部检测保留宽整数分子，不先把角点或投影截断成 Fix64。两条轴严格垂直且长度相等，但长度未必数学上恰好为一；圆–OBB 距离比较显式补偿这一长度，SAT 也使用相同轴定义。

OBB 的 AABB 半尺寸按投影结果向外取整到原始整数单位，保守包含这一点集。它可能比精确投影多不到一个原始单位，不会因向零截断缩小范围。若将来用容差扩大精筛接受范围，需要同步更新包围盒契约。

圆距离、矩形投影和跨大坐标的差值使用 `BigInteger`，避免中间平方、半径和或点积超出 Fix64。构造时仍限制最终包围盒的可表示范围。本实现不承诺零 GC、Burst 兼容或具体性能目标，后续需在目标负载评测。

## 任务 3：XY/XZ 平面适配

| 模式 | 世界位置映射为逻辑二维 XY | Embed 显式补入的 normalCoordinate |
| --- | --- | --- |
| XY | `(x, y, z)` → `(x, y)` | 世界 Z |
| XZ | `(x, y, z)` → `(x, z)` | 世界 Y |

核心 Project/Embed 只复制定点分量，保留原始位，使用固定世界原点且不执行偏移或缩放。Project 也可以用于方向，但不会归一化。非法 CollisionPlane 值抛出 ArgumentOutOfRangeException。

```csharp
FixVector3 worldPosition = new FixVector3(10, 5, 20);
FixVector2 position = CollisionPlaneProjection.Project(worldPosition, CollisionPlane.XZ);
FixVector3 restored = CollisionPlaneProjection.Embed(position, 5, CollisionPlane.XZ);

// position 为 (10, 20)，restored 为 (10, 5, 20)。
```

normalCoordinate 是真实世界坐标。它与后续由角色单值设置的逻辑高度 1/2/3 无关；二维 Position.Y 在 XZ 模式下始终代表世界 Z。

TryGetRotation 读取世界局部 X 轴的平面投影，并调用现有 FixMath.Atan2 得到逆时针弧度；零投影返回 false、角度为零。该函数仅处理方向，不验证整个物体是否发生平面外倾斜。方向的极小 raw 分量及大分量不需要先计算长度平方。

### Unity 姿态输入

在有 UnityEngine 引用的调用方，使用 `AshFramework.Collide.Unity.UnityCollisionPlaneAdapter.TryReadRigidPose`。入参为 Transform、CollisionPlane、out FixTransform2D，以及可选 planeTolerance。

```csharp
using AshFramework.Collide;
using AshFramework.Collide.Unity;
using AshFramework.Math;
using UnityEngine;

public static class CollisionPoseExample
{
    public static bool TryCreateBox(Transform source, FixVector2 halfExtents, out Obb2D box)
    {
        box = default;
        if (!UnityCollisionPlaneAdapter.TryReadRigidPose(
            source, CollisionPlane.XZ, out FixTransform2D pose))
        {
            return false;
        }

        box = new Obb2D(pose.Position, halfExtents, pose.Rotation);
        return true;
    }
}
```

示例要求 halfExtents 已经按实际形状尺寸准备且符合 OBB 构造约定；TryReadRigidPose 的成功不替代形状参数验证。

读取约定：

- 使用 Transform 的世界 position 和 rotation，包含父级变换的世界结果。
- 将世界局部 X 轴投影后计算角度。Unity 绕 Z 正转 90° 在 XY 模式约为逻辑 +90°；绕 Y 正转 90° 在 XZ 模式约为逻辑 -90°。
- 输出 FixTransform2D.Scale 恒为 One。该接口只读取刚体姿态，不读取、烘焙或修改 Transform 的缩放；后续 Shape 组件负责尺寸与缩放支持规则，不能因读取成功就认定层级无剪切。
- XY 模式要求旋转保持世界 +Z 法线，XZ 模式要求保持世界 +Y 法线。翻转法线和明显的平面外倾斜返回 false。
- planeTolerance 表示旋转后法线与基准法线的最大弦长，默认 `0.00001`，约对应 `0.000573°`；有效范围为有限数值 `[0,1)`。这是姿态读取容差，不是几何接触容差，也不读取逻辑高度。
- 空/已销毁对象、不可表示的平面位置、非有限或不合格的旋转返回 false，out pose 为 Identity。非法平面和容差抛出参数异常。
- 仅量化参与所选平面的两个位置分量。因此 XZ 模式忽略的世界 Y 即使超出 Fix64 范围，也不妨碍读取合法的 XZ 位置。
- 适配器不反向修改 Transform。浮点输入的量化沿用 Math.Unity 约定，不承诺让此前的浮点模拟获得跨端确定性。

目录与依赖：

| 目录 | 程序集 | 依赖 |
| --- | --- | --- |
| Shape / Geometry / NarrowPhase | AshFramework.Collide | AshFramework.Math、.NET 基础库；无 UnityEngine |
| UnityAdapters | AshFramework.Collide.Unity | Collide、Math、Math.Unity、UnityEngine |
| Tests/Editor | AshFramework.Collide.Tests.Editor | 两个碰撞程序集、Math、Unity Test Framework |

## 验证与当前状态

验证必须在 `D:/UnityProject/AshFramework` 执行：

```powershell
python .codex/scripts/workflow.py verify --task csharp --include Assets/AshFramework/Collide
python .codex/scripts/workflow.py verify --task editor --test-filter AshFramework.Collide.Tests
```

任务 1 的测试覆盖六种配对、九个有序入口、相切与一个原始单位间隙、包含、旋转分离、极小形状、大坐标、非法/default 形状、保守 AABB、交换对称性、平移及核心程序集隔离。

随机对照使用固定种子：矩形参考采用角点、边相交与包含；圆–矩形参考采用点包含和到边的最近距离，与生产 SAT/局部投影采用不同路径。浮点参考仅用于中等坐标的随机用例；边界及极小值使用独立的确定性标准用例。随机对照不是全输入域证明。

任务 3 增加 22 项定点映射和 28 项 Unity 适配测试。本轮在 AshFramework 的 Unity 原生 Test Runner 中重新运行整个碰撞测试程序集，78/78 通过，无失败或跳过；C# 构建、格式和 Editor 编译也已通过。执行结果和限制见 [TASK-CHECKLIST.md](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/TASK-CHECKLIST.md)。CLI 测试参数兼容性仍为 blocked，与原生界面的实际通过结果分别记录。
