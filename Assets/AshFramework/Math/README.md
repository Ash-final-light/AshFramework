# AshFramework.Math

通用、可复现的定点数学库。用于逻辑计算、移动与转向、几何、程序化数据和可回放计算，不依赖碰撞系统、场景、网络或时钟。

可运行的使用示例见 [Example/README.md](Example/README.md)，支持编辑器菜单直接运行与运行时组件调用。

## 程序集与目录

| 目录 | 程序集 | 依赖 |
| --- | --- | --- |
| `Runtime` | `AshFramework.Math` | .NET 基础库、`System.Numerics.BigInteger`；`noEngineReferences=true` |
| `UnityAdapters` | `AshFramework.Math.Unity` | 数学核心、UnityEngine；显式转换边界 |
| `Tests/Editor` | `AshFramework.Math.Tests.Editor` | 核心、转换程序集、Unity Test Framework |
| `Example` | `AshFramework.Math.Example` | 核心、转换程序集、UnityEngine；使用示例与运行时入口 |
| `Example/Editor` | `AshFramework.Math.Example.Editor` | 示例程序集、UnityEditor；菜单入口 |

命名空间分别为 `AshFramework.Math`、`AshFramework.Math.Unity` 和 `AshFramework.Math.Tests`。
没有迁入旧项目的碰撞代理、四叉树、事件、指针、NativePool 或帧调度。

## 数值契约，版本 1

- `Fix64` 是有符号 **Q32.32**：`实际值 = RawValue / 4294967296`。
- 有效范围是 `[-2147483648, 2147483648 - 2^-32]`，最小单位为 `2^-32`，约 `2.3283064365e-10`。
- 它与旧项目中 `Precision=1000` 的 `LFloat` **不是同一格式**，原始值、存档和消息不能直接混用。
- 加减、负号和最终结果超范围时抛出 `OverflowException`；零除数抛出 `DivideByZeroException`。
- 乘除、分数构造、文本量化及宽中间值收窄统一向零截断；不会静默饱和。
- 乘除、点积、几何行列式及矩阵求逆使用 `BigInteger` 保护中间精度。中间值可宽于 64 位，最终返回值仍必须可表示。
- `Sqrt` 对负数抛出异常，非负数返回向下取整的定点平方根；误差小于一个原始单位。
- `RoundToInt` 的中点远离零；`FloorToInt` 向负无穷取整，`CeilToInt` 向正无穷取整，`TruncateToInt` 向零截断。
- `FixMath.Floor/Ceil/Round` 返回 `Fix64`，因此向上取整超出该类型范围时仍会抛出溢出异常。
- 不承诺无 GC、Burst 兼容或优于浮点实现的性能。`BigInteger` 是正确性优先的选择，需要在目标平台上测量实际热点。
- 所有值类型不可变。`default(Fix64)`、`default(FixVector2/3)` 为零；`default(FixQuaternion)` 是零四元数，**不是单位旋转**；`default(FixTransform2D)` 的缩放为零。
- `[Serializable]` 不代表 Unity Inspector 会序列化只读属性。Inspector 资源应由项目自己的可序列化原始值包装/烘焙流程接入；持久化用下述原始值协议。

构造和文本：

```csharp
using AshFramework.Math;

Fix64 a = Fix64.FromInt(2);
Fix64 b = Fix64.FromRatio(3, 2);
Fix64 c = Fix64.FromRaw(6442450944L); // 同样表示 1.5
Fix64 d = Fix64.Parse("1.25");
Fix64 e = Fix64.FromDecimal(1.25m);

string exact = b.ToString();
bool identical = Fix64.Parse(exact).RawValue == b.RawValue;
```

`Parse/TryParse` 使用固定的 `.` 小数点，接受前后空白、可选正负号、小数点和十进制数字；不接受千分位、指数、NaN 或 Infinity。
`ToString()` 和 `G/R` 格式返回可按位往返的精确十进制文本；其他格式使用 decimal，供显示使用，不作为精确存储。

## 标量与角度

`FixMath` 包含：

- `Abs/Sign/Min/Max/Clamp/Clamp01`、显式容差的 `Approximately`。
- `Sqrt`、整数指数 `Pow`；约定 `Pow(0,0)=1`，负指数先取倒数。
- `Lerp/LerpUnclamped/InverseLerp/MoveTowards/SmoothStep`。
- `Repeat/PingPong`；长度必须为正。
- `Sin/Cos/SinCos/Tan/Atan/Atan2/Asin/Acos`。
- `DegreesToRadians/RadiansToDegrees/NormalizeRadians/NormalizeDegrees/DeltaAngle/LerpAngle/MoveTowardsAngle`。

`Lerp`、`SmoothStep` 和 `LerpAngle` 将参数限制在 `[0,1]`；`LerpUnclamped` 可外插。
`InverseLerp` 将结果限制在 `[0,1]`，起终点相等时返回零。
`MoveTowards` 系列的最大变化量必须非负。

除 `DegreesToRadians`、`RadiansToDegrees`、`NormalizeDegrees` 的显式度数边界外，所有角度 API 都使用**弧度**。
二维旋转为逆时针，三维旋转使用右手定则。
`NormalizeDegrees` 返回 `[0,360)`；`NormalizeRadians` 与 `DeltaAngle` 使用以 `Pi` 为边界的半开区间，恰好半周选择负方向。
由于常量的最终 Q32.32 量化，区间边界与数学上的精确 pi 允许一个原始单位的偏差。
`LerpAngle/MoveTowardsAngle` 返回规范化后的方向，不保留累计转数。

三角函数使用 **48 次 CORDIC 迭代**与固定 Q48 角度常量，不在运行时调用浮点三角函数或生成查找表。
角度常量是一次离线采样的整数；更改常量、迭代次数、舍入或算法时必须升级数值版本并重新运行标准向量测试。
`Atan2(0,0)` 明确返回零；`Asin/Acos` 对 `[-1,1]` 以外输入抛出异常，不隐式修正调用者数据。
`Tan` 在余弦量化为零时抛出除零异常，结果超范围时抛出溢出异常。

当前测试用例对正反三角函数的常见角度/输入使用绝对误差 `2e-8`，对度数往返使用 `1e-7` 度。
这些是测试覆盖点的验收容差，**不是全输入域误差上界**。尤其反三角函数在定义域端点附近会放大量化误差。

```csharp
Fix64 current = FixMath.DegreesToRadians(350);
Fix64 target = FixMath.DegreesToRadians(10);
Fix64 next = FixMath.MoveTowardsAngle(current, target, FixMath.DegreesToRadians(5));
// next 对应 -5 度，等价于 355 度。
```

## 向量、旋转与变换

`FixVector2/3` 提供长度、点积/叉积、距离、安全归一化、限长、移动、投影、反射、插值和有符号/无符号夹角。

- `TryNormalize` 对零向量返回 false；`Normalized/NormalizeOrZero` 返回零；`NormalizeOrFallback` 使用调用者提供的原样后备值。
- 归一化使用宽整数长度计算，支持只有一个原始单位的极小向量，避免先量化长度导致方向严重失真。
- 零方向的向量夹角返回零；零投影方向的投影返回零；反射使用归一化后的法线。
- `FixVector3.SignedAngle` 的符号来自 `axis dot cross(from,to)`；符号为零时选择非负角度。
- `SqrMagnitude/DistanceSquared` 的最终结果仍是 `Fix64`，大距离平方可能超范围。仅比较距离时使用 `WithinDistance`，它直接比较宽整数平方。
- 向量减法、投影标量等其他中间结果若无法用返回类型表示，会抛出溢出异常；不要据此假设所有全范围组合都可计算。
- `FixVector2.Rotate/FromAngle` 使用弧度；`Perpendicular` 返回逆时针垂直向量。

`FixQuaternion` 提供 `Identity/AngleAxis/Euler/FromToRotation/LookRotation`、归一化、共轭、逆、旋转向量、夹角和 `Nlerp/Slerp/RotateTowards`。

- `Euler(x,y,z)` 按 `qY * qX * qZ` 组合，即先 Z，再 X，最后 Y。
- `left * right` 先应用 right；调用时使用角度函数名与本文约定，不依赖其他库的隐含坐标约定。
- 零四元数的归一化、求逆、旋转会抛出异常；`TryNormalize` 返回 false 并输出 Identity。
- 零旋转轴、零 FromTo 方向，以及零/平行的 LookRotation 方向会被拒绝。
- `q` 和 `-q` 数值上不相等，但表示同一旋转；插值会选择同一半球，按最短路径插值。
- `FromRotationMatrix` 要求正交旋转矩阵，不负责剥离缩放、反射或剪切。

`FixMatrix3x3` 提供行/列访问、矩阵与向量乘法、转置、行列式、求逆、四元数转矩阵和二维齐次变换。
矩阵元素命名 `M行列`，使用列向量；`left * right` 先应用 right。
`TryInverse` 仅对原始行列式严格为零返回 false，逆矩阵不能表示时抛出溢出异常；不隐藏近奇异矩阵的放大效应。

`FixTransform2D` 按**缩放 → 旋转 → 平移**变换点；方向忽略缩放和平移，向量包含缩放。
零缩放不能执行逆变换。
`Compose(child)` 返回矩阵，因为非均匀缩放与旋转组合可能产生剪切，不能用简单的角度/位置相加替代。

```csharp
FixTransform2D transform = new FixTransform2D(
    new FixVector2(10, 20),
    FixMath.DegreesToRadians(90),
    new FixVector2(2, 3));
FixVector2 world = transform.TransformPoint(FixVector2.Right);
FixVector2 local = transform.InverseTransformPoint(world);
```

`FixGeometry` 是独立几何工具：二维方向判定、二维/三维线段最近点、二维点到线段距离、三角形重心坐标、二维二次和三维三次 Bezier。
零长度线段返回端点；退化三角形的 `TryBarycentric` 返回 false；Bezier 参数限制在 `[0,1]`。
该模块不注册物体，不产生事件，不引用碰撞系统。

## 随机数、分数累计与原始值协议

`FixRandom` 使用版本固定的 SplitMix64，显式 `unchecked` 回绕。
整数区间采样用拒绝采样消除直接取模的偏差；不同区间调用可能消耗不同数量的随机输出。
同一算法、状态与调用顺序才能复现结果；它不是密码学随机数。

```csharp
FixRandom random = new FixRandom(123);
ulong snapshot = random.State;
int value = random.Range(-3, 4); // 包含 -3，不包含 4。
random.RestoreState(snapshot);
int replay = random.Range(-3, 4); // 与 value 相同。
```

`Range(min,max)` 相等时不消耗状态，返回边界；反向区间抛出异常。
`NextInt/NextUInt64(max)` 的 max 必须大于零。
`NextUnit` 返回 `[0,1)`；另有 `Shuffle/OnUnitCircle/InsideUnitCircle/OnUnitSphere`。
圆内按面积、球面按面积分布，均受最终定点量化影响。
逻辑随机与表现随机应使用不同实例。

`RationalAccumulator` 是通用整数除法余数累计器，不读取时钟或调度逻辑帧。
例如将每秒一个单位分配到 60 次更新，可以保留余数，使 60 次累计的原始值恰好等于一个单位：

```csharp
RationalAccumulator portions = new RationalAccumulator(60);
Fix64 position = Fix64.Zero;
for (int i = 0; i < 60; i++)
{
    position += Fix64.FromRaw(portions.Accumulate(Fix64.Scale));
}
// position == Fix64.One；如需回放，同时保存 Denominator 与 Remainder。
```

`Fix64.FromRatio(1,60)` 仍然是单个量化值，直接反复相加不会凭空恢复被截断的余数。
逻辑帧调度、输入队列、对象生命周期和回滚由上层系统实现。

`FixBinary` 明确使用小端 8 字节原始整数；向量按 X/Y/Z，四元数按 X/Y/Z/W 排列。
写入前检查整个值的缓冲区范围，长度不足不做部分写入。
格式本身不带版本头；上层消息/文件应写入 `Fix64.FormatVersion` 并拒绝不兼容格式。
不要直接序列化结构体内存布局，也不要经 float、double 或格式化显示文本中转。

`StableHash64` 是 FNV-1a 诊断校验，字段顺序由调用者明确决定；相同哈希不能证明状态绝对相同。
调用者应按稳定对象顺序添加全部权威状态，并包含随机状态、累计余数、版本与帧号。

## Unity 边界

```csharp
using AshFramework.Math.Unity;

// 逻辑计算结果 -> 表现。
unityTransform.position = logicalPosition.ToUnityVector();

// 显式输入量化边界。量化完成后分发相同原始值，不让各端独立浮点模拟。
FixVector2 input = unityInput.ToFixVector();
```

浮点转换只在 `UnityAdapters` 中提供，核心没有隐式 float/double 转换。
NaN、Infinity 被拒绝，超范围转换抛出异常。
float/double 可能丢失低位，尤其最大值转换后不能保证往返。显示值不应回写成权威逻辑状态。

## 验证与验收

验证命令必须在 AshFramework 工程执行：

```powershell
python .codex/scripts/workflow.py verify --task csharp --include Assets/AshFramework/Math
python .codex/scripts/workflow.py verify --task editor --test-filter AshFramework.Math.Tests
```

测试包含数值边界、精确文本往返、取整、独立浮点参考值、极小/极大向量、旋转与逆矩阵、非均匀缩放组合、随机标准序列与恢复、字节序标准向量，以及 1000 步状态恢复回放。
本机通过不等于跨平台已验证；目标平台、后端与实际游戏状态回放仍需项目接入后执行。
自动化工作流状态及本次实测见 `TASK-CHECKLIST.md`。

本次范围不包含 4×4 矩阵、一般实数指数/对数、噪声、样条弧长表、完整三维几何库、Inspector 绘制器及网络帧同步系统；这些可按实际需求扩展，不作为通用数学核心的隐式依赖。
