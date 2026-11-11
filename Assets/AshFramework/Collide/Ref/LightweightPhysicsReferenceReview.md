# LightweightPhysics 源码参考审查

审查日期：2026-10-28。目标项目：`D:/UnityProject/AshFramework`。

**实施更新：** 用户后续确认首个均匀网格阶段，AshFramework 已按自身 IBroadPhase2D 重新实现 UniformGridBroadPhase2D。吸收完整 AABB 跨格登记、桶列表复用和查询代数去重；使用固定范围格子表，代理保留格子范围，提供逐项 Insert/Update/Remove。没有复制参考模块的全量 Build、完整 Body 数组依赖、双静态/休眠过滤、暴力回退或精筛边界规则。本文下方“本阶段只定义接口”的描述保留原审查时点；实际当前状态以 README/TASK-CHECKLIST 为准。

参考目录：`D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics`。另读取其依赖 `Assets/Framework/Physics/FixedPoint` 的部分源码。

本文记录只读源码审查结论，供碰撞世界、数据存储和空间索引接口设计参考。没有迁入实现，没有运行参考项目的构建、测试、Burst 编译或性能测量；参考项目不能作为 AshFramework 的验收结果。文中“建议”不自动成为用户确认或实施授权。最新需求以 [碰撞世界讨论备忘](CollisionWorldDiscussionMemory.md) 及用户后续指令为准。

## 1. 参考模块的实际范围

- 世界拥有 Body 数值存储，通过 IBroadphase 接入粗筛。
- 包含运动积分、粗筛、精筛、碰撞响应、休眠、触发器配对差分和快照恢复。
- 单个 Body 只有一个 Circle 或 AABB；没有当前 AshFramework 的 OBB 与复合体模型。
- 粗筛包含 UniformGridBroadphase、BruteForceBroadphase；网格实现还提供查询侧的 ISpatialIndex。
- 本次读取的模块源码没有使用 Unity Jobs、NativeArray、BurstCompile 或 unsafe。结构体数组不等于已经实现 Jobs 或验证 Burst 兼容。
- 参考项目记录的 Unity 版本为 2022.3.62f1c1；版本相同也不能代替本项目验证。

## 2. 值得借鉴的结构

### 2.1 构造注入粗筛，世界不绑定具体算法

[LWPhysicsWorld 构造入口](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/World/LWPhysicsWorld.cs) 可接收 IBroadphase，检测流程通过接口调用粗筛。这支持当前 CollisionWorld 组合 IBroadPhase2D 的方向。

两个项目的接口语义不同：

| 项目 | LightweightPhysics | AshFramework 当前方案 |
| --- | --- | --- |
| 索引输入 | 完整 LWBody 数组及已分配槽位范围 | Body ID 与整体 AABB |
| 更新方式 | 每步 Build 生成全部候选对；查询侧 Refresh 重建 | Insert、Update、Remove、Query、Clear |
| 候选表示 | 数组索引对 LWPair | Body ID，经世界规范化为无序碰撞对 |
| 过滤位置 | 粗筛读取静态、休眠、Layer/Mask | 索引只处理几何范围，玩法过滤尚未设计 |

参考的 [IBroadphase](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/Collision/IBroadphase.cs) 负责生成配对，[ISpatialIndex](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/Collision/ISpatialIndex.cs) 负责查询。可借鉴明确职责，不立即复制双接口，也不把完整 Body 暴露给索引。当前范围查询接口已可支撑世界配对，批量配对入口留待实际负载评估。

### 2.2 稳定 ID 与内部槽位分离

LWPhysicsWorld 保存递增 ID、ID 到槽位的字典、Body 数组和空闲槽位列表。删除后复用槽位，但不直接复用旧 ID。

可借鉴：外部使用稳定身份，内部可独立回收或调整存储位置。未来候选内部若使用数组索引，必须限定其有效阶段，对外结果仍使用 Body ID，不能把跨帧槽位当成稳定身份。

注意：虽然注释称“稠密数组”，实际 RemoveBody 清空槽位并加入空闲列表，没有压紧剩余对象；遍历上限 mCapacity 是历史已分配最高槽位加一，不是活跃数量。大量删除后仍可能扫描空槽。吸收 ID/槽位分离思路，不将其误记为已压紧的活跃数组。

### 2.3 修改入口集中，保护世界状态

GetBody/TryGetBody 返回数值副本，SetBody 写回并保留世界管理的 ID 与 Alive/Static 等字段，同时标记索引脏。

可借鉴：外部只提交允许修改的数据，身份、参与检测状态和索引状态由世界维护；不暴露可随意修改的内部存储，也不允许绕过入口改变几何却漏掉索引同步。

LWBody 的主要数据是值类型。若 AshFramework 的记录包含 CompoundShape2D 引用，复制记录并不会复制内部形状数组；仍需专用几何复制入口，不能用结构体赋值冒充独立几何副本。

### 2.4 复用容量与有效长度

世界的 Snapshot(LWSnapshot reuse) 只在容量不足时扩容，分开保存 buffer 容量与逻辑长度，使用 Array.Copy 等复制有效数据，并处理旧尾部内容。

这是当前几何副本的存储管理参考：为世界长期保留容器，稳定容量下覆写数据，不每步创建新容器。它的快照用于回放/预测回滚，与组件提交本步几何不同；借鉴复制和容量管理，不顺带加入 Snapshot/Restore。

源码：[LWSnapshot](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/World/LWSnapshot.cs)、[世界 Snapshot/Restore](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/World/LWPhysicsWorld.cs)。实际分配量和耗时未测量。

## 3. 空间索引与缓冲复用的参考

[UniformGridBroadphase](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/Collision/UniformGridBroadphase.cs) 包含：

| 做法 | 可借鉴点 | 边界 |
| --- | --- | --- |
| 格子桶列表池 | 重建时复用列表容量 | 需关注峰值容量及长期保留的内存 |
| 候选对列表、配对集合复用 | 减少反复创建集合 | 明确清空时间与结果有效期 |
| 查询代数标记数组 | 跨格去重，不必每次清空整张标记表 | 依赖内部槽位管理，需处理代数回绕 |
| 候选对排序 | 消除字典枚举顺序对结果的影响 | 有排序成本，不等于完整锁步验证 |
| 按完整 AABB 覆盖格子 | 不只按中心登记对象 | 大范围对象的跨格数量仍需限制或专门设计 |

本阶段只定义接口，不提前引入桶池、标记数组或具体网格实现。索引内部定位信息不进入公共形状；最终 Body 配对顺序由世界统一约定。

其配对使用内部槽位排序，事件再使用规范化 ID。不要混淆“槽位顺序稳定”“ID 对规范化”和“跨运行完整确定性”三种保证。

## 4. 脏标记的真实含义

LWPhysicsWorld 的 mSpatialDirty 在增删、SetBody 和物理步结束后设置。查询前 PrepareSpatialIndex 按需调用 Refresh；该 Refresh 清理格子并重新遍历 Body 建索引。

它的收益是把多次修改合并成真正查询前的一次重建，不是只更新变化 Body 的格子。每步粗筛 Build 也不因该标记而自动成为增量更新。

可借鉴更新时机与显式失效概念，但不能直接称为完整的每 Body 脏更新系统。AshFramework 仍需区分结构、几何和索引变化，并保证查询前完成全部同步。

## 5. Jobs 与宽整数计算的后续参考

### 5.1 本模块没有实现 Jobs

它使用普通结构体数组、Dictionary、List、HashSet，没有 Job 调度或 NativeContainer。数组存储可作为未来批量输入布局的参考，但不代表这些管理对象和接口调用能直接放进 Burst Job。

Step 在遍历候选对时立即调用 ContactSolver.Resolve，通过 ref 修改双方位置和速度。不同候选对可能共享 Body，因此不能直接将这段循环改成每对一个并行任务；只读检测和共享状态求解要分别设计。

### 5.2 Math128 是研究材料，不是可直接替换的 Math

依赖的 [FP64](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/FixedPoint/FP64.cs) 用 [Math128](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/FixedPoint/Math128.cs) 的 long/ulong 宽乘除路径，没有沿用当前 AshFramework Fix64 的 BigInteger 方式。可研究固定宽度整数计算如何减少任意精度运算开销，不要求 unsafe。

直接迁移前存在语义差异与验证要求：

- FP64 乘除采用向负无穷取整，当前 Fix64 采用向零截断，边界结果不等价。
- 参考运算存在截位/回绕路径，不能替代当前溢出检查约定。
- 当前 OBB 等谓词有更宽的中间结果，不能未经范围证明全部改为 128 位。
- 源码注释中的跨平台一致、性能或零分配说明，不作为本轮实测证据。
- 未进行目标 Burst 编译、目标平台构建或数值对照；不宣称这套 Math 已能直接用于当前 Jobs/Burst。

当前不替换 Math、不增加 Jobs 或 unsafe。若以后进入此方向，单独审查舍入、溢出、极值和精筛几何语义。

## 6. 不带入当前方案的内容

| 参考模块行为 | 与当前范围的关系 |
| --- | --- |
| 未提供粗筛时回退 BruteForceBroadphase | 当前缺少索引要明确未初始化，不做正式暴力兜底 |
| 小规模阈值默认走暴力配对 | 当前不引入；其默认配置针对少量刚体，不作为 ARPG 参数依据 |
| 查询无 ISpatialIndex 时线性扫描 | 当前不自动引入类似回退策略 |
| 粗筛排除双静态、双休眠及 Layer/Mask 不匹配 | 尚未确认这类过滤语义，当前索引保持几何职责 |
| 质量、速度积分、阻尼、冲量与位置修正、休眠 | 当前是碰撞检测世界，不扩成刚体物理求解器 |
| 触发器事件、快照回滚、射线、扫掠与轨迹预测 | 留作后续议题，本阶段不实现 |
| 单 Circle/AABB Body | 不能替代现有 Circle/AABB/OBB 复合体 |

相切语义也不同：[Narrowphase](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/Collision/Narrowphase.cs) 的圆配对在 distSq >= rSq 时返回 false，AABB 配对在 overlap <= 0 时返回 false，即相切不碰撞。AshFramework 已确认相切算相交，继续使用已有精筛，不搬用这些边界判断。

配置见 [LWPhysicsConfig](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/World/LWPhysicsConfig.cs)；求解见 [ContactSolver](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/Collision/ContactSolver.cs)；查询见 [Raycast](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/Queries/Raycast.cs)、[Sweep](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/Queries/Sweep.cs)、[Trajectory](D:/UnityProject/TapTap_GameJam/Assets/Framework/Physics/LightweightPhysics/Queries/Trajectory.cs)。

## 7. 对 AshFramework 当前设计的建议

优先吸收四点：构造注入索引、稳定 ID 与内部存储分离、受控数据修改入口、复用容量与有效长度。

内部是否采用槽位数组仍是存储设计候选，不改变已确认的世界独立几何副本、显式绑定、禁用保留身份、Body 错误暂停与显式 Step 约定。不得把“已阅读并记录参考代码”当成用户已经批准迁入其全部接口或功能。

本阶段继续只建设接口版世界的设计；具体空间索引、运行中切换、Jobs、Math128、事件和物理求解均不自动加入。若后续实施，所有构建、测试和性能验证必须在 AshFramework 及实际目标环境中执行。
