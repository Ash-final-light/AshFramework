# 碰撞世界与空间划分讨论备忘

更新日期：2026-10-28。目标项目：`D:/UnityProject/AshFramework`。

用途：保存本轮讨论上下文，便于后续提及“碰撞世界备忘”“接口方案”“脏标记和指针收益”时继续讨论。本文是需求与方案记忆，不是实现记录，也不证明代码已经通过测试。

旧调查和实施草案中与本文不一致的历史表述，不应覆盖下列用户已确认事项；未来用户的新指令优先。尤其不要恢复“先做暴力枚举”“高度集合匹配已确定”或“只做四叉树”等旧设想。

## 1. 用户已确认的需求与本轮范围

- 面向平面 3D ARPG 和 2D 游戏。3D Unity XZ 映射到内部定点二维 XY；2D 使用 Unity XY。世界 Y 与逻辑高度分开。
- 角色逻辑高度只允许 1/2/3，目前仅保存数据。高度匹配、类别、阵营和公共过滤接口尚未确定。
- 最近祖先 Body 决定 Shape 归属；嵌套 Body 是边界，禁用也不让内部 Shape 自动归属外层 Body。Hierarchy 顺序影响形状索引，不决定拥有者。
- 地图有固定关卡范围。碰撞覆盖范围需容纳完整几何；具体坐标及边缘余量留到关卡配置，越界处理见第 1.1 节。
- 同一个碰撞世界统一使用 XY 或 XZ。用户要求提供平面一致性检查按钮。
- 最终希望能够通过运行时配置选择空间哈希或松散四叉树。“运行时选择”是否还包含游戏运行中更换算法，尚未确认。
- 不先实现暴力候选枚举作为正式检测路径。
- 最新阶段要求：先使用空间划分接口，不实现具体空间划分技术。世界不能直接绑定四叉树；本轮讨论不因阅读参考代码而扩大到 unsafe 或算法实现。
- 最初只保存文档记忆；用户后续要求“完成第一步”，已实施核心阶段，详见第 2 节状态。仍不自动提交。
- 用户后续明确要求“忘掉 jobs 布局”：按普通 C# 对象、数组及集合设计，不再把 Jobs/Burst、NativeArray 或为其准备的存储布局作为当前目标。历史参考中的 Jobs 讨论仅保留为审查背景。

### 1.1 2026-10-28 后续确认：五项设计约定全部接受

用户在五项确认清单后回复“都接受”。以下从建议转为已确认设计；这次确认没有授权开始业务实现。

| 项目 | 已确认约定 |
| --- | --- |
| 几何所有权 | 世界保存独立的形状数值副本并复用容器；Body 负责生成几何，世界负责检测。副本不包含 GameObject 或 Transform |
| 世界绑定 | Body 显式指定一个 World，不自动查找全局世界或按父级推断。未绑定 Body 可继续单独使用几何与 Gizmos，但不参与世界检测 |
| 禁用与身份 | 禁用时保留绑定、世界记录和 ID，仅暂停检测并移出索引；重新启用并准备有效几何后恢复。销毁、解除绑定或转移世界时移除旧注册；新世界分配新身份 |
| Body 数据错误 | 无效几何、平面不一致或越界时，报告具体原因，暂停该 Body 并撤下旧索引项；恢复有效后重新加入，不继续沿用旧范围 |
| 检测驱动 | 核心与 Unity 入口提供显式 Step，由外部逻辑帧在移动等逻辑完成后调用；首版不默认在 Update 或 FixedUpdate 自动驱动 |

- Body 数据错误不阻断其他有效对象检测；但空间索引自身同步或查询失败时，本次世界检测失败，不发布不完整的正常结果，也不把上一轮结果冒充本轮结果。
- 世界使用明确配置的“碰撞覆盖范围”，检查完整 AABB，不裁剪形状；具体范围数值无需在架构阶段先确定。
- 平面检查按钮只检查并报告，不自动修改配置；检查明确绑定给该世界的 Body，包含禁用对象。
- 游戏运行中切换算法仍暂缓；本次没有将“都接受”扩展为对具体算法、完整脏标记优化、事件、锁步或 unsafe 的实现授权。

### 1.2 后续记录：四项借鉴方向与位置修正

- 用户要求记录接口注入、稳定 ID 与内部槽位分离、受控数据修改、数组及缓冲复用，并考虑未来位置修正。
- 具体内部记录数组、ID 映射、空闲/已注册槽位列表属于当前建议的落地方式，不将讲解等同于用户逐项批准所有容器细节。
- 位置修正尚未确定算法、移动规则或权威位置来源。当前只有布尔精筛；未来需接触方向、深度及子形状信息，再决定修正量。首阶段不实现求解器。
- 未来修正必须经过受控更新，使权威姿态、全部世界形状、整体 AABB 与索引一致；不能只改 Position 或索引范围，也不能下一步用旧输入覆盖修正结果。
- 复合体可能多处接触，需要明确复检/迭代及结果时点；推开已有重叠不等于完整防穿墙。
- 下一步详细代码计划见 [接口版碰撞世界代码计划](CollisionWorldCorePlan.md)。本次仍仅记录与规划，不开始业务实现。

## 2. 当前源码事实

**实施状态更新：** 用户后续已要求“完成第一步”。2026-10-28 第一阶段核心世界及测试已完成；不再沿用此前“仅记录、未实现核心”的时点描述。Unity 世界组件、具体算法和位置修正仍未实现，不自动提交。

- 已有 Circle/AABB/OBB 精筛、平面适配、Body/Shape 组件、复合体与整体 AABB、逻辑高度数据、Editor Gizmos。
- `AshFramework.Collide` 仅引用 Math，`noEngineReferences=true`、`allowUnsafeCode=false`；Unity 适配和 Editor 各有独立程序集。
- 已有 CollisionWorld、CollisionBodyId、CollisionPair、内部记录及 IBroadPhase2D。没有具体空间算法、事件或 Unity 世界驱动。测试 Stub 仅预设候选与模拟失败。
- `CollisionBodyBehaviour.Geometry` 返回反复复用的可变 `CompoundShape2D`。重建、刷新失败、禁用会清空该缓存；已经取得的引用也会看到清空结果。
- `CompoundShape2D` 有 Count、索引器、SetShapes、Clear 和新 CopyFrom；仍未实现 IReadOnlyList，世界使用 CopyFrom 复制已验证形状及缓存 AABB。
- 移动后显式 RefreshGeometry；深层结构调整后显式 RebuildShapes。当前组件没有完整的增量几何脏标记系统。
- Gizmos 独立读取配置，不刷新或改写 Body 检测缓存与 LastError。
- 当前布尔精筛采用相切算相交，不读取逻辑高度。
- 核心 Step 已按规范化 ID 对排序输出，未要求完整锁步。本轮原生碰撞测试 158/158 通过，其中世界测试 27 项和复制测试 1 项为新增；CLI 参数阻塞与原生通过分别记录。

主要源码：[Body](../UnityAdapters/Components/CollisionBodyBehaviour.cs)、[复合体](../Shape/CompoundShape2D.cs)、[精筛](../NarrowPhase/CompoundCollision2D.cs)、[程序集](../AshFramework.Collide.asmdef)。

## 3. 当前接口与世界结构（第一阶段已实现）

```text
CollisionWorld
    ├── 世界内部 Body 记录、注册身份与参与检测状态
    ├── IBroadPhase2D（由外部提供具体实现）
    ├── CompoundCollision2D（复用现有精筛）
    └── 规范化 CollisionPair 结果
```

- 已新增 BroadPhase/IBroadPhase2D；World/CollisionWorld、CollisionBodyId、CollisionPair，以及内部 CollisionBodyRecord。具体行为见 README。
- 接口仅处理 Body ID 与整体 AABB，提供 Insert、Update、Remove、Query、Clear。索引不单独注册每个子 Shape。
- Query 向调用方列表追加候选，覆盖所有相交和相切的已登记 Body，允许额外候选；本次追加无重复、不保证顺序，不负责清空或去重调用方原有内容。
- Update 不隐式注册，成功后不残留旧空间位置；Remove 建议幂等。参数和支持范围失败必须明确，不能静默截断或留下半次更新。
- 节点、格子、对象在索引中的存储位置都属于具体索引内部，不能写进公共形状或暴露为世界依赖。
- 世界遍历全部有效 Body、每次查询无重复时，可只处理 A.Id < B.Id，排除自身及反向重复，再调用已有复合体精筛。
- 拟定顺序：处理生命周期变化 → 刷新全部 Body → 同步几何并移除失效索引项 → 同步全部 AABB → 查询与精筛 → 整理结果。未来事件在检测完成后统一派发，回调变化延后处理。
- 只定义接口的阶段，不创建具体算法占位类、算法选择枚举、工厂或算法设置。没有真实索引实现时，Unity 层明确未初始化；不以空查询结果假装检测成功，不加入运行时暴力兜底。
- 测试可使用预设候选的 Stub 验证世界流程。Stub 仅在测试程序集，不作为正式算法；这类测试不能证明未来空间索引不漏报。
- 核心、Unity、Editor 沿用现有程序集边界，目前不增加新的程序集。

## 4. Unity 检查按钮约定与建议

用户已确认需要按钮，且已接受显式世界绑定和只检查、不自动修改配置的行为。其他呈现细节按下列建议推进：

- 在 World Inspector 提供“检查平面一致性”，按明确归属于该世界的 Body 检查，包含禁用/不活跃对象。
- 报告对象路径、实际平面与世界要求平面；没有待检查 Body 时明确说明，不能显示成全部通过。
- 按钮只读，不自动改平面、不移动对象，不调用 RefreshGeometry/RebuildShapes 改写缓存。
- 运行时注册及几何同步也检查平面一致性，因为 Body.Plane 可修改。
- Body 显式绑定一个 World；不把最近祖先 Body 的形状归属规则直接套用成世界归属规则。未绑定 Body 不参与世界检测。
- 平面枚举一致性与形状倾斜/缩放是否合法是不同检查；完整几何校验不应冒充已确认的按钮范围。

## 5. 尚未确认的设计

| 事项 | 当前建议或解释 | 状态 |
| --- | --- | --- |
| 游戏运行中切换索引 | “帧间”指两次碰撞逻辑 Step 之间；新索引重建成功后替换，保留 Body ID，失败保留原索引 | 用户仅询问含义，未确认需要 |
| 具体算法的根配置 | 固定关卡适合固定根，但具体算法暂不实现 | 后续算法阶段再确定；世界越界处理已确认，见第 1.1 节 |
| 哈希超大物体 | 固定地图不等于单个 Body 覆盖格数有限到可接受程度 | 格长、上限及超限处理未定 |
| 位置修正 | 后续扩展接触与移动策略，经受控入口保持全部几何/索引一致 | 用户要求纳入考虑，算法、规则和姿态来源未定，本阶段不实现 |

## 6. 脏标记的收益与限制

- 目的：减少没有变化的对象重复生成几何、计算 AABB、更新索引；同一逻辑步内多次变化可以合并成一次最终更新。
- 主动通知能减少轮询，但所有修改入口必须可靠通知；扫描比较容易接入外部 Transform 修改，但仍有扫描成本。
- 参考代码 PhysicsBody.Update 仍逐个读取并比较位置，只将变化对象加入集合；主要减少树重插次数，没有省掉全部对象的 Update。
- AshFramework 还需覆盖旋转、缩放、局部中心、尺寸、Shape 启停、结构归属及父级变化，不能只比较 Body 位置。
- 概念上区分结构变化、几何变化、索引变化。几何可能改变但整体 AABB 不变：此时精筛几何仍需更新，通常无需更新索引范围。
- 未标脏不等于不参与碰撞：静止物体仍应被查询，持续重叠对象未来仍可能需要 Stay。
- 示例：10,000 个 Body 中只有 1,000 个变化，增量更新可能将相应维护工作量降到约十分之一；这是工作次数示例，不是整个系统十倍提速的实测结论。
- 若大部分对象都变化，收益减少，还会增加标记、队列或集合管理成本。候选查询和精筛成本不会自动同比下降。

## 7. 指针的潜在收益与限制

- 与脏标记独立；脏标记不需要 unsafe。
- 共享同一份可变几何数据不要求指针，普通对象引用也能共享。是否复制缓存是所有权问题，不是指针与安全代码的二选一。
- 原生内存池可复用形状和节点存储；收益来自存储、分配和池化设计的组合，不能全归因于指针。
- 记录 ParentNode 便于快速定位移除，但这一收益来自定位信息；内部节点索引、句柄或 ID 映射也可实现。
- 指针不保证连续存储、缓存友好或更快。参考池会分别申请内存块，不等于紧凑数组。
- 指针允许底层寻址和原生互操作，但访问收益需对照实际运行后端、数据布局与热点测量；不预设相对结构体数组的固定收益。
- 原生内存不由 GC 管理，需要管理生命周期。参考代码仍使用 Dictionary/HashSet/List，因此不等于没有托管分配或 GC。
- 当前 CompoundShape2D 已复用容量，候选与结果集合也可复用；这些不要求 unsafe。
- 若热点主要在候选过多、精筛或定点宽整数运算，换指针未必解决问题。
- 优先顺序建议：正确同步与移除 → 容器复用 → 根据实际变化比例增量更新 → 测量后再评估布局和 unsafe。未授权引入 unsafe。

官方依据：[C# 指针与 GC](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/unsafe-code)、[Unity 集合与数组复用](https://docs.unity.cn/6000.1/Documentation/Manual/performance-garbage-collection-best-practices.html)。

## 8. Collision2D_Unsafe 源码参考审查

参考目录：`D:/UnityProject/LockstepCollision/Assets/Collision2D_Unsafe`。只读参考，不修改，不作为 AshFramework 的测试或性能验证。

值得吸收：显式 Step、ID 映射、复用缓冲、集中记录变化、未来比较前后碰撞对。其扩展点是 CollisionSystem 的子类 DetectBodyVsBody，不是独立空间索引接口；AshFramework 保持世界组合 IBroadPhase2D 的方案。

| 已核实的源码事实 | 对当前方案的启示 |
| --- | --- |
| CollisionSystem.RemoveBody 只删字典，没有同时清理遍历列表和树节点 | 世界统一协调移除与清理 |
| LDemoScript.OnUpdate 先 Step，再迁移脏对象 | 检测前必须先同步全部索引 |
| dirtyBodys 与 QuadTree 查询临时列表是 static | 变化队列和查询缓冲按世界实例保存，避免互相覆盖 |
| Circle 内保存 QuadTree* ParentNode | 定位记录封装在索引内部，不污染公共形状 |
| 配对按 a.Id * 常量 + b.Id 编码，未规范化顺序 | 使用明确的无序 Body 对，避免反向重复和 ID 范围假设 |
| Test 内立即回调 | 未来检测完后再统一派发事件 |
| QuadTree 按中心选子节点，按普通节点范围查询，没有松散范围 | 不能直接当成松散四叉树模板；大物体跨节点完整性需重新设计 |
| 查询用位置与 size 直接构造 LRect，而构造接收最小角与完整尺寸 | 核实中心/最小角及半尺寸/完整尺寸语义，不能照搬 |
| LRect.Overlaps 使用严格比较 | 不符合当前相切算相交的统一语义 |
| 系统和树直接引用 UnityEngine，依赖原生池及共享内存布局 | 不直接迁入现有纯核心程序集 |

主要参考文件：[CollisionSystem](D:/UnityProject/LockstepCollision/Assets/Collision2D_Unsafe/QuadTree/CollisionSystem.cs)、[QuadTree](D:/UnityProject/LockstepCollision/Assets/Collision2D_Unsafe/QuadTree/QuadTree.cs)、[PhysicsBody](D:/UnityProject/LockstepCollision/Assets/Collision2D_Unsafe/QuadTree/PhysicsBody.cs)、[Demo 更新顺序](D:/UnityProject/LockstepCollision/Assets/Collision2D_Unsafe/QuadTree/LDemoScript.cs)、[查询入口](D:/UnityProject/LockstepCollision/Assets/Collision2D_Unsafe/QuadTree/CollisionSystemQuadTree.cs)、[LRect](D:/UnityProject/LockstepCollision/Assets/Math/BaseType/LRect.cs)、[NativePool](D:/UnityProject/LockstepCollision/Assets/NativeUtil/NativePool.cs)。

上述风险来自静态审查，没有在本轮运行测试复现。参考代码的耗时注释没有受控对照条件，不能作为本项目采用指针或具体算法的性能证据。

### 8.1 LightweightPhysics 源码参考审查

另只读审查了 `D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics`，详细结论见 [LightweightPhysics 源码参考审查](LightweightPhysicsReferenceReview.md)。

- 值得参考：构造注入粗筛、稳定 ID 与内部槽位分离、受控修改入口、复用快照与查询缓冲。
- 所谓稠密数组实际保留空槽，扫描到历史分配上限；空间脏标记控制的是按需全量重建，不是每 Body 增量更新。
- 本模块没有使用 Jobs/Burst；依赖的 Math128 可作宽整数研究材料，但舍入、溢出和中间位宽与当前 Math 不等价，未验证 Burst 编译或性能。
- 不迁入默认暴力回退、小规模暴力阈值、双静态/休眠过滤、Layer/Mask、求解器或回滚功能。参考精筛相切不碰撞，与当前相切算相交不符。
- 记录参考结论不改变第 1.1 节的已确认约定，也不扩大当前仅接口、无具体算法的范围。没有修改参考项目或以其结果替代本项目验收。

## 9. 后续继续讨论时的核对顺序

1. 先读取本备忘和实际源码，确认期间是否已有实现或用户的新决定。
2. 保持当前仅接口、无具体算法的范围，按 [核心世界代码计划](CollisionWorldCorePlan.md) 安排核心交付再接 Unity；第 1.1 节五项不再作为未定，不重新引入 Jobs 布局目标。
3. 固定碰撞覆盖范围的数值留到关卡配置；越界暂停该 Body、撤下旧项并报告，索引自身错误导致本步失败。区分已确认行为与尚未确定的具体算法参数。
4. 具体算法、事件、完整锁步和 unsafe 继续作为独立后续议题。
5. 实现和验证均在 AshFramework 目标项目执行；历史通过记录与当次实际验证分开。
