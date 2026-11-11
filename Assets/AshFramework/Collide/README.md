# AshFramework.Collide：精筛、碰撞世界、Unity 绑定与 Gizmos

当前实现纯 C# 二维几何精筛。核心程序集为 `AshFramework.Collide`，只引用现有 `AshFramework.Math` 和 .NET 基础库，不引用 UnityEngine，不使用 unsafe。

任务 3 已增加定点 XY/XZ 映射和独立的 `AshFramework.Collide.Unity` 适配程序集，后者负责显式读取 Unity 世界姿态。

## 已实现范围

- 世界空间 `Circle2D`、`Aabb2D`、`Obb2D` 不可变形状。
- 每种形状的 `GetBounds()`；AABB 提供 `Min`、`Max`。
- `Collision2D.Overlaps` 的六种无序配对、九个有序重载。
- 独立 EditMode 几何测试，包含固定种子的随机参考对照。
- `CollisionPlaneProjection` 的 Project、Embed、TryGetRotation。
- `UnityCollisionPlaneAdapter.TryReadRigidPose`，读取 Transform 世界平移和平面旋转。
- Body/CircleShape/BoxShape 挂载组件、最近 Body 归属、复合体收集和世界几何刷新。
- `WorldShape2D`、`CompoundShape2D` 与 `CompoundCollision2D.Overlaps`。
- `Collision2D.TryGetContact` 计算基础形状接触；`CompoundCollision2D.CollectContacts` 收集命中的子形状对，世界 Step 可按需输出接触。
- Body 单值逻辑高度 1/2/3 的保存和输入验证。
- Editor 独立程序集的形状轮廓、子形状 AABB、Body 整体 AABB 和配置错误提示。
- 接口版核心世界：世界归属 ID、注册/提交/暂停/移除、独立几何副本、统一索引同步、配对与失败恢复。
- `IBroadPhase2D` 契约及 `CompoundShape2D.CopyFrom`；测试用预设候选 Stub 不进入正式运行路径。
- `CollisionWorldBehaviour` 的显式初始化、固定范围、统一平面和 Step；Body 显式绑定、生命周期及 Inspector 检查按钮。
- `UniformGridBroadPhase2D` 固定范围均匀网格，逐项插入/更新/移除、跨格去重和桶容量复用。
- `CollideWorldOverview` 示例场景显式注入网格，展示真实碰撞对、Body ID、暂停原因与手动操作。

Unity 层提供单角色对固定障碍的位置修正。核心检测仍按几何报告接触。核心世界需要调用方提供空的、独占的索引，首个实现为均匀网格。

位置修正使用独立 `CollisionBodySettings` 配置 Body 的层，`CharacterMovement` 配置阻挡 Mask 与修正策略，由 `PositionCorrectionManager.TryMoveAndCorrect` 显式执行。当前处理 `Immediate + Snap`。成功时返回最终位置对应的配对与接触，失败时恢复请求前的位置、清空结果，下次 Step 重新同步。详见 [位置修正示例](Samples/README.md#单角色位置修正示例)。

后续世界与空间划分的最新讨论见 [碰撞世界与空间划分讨论备忘](Ref/CollisionWorldDiscussionMemory.md)（2026-10-28）。用户在接口及 Unity 接入阶段完成后，确认继续均匀网格实现与示例接入，已扩展此前“仅接口”的阶段范围。历史调查中的暴力基线、高度匹配等旧设想不作为最新需求。

外部项目的世界存储、接口与缓冲设计参考见 [LightweightPhysics 源码参考审查](Ref/LightweightPhysicsReferenceReview.md)。该文同时记录与当前需求不兼容的行为及 Math128/Jobs 的验证边界，不表示已迁入实现。

实施状态见 [接口版碰撞世界代码计划](Ref/CollisionWorldCorePlan.md)：核心、Unity 接入及首个均匀网格均已实现，采用普通 C# 容器。位置修正仍留待后续阶段。

## 数据与判定约定

### 核心世界使用约定

创建 `CollisionWorld(worldBounds, broadPhase)`，通过 `RegisterBody` 取得身份；注册初始暂停。调用 `TrySubmitGeometry` 复制准备好的复合体及高度，再显式调用 `Step(results, out error)`。核心不读取 Transform，不做运动积分或位置修正。

- `SuspendBody` 保留身份和容量；`UnregisterBody` 立即使旧 ID 失效，等 Step 清理索引后回收槽位。
- ID 只在所属世界使用，槽位复用不复用 ID。`ContainsBody`、`IsBodyActive`、`TryGetBodyBounds`、`TryGetLogicalHeight`、`GetBodyError` 提供只读观察，不暴露内部可变几何。
- 提交后 `IsBodyActive` 表示当前有效参与意图，索引在 Step 内统一同步；AABB 不变也仍更新精筛几何。
- 空/越界几何或非法高度暂停对应对象；世界边界允许完整 AABB 恰好贴边。高度只保存，不参与过滤。
- Step 清空调用者结果列表；返回 true 才代表本步完整结果，按规范化 ID 对排序。返回 false 时结果为空，不等于成功检测到零碰撞。
- 核心和 Unity 世界都可调用 `Step(pairs, contacts, out error)`。每个命中的 Body 对只出现一次，每个命中的子形状对各有一条接触。接触按 Body 对、A 子形状索引、B 子形状索引排序，索引对应本次提交的形状顺序。只移动 B 时沿法线，移动 A 时沿反向。失败时两个列表均为空。
- 索引同步/查询异常或返回未知/失效候选使整步失败；下次先 Clear 并重新插入当前有效记录。失败不回滚已提交的几何。
- 检测中禁止修改世界或嵌套 Step；索引 Query 单次追加必须无重复且覆盖全部相交、相切候选。
- `CopyFrom` 公开于已有可变复合体类型，复制数值和缓存 AABB，复用数组，不共享内部存储；世界记录不公开其副本引用。

第一阶段本项目原生 EditMode 158/158 通过；Unity 接入与说明场景增加 25 项，均匀网格及实际接入再增加 26 项，现为 209/209 通过，C# 构建/格式与 Editor 编译通过。CLI 测试参数兼容仍 blocked，原生通过单独记录；详见 [验收清单](TASK-CHECKLIST.md)。测试包含网格边界、跨格去重、更新/移除和随机查询参考对照，不作为性能或 Player 验证。

### Unity 世界接入

可直接打开 [CollideWorldOverview 示例场景](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/Samples/Scenes/CollideWorldOverview.unity)，按 [示例查看步骤](Samples/README.md) 初始化网格并手动 Step，观察真实碰撞对、稳定 ID、禁用、平面不一致和越界暂停原因。

### 均匀网格实现

创建 `UniformGridBroadPhase2D(worldBounds, cellSize, maxCellCount)` 后注入世界。范围使用同一份固定 AABB，cellSize 为正 Fix64；maxCellCount 默认 65536，可显式调整。配置超限或非法直接报错，不改变格子尺寸、不截断大物体范围，也不回退暴力。

- 使用固定格子表，按覆盖范围最小角对齐；最后一行/列可以小于格子边长。格子坐标直接由定点原始整数距离计算，支持负坐标及跨越有符号长整数差值上限的合法范围。
- 每个 Body 的整体 AABB 覆盖哪些格子，就登记到全部这些格子；内部代理保留覆盖格子矩形，更新时保留共有格、增加新格并移除旧格。
- Query 可部分或完全在覆盖范围之外；只遍历与世界相交的格子，并按闭区间 AABB 过滤。世界最大边界归最后一格，相切会被返回。
- 去重代数保存在内部代理上，回绕时重置；不按裸 ID 数值去重，不改变调用方列表原有顺序或内容，仅追加本次去重结果。最终碰撞对排序继续由世界负责。
- Insert/Update 在修改登记前完成 ID/范围校验和目标桶容量准备，正常提交阶段不再扩容；准备失败可能保留空桶或增长容量，但不改变原有 Body 逻辑登记。
- 空桶归还池，Clear 清空代理及结果但保留桶容量。该实现预分配格子表和桶池引用容量，内存随格子数增长；大范围 Body 仍需遍历全部覆盖格，不承诺固定成本或零 GC。
- 参考 LightweightPhysics 的跨格登记、桶复用及查询去重，按本项目接口重新实现，不引入其全量 Build、完整 Body 数组依赖、静态/休眠过滤或小数量暴力回退。

### Unity 入口与生命周期

在场景添加 CollisionWorldBehaviour，在 Body 的“显式绑定世界”字段指定它，或在主线程调用 BindWorld。不会按父层级或全局单例猜测世界；父子归属规则只用于收集 Shape。

| 入口 | 行为 |
| --- | --- |
| Configure(plane, boundsCenter, boundsHalfExtents) | 初始化前设置平面与固定绝对坐标范围；第二维对应 Y 或 Z |
| Initialize(broadPhase) | 注入空的独占索引，只能初始化一次；发现已绑定的场景 Body，包含禁用和未激活对象 |
| Body.BindWorld(world) | 受控绑定、解绑或转移；未初始化只记录绑定，初始化后才分配核心 ID |
| RefreshBindings() | 批量直接改写序列化绑定字段后显式发现，包括禁用对象；不是每步全场景扫描 |
| Step(results, out error) | 协调已跟踪绑定 → 刷新/校验/复制全部有效几何 → 核心统一同步索引并检测 |
| TryGetBodyId(body, out id) | 只读身份查询；未初始化、未绑定时 false，不制造假 ID |
| IsBodyActive(body)、GetBodyError(body) | 最近提交状态与暂停原因，不表示 Transform 已自动同步 |

调用方提供 broadPhase、world、body 及可复用的 results：

```csharp
world.Initialize(broadPhase);
body.BindWorld(world);
bool success = world.Step(results, out string error);
```

- 没有 Update/FixedUpdate 自动检测。业务逻辑每步移动对象后调用 Step；静止 Body 也参与几何刷新与检测。尚未实现完整增量脏标记优化，不承诺零 GC 或具体性能。
- Body 禁用/未激活时保留 ID 并暂停；启用后下一步重新提交。解绑、换世界或销毁使旧注册失效；索引物理移除在下一次核心 Step 完成。Unity 对从未激活对象可能不发 OnDestroy，世界准备阶段也会清理已销毁引用。
- World 禁用时暂停全部已注册 Body，保留身份；禁用期间 Step 返回 false。重新启用后可继续显式 Step。World 销毁清空独占索引；Clear 异常会写入 Unity Console，不替外部 Dispose 其他资源。
- 初始化后固定平面和范围。Inspector 锁定字段，Configure 拒绝修改；绕过入口改写序列化配置时 Step 失败并暂停，恢复原配置后可继续。
- Body 平面不一致、几何无效或完整 AABB 越界，仅暂停该对象并保留 GetBodyError 原因，其余对象继续；索引失败使整步失败。返回 true 时仍应检查各 Body 状态，空结果不代表所有 Body 配置有效。
- Body Inspector 在主线程接入字段变更及当前 Inspector 的撤销/重做；OnValidate 仍只标记几何脏状态。脚本修改应使用 BindWorld；工具批量序列化修改、未被当前 Inspector 接入的撤销/重做后可调用 RefreshBindings。
- World Inspector 的“检查平面一致性（包含禁用 Body）”按钮独立读取显式配置，未初始化也可用；只报告不修改配置，不刷新缓存，不注册或初始化世界。无 Body 明确报告检查数量为 0。
- 全场景发现只发生在 Initialize、显式 RefreshBindings 和 Editor 检查按钮。每步复用已跟踪绑定的遍历缓冲；既有几何准备与定点计算仍可能分配。
- 运行时初始化状态不序列化；脚本域重载后由业务重新注入索引。当前没有算法选择器、默认索引、事件或位置修正。

### 几何数据

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
| Editor | AshFramework.Collide.Editor，仅 Editor | Collide、Collide.Unity、Math、Math.Unity、UnityEditor |
| Tests/Editor | AshFramework.Collide.Tests.Editor | 三个碰撞程序集、Math、Unity Test Framework |

## 任务 2：Body / Shape 挂载与复合体

### 场景配置

从 Component → AshFramework → Collide 添加 Collision Body、Circle Shape 或 Box Shape：

```text
Player                         CollisionBodyBehaviour
├── Model                      普通表现对象
├── BodyShape                  CircleShapeBehaviour
└── WeaponShape                BoxShapeBehaviour
```

Body 选择 Plane（默认 XZ）和 Logical Height（Level1/2/3，对应值 1/2/3）。CircleShape 配置正半径，BoxShape 配置正半尺寸。Shape 的 Local Center 是所在物体局部平面的偏移；子 Transform 的位置、旋转和支持的缩放也参与世界几何计算。

局部中心在 XY 模式映射为 `(center.x, center.y, 0)`，XZ 模式映射为 `(center.x, 0, center.y)`，再通过 Shape 的完整 localToWorldMatrix 变换。不要把 XZ 模式 Local Center 的 Y 当作世界高度。

Shape 归属向上查找到的最近 Body，包括同一物体上的 Body。普通分组不形成边界；嵌套 Body 即使 disabled，仍形成独立归属边界，父 Body 不接管其形状。只收集启用且在活跃层级中的 Shape。收集顺序按 Unity 层级遍历和组件顺序；调整同级顺序只改变子形状索引，不改变拥有者。当前索引是缓存内位置，不是跨重建稳定 ID。

### 收集与刷新

| 入口/属性 | 行为 |
| --- | --- |
| RebuildShapes() | 重新收集所属启用形状，清空先前几何；下一步需要 RefreshGeometry |
| RefreshGeometry() | 必要时重建列表，更新全部世界形状和整体 AABB；返回成功与否 |
| ShapeCount / GetShape(index) | 最近一次收集的组件及顺序 |
| HasGeometry | 当前未被标记失效且有成功刷新的几何 |
| Geometry | Body 拥有的复合体缓存；无效时抛出异常 |
| TryGetBounds(out bounds) | 无有效几何时返回 false |
| LastError | 最近一次刷新失败原因，成功时清空 |
| RequestRebuild() | 标记列表/配置失效，不进行场景遍历 |

Body 启用时尝试刷新；Shape 启停、直接换父、参数 setter 和 Body 的直接子层级变化会请求重建。任意深层分组调整、运行时增加嵌套 Body 等操作，调用方应显式对受影响 Body 执行 RebuildShapes。

**在每次检测前调用 RefreshGeometry。** 组件不运行 Update/LateUpdate 检测循环；Transform 移动、旋转和缩放不保证立即使 HasGeometry 变为 false，因此该属性不表示当前帧姿态已自动同步。使用 CollisionWorldBehaviour 时由世界 Step 统一刷新。

Inspector 组件菜单提供 Refresh Collision Geometry，依次重建并刷新，便于手动准备数据；LastError 通过 API 获取，Body Inspector 另外显示世界内 ID、最近提交状态和世界记录的暂停原因。Scene 视图的配置问题提示由下述独立 Gizmos 预览提供。

Geometry 是同一个可复用容器，成功刷新会更新其内容，不是不可变的历史快照；调用方应只读使用，不要直接 SetShapes/Clear 修改 Body 缓存。任一启用形状无效时，Body 整次刷新失败并清空旧几何，不会忽略该形状后继续保留一个不完整的碰撞体。空 Body 不提供有效几何。

### 缩放与形状准备

- Shape 及其所有祖先要求三轴 localScale 为正有限值，零缩放和负缩放不支持。
- 世界变换后的两条平面轴必须正交并保持在所选平面中；矩形可按两轴分别烘焙正缩放，剪切组合被拒绝。
- 圆要求两轴缩放相等。数值上采用约 `1e-5` 的相对缩放容差；容差内的微小差别取较大轴缩放，不输出椭圆。
- Local Center、半径、半尺寸先由 Unity 输入边界量化为定点几何；极小尺寸若量化为零，或者世界形状/整体范围超出 Fix64 表示能力，刷新失败。
- 矩形准备为 Obb2D；仅当其量化轴严格轴对齐时转为 Aabb2D，避免把外接 AABB 当作旋转矩形的真实形状。
- 矩阵轴正交/平面误差使用明确数值容差，不能据此声称与无限精度 Unity 浮点图形完全重合。几何判定以准备后的定点形状为准。

### 复合体检测

WorldShape2D 通过 FromCircle/FromAabb/FromObb 创建，默认值无效；错误类型的几何访问器抛出异常。CompoundShape2D.SetShapes 复制输入，复用内部容量，先验证全部形状和整体范围再替换；失败保持原数据。Clear 清空数据和包围盒，空复合体与任何复合体不重叠。

整体 AABB 覆盖全部子形状；raw 范围跨度为奇数时，向一侧保守多覆盖一个单位，且处理 Fix64 坐标边缘。如果合并范围无法由一个合法的中心/半尺寸 AABB 表示，会明确抛出溢出异常。

CompoundCollision2D 先测整体 AABB，再遍历子形状对，经子 AABB 过滤后复用已有简单形状精筛。任何一对子形状相交即返回 true；两个复合体整体 AABB 相交，不代表它们的子形状必然相交。

```csharp
using AshFramework.Collide;
using AshFramework.Collide.Unity;

public static class CompoundCollisionExample
{
    public static bool Check(CollisionBodyBehaviour a, CollisionBodyBehaviour b)
    {
        if (a == null || b == null || a == b || a.Plane != b.Plane)
        {
            return false;
        }

        if (!a.RefreshGeometry() || !b.RefreshGeometry())
        {
            return false;
        }

        return CompoundCollision2D.Overlaps(a.Geometry, b.Geometry);
    }
}
```

示例由调用方明确传入两个不同、同平面的 Body。纯几何核心不知道 Body 身份或平面，自身几何与自身比较可以返回 true；世界层会排除 Body 自配对。世界绑定使用上述显式入口，没有注册事件。

LogicalHeight 仅保存 Level1/2/3；它不是世界 Y，也不影响当前几何检测。不同逻辑高度的两个复合体仍可能返回 true，具体玩法匹配、类别和公共过滤接口暂缓设计。

## 任务 6：Scene 视图 Gizmos

已保留可直接打开的 [XZ 示例场景](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/Samples/Scenes/CollideGizmosXZ.unity) 和 [XY 示例场景](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/Samples/Scenes/CollideGizmosXY.unity)，配置与查看方式见 [Samples/README](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/Samples/README.md)。

开启 Scene 视图工具栏的 Gizmos。绘制由 Editor 程序集的 DrawGizmo 回调完成；可在 Gizmos 下拉列表中切换 CollisionBodyBehaviour 的显示。未选中时绘制绿色真实形状轮廓；选中 Body，或选中归属于它的内部对象后，增加以下信息：

检查 XZ 时，关闭 Scene 视图的 2D 模式并切换 Top 视角；2D 模式默认沿 Z 轴观察，XZ 轮廓从侧面看会成为一条线。XY 可以直接使用 2D 视图。

| 显示 | 含义 |
| --- | --- |
| 绿色轮廓 | Circle、AABB 或 OBB 实际准备后的定点几何 |
| 黄色矩形 | 每个子形状的外接 AABB |
| 青色矩形 | Body 所有形状的整体 AABB |
| 青色文字 | 平面、逻辑高度 1/2/3、启用形状数量 |
| 红色十字和文字 | 配置无效、空 Body，或选中的 Shape 缺少所属 Body |

AABB 形状的绿色轮廓与黄色范围完全重合；整体 AABB 的部分边也可能与子形状范围重合，因此颜色可能相互覆盖。圆用 64 段闭合折线近似，仅影响显示，不改变圆形精筛。

预览每次读取当前层级和 Transform，并复用 TryBuildWorldShape 及 CompoundShape2D 的几何准备/合并逻辑。OBB 顶点使用实际量化后的 AxisX/AxisY，不重新单位化；浮点转换只在 Editor 绘制边界发生。绘制前后恢复 Gizmos.color、Gizmos.matrix、Handles.color。

同一 Body 的所有轮廓投影到统一显示平面：XZ 使用 Body.position.y，XY 使用 Body.position.z。子物体沿法线方向的位置差被投影掉，符合当前平面碰撞语义；逻辑高度只显示数值，不转换为世界高度或自动抬高图形。Body 变换由 Scene 工具显示，Gizmos 的世界轮廓不再乘一次父矩阵。

预览容器独立于 Body.Geometry，不调用 RefreshGeometry、RebuildShapes，也不改写 Body.LastError。移动物体后，Gizmos 能显示当前配置，但运行时检测仍须显式刷新。若一个启用形状无效，则本次 Body 预览整体失败，不显示旧范围或部分有效形状；选中对象查看错误提示。禁用/不活跃 Body、禁用 Shape 及嵌套 Body 边界遵循已有规则，未归属 Body 的形状不猜测其平面。

Editor 绘制会遍历层级并准备预览几何，只用于配置检查，不作为运行时更新或性能基准。Editor 程序集通过 includePlatforms 限制为 Editor，不进入 Player。

Unity 回调机制参考 [DrawGizmo 官方说明](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/DrawGizmo.html)。

## 验证与当前状态

验证必须在 `D:/UnityProject/AshFramework` 执行：

```powershell
python .codex/scripts/workflow.py verify --task csharp --include Assets/AshFramework/Collide
python .codex/scripts/workflow.py verify --task editor --test-filter AshFramework.Collide.Tests
```

任务 1 的测试覆盖六种配对、九个有序入口、相切与一个原始单位间隙、包含、旋转分离、极小形状、大坐标、非法/default 形状、保守 AABB、交换对称性、平移及核心程序集隔离。

随机对照使用固定种子：矩形参考采用角点、边相交与包含；圆–矩形参考采用点包含和到边的最近距离，与生产 SAT/局部投影采用不同路径。浮点参考仅用于中等坐标的随机用例；边界及极小值使用独立的确定性标准用例。随机对照不是全输入域证明。

任务 3 增加 22 项定点映射和 28 项 Unity 适配测试；任务 2 增加 17 项复合体和 23 项组件测试；任务 6 增加 12 项预览测试，历史合计 130 项。之后核心世界增加 28 项，Unity 世界接入增加 23 项，说明场景增加 2 项，均匀网格增加 19 项、实际网格接入增加 7 项。本轮在 AshFramework 的 Unity 原生 Test Runner 中重新运行整个碰撞测试程序集，209/209 通过，无失败或目标跳过；C# 构建、格式和 Editor 编译也已通过。执行结果和视觉验收范围见 [TASK-CHECKLIST.md](D:/UnityProject/AshFramework/Assets/AshFramework/Collide/TASK-CHECKLIST.md)。CLI 测试参数兼容性仍为 blocked，与原生界面的实际通过结果分别记录。
