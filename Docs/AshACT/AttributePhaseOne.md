# 属性模块第一阶段

本阶段实现位于 `Assets/AshACT/Attributes`。原始设计见 [AttributeSystemDesign.md](AttributeSystemDesign.md)。本模块不内置任何具体属性，由调用方显式分配 ID、创建配置并为每个角色创建独立 Store。

## 程序集与数值边界

- `AshFramework.FixedPoint` 包含现有 `Framework.FixedPoint` 源码；新增 asmdef 并将注释说明统一为中文，保留原有运算逻辑。`Math128` 保持 internal。
- `AshACT.Attributes` 单向引用 FixedPoint。两个运行时程序集均设置 `noEngineReferences: true`，没有 UnityEngine / UnityEditor 依赖。
- `AshACT.Attributes.EditModeTests` 是仅 Editor 使用的测试程序集。
- `AttributeMath` 使用框架自带的 `System.Numerics.BigInteger` 保存宽中间值，不依赖额外包或 `Int128`。加减乘除返回 `AttributeError`，只有完整 Raw 结果落在 long 范围内才成功；失败输出为零。乘法使用算术右移，除法对负数有余数时向负无穷取整，保持现有 FP64 约定。
- 配置值限制在 ±1,000,000,000。`FromDecimalText` 接受不带空白或指数的十进制文本，精确分数转换并按中点取偶数舍入，不经 float/double。入口保留比四位小数更多的输入精度以验证舍入；将来编辑器负责四位显示和输入精度限制。

## 创建和读取

`AttributeId(uint)` 使用显式 ID，0 无效，不按名称哈希。`AttributeConfiguration` 是不可变值，声明 ID、初始 BaseValue、静态范围、Stat/Resource 分类，以及可选的资源上限 Stat ID。

`AttributeStore.TryCreate(configurations, out store)` 验证配置并复制数据；失败时不返回 Store。角色属性集合在创建后固定，`AttributeIds` 按 ID 升序只读提供。`TryGetValue` 返回 BaseValue/CurrentValue 的值副本，不暴露可变集合。允许空集合。

一般 Stat 的 BaseValue 通过瞬时命令修改并截断到静态范围；CurrentValue 由最新 BaseValue 和持续修改器重算，其截断不反写 BaseValue。Resource 不接受持续修改器，其 CurrentValue 等于 BaseValue。可选最大值必须引用同一 Store 的 Stat；先结算所有 Stat，再按资源自己的静态范围和上限 Stat 的 CurrentValue 永久截断 Resource BaseValue。上限恢复不自动补足。动态上限低于资源下限时拒绝整个事务。

## 命令与原子性

调用 `TryExecute(IReadOnlyList<AttributeCommand>)` 提交有序命令批次。返回值含结构化 Error、相关 AttributeId 和失败 CommandIndex（初始化为 -1）。在模拟线程按确定顺序调用，不支持并发写入或重入回调。

| CommandKind | 作用 |
| --- | --- |
| Instant | 按 Add/Multiply/Divide/Override 修改 BaseValue，执行后截断。 |
| AddPersistent | 注册常量持续修改器，只影响 Stat CurrentValue，要求非零 ModifierHandle。 |
| RemovePersistent | 按 AttributeId 和 ModifierHandle 移除，使用最新 BaseValue 重算剩余项。 |

Multiply 的完整倍率必须非负；Divide 的完整除数必须至少为 1，零单独返回 DivideByZero。所有数值输入仍受统一配置值范围限制。Override 优先级只作用于持续修改器；瞬时命令按列表顺序执行。

持续聚合严格使用 `A = ΣAdd`、`M = max(0, 1 + Σ(Multiply - 1))`、`D = 1 + Σ(Divide - 1)`，普通结果为 `((Base + A) × M) ÷ D`。Override 选最高优先级，同级选最后应用。存在胜出的 Override 时使用其值，普通乘除不执行；已遍历的 Add/M/D 累加仍检查溢出。没有 Override 时，乘积先检查溢出再除法，不能以最终结果可表示或会截断为理由放过中间溢出。

ModifierHandle 由调用方显式分配，仅在所属 Store 内唯一；调用方须保留所属角色信息并将移除命令路由到该 Store。句柄在活跃期间不能重复。应用顺序由 Store 的单调 `LastApplicationSequence` 分配，与句柄数值、名称和 Dictionary 遍历顺序无关。移除不倒退序号，失败事务不消耗序号。

每条命令在私有工作副本中执行并重算全部属性，边界影响对批次后续命令可见。例如同一批次先降低资源上限再恢复，资源仍保留已截断值。整个批次全部成功后才替换 Store 状态；溢出、除零、属性缺失、句柄错误或非法资源范围均丢弃全部数值、修改器增删及候选应用序号。没有在事务内发布事件。

当前实现采用复制和完整重算以优先保证正确性，未针对大量属性/修改器优化分配和计算成本。

## 使用示例

以下 ID 仅为调用示例，不在运行库预置：

```csharp
AttributeId id = new AttributeId(1001);
AttributeConfiguration[] configuration =
{
    new AttributeConfiguration(id, FP64.FromInt(100), FP64.Zero, FP64.FromInt(1000))
};
AttributeResult creation = AttributeStore.TryCreate(configuration, out AttributeStore store);
if (!creation.Succeeded)
{
    return;
}

AttributeCommand[] commands =
{
    new AttributeCommand(AttributeCommandKind.AddPersistent, id,
        AttributeOperation.Multiply, FP64.FromRaw(FP64.OneRaw + FP64.HalfRaw), 1),
    new AttributeCommand(AttributeCommandKind.AddPersistent, id,
        AttributeOperation.Multiply, FP64.FromRaw(FP64.OneRaw + FP64.HalfRaw), 2)
};
AttributeResult applied = store.TryExecute(commands);
if (!applied.Succeeded)
{
    return;
}

store.TryGetValue(id, out AttributeValue value);
// value.BaseValue = 100，value.CurrentValue = 200。
```

## 验证与后续边界

针对性测试位于 `Tests/Editor`，测试命名空间为 `AshACT.Attributes.Tests`。测试覆盖瞬时/持续倍率区别、偏置式除法、Override 顺序、移除重算、静态/动态边界、资源规则、事务回滚、角色实例隔离、精确转换、数值边界和同输入重复执行。

```powershell
python .codex/scripts/workflow.py --project D:/UnityProject/AshFramework verify --task csharp --include Assets/AshACT/Attributes
python .codex/scripts/workflow.py --project D:/UnityProject/AshFramework verify --task editor --test-filter AshACT.Attributes.Tests
```

第一阶段交付时尚未实现效果运行时；后续已在[第二阶段](AttributePhaseTwo.md)实现只读效果定义/实例、周期、帧调度与标记。动态表达式、跨角色依赖、生命周期冻结快照、事件发布、完整状态保存恢复和可视化编辑器仍未实现。重复执行测试只验证本模块在当前 Windows Editor 环境下的 Raw 和序号结果，不代表完整帧同步、网络回滚或跨平台确定性验收。
