# 数学库使用示例

所有示例使用中文注释，不依赖碰撞系统。示例会在 Console 输出输入、结果和关键说明，不修改场景物体，也不读写外部文件或发送网络消息。

## 直接运行

等待 Unity 编译完成后，在菜单中选择：

**Tools → AshFramework → Math → 运行全部示例**

不需要创建 GameObject，也不需要进入 Play。菜单中还可以按主题分别运行。

## 作为运行时组件使用

1. 在自己的场景中创建一个空 GameObject。
2. 添加 `MathExampleRunner` 组件，也可以通过 **Add Component → AshFramework → Math → 数学库使用示例** 添加。
3. 保持 `Run On Start` 勾选，进入 Play，查看 Console。
4. 也可以使用组件右键菜单，运行全部示例或指定主题。

组件只输出结果，不接管对象移动、场景更新或逻辑帧调度。

## 按主题阅读代码

| 文件 | 演示内容 |
| --- | --- |
| [ScalarExamples.cs](ScalarExamples.cs) | 分数、文本和原始值构造；四则运算；负数取整；插值；平方根；整数幂；弧度与最短转向 |
| [VectorAndTransformExamples.cs](VectorAndTransformExamples.cs) | 安全归一化；移动与限长；范围判断；投影与反射；四元数、矩阵、父子变换；最近点、重心坐标与 Bezier |
| [StateAndDataExamples.cs](StateAndDataExamples.cs) | 固定种子、随机状态恢复、采样和洗牌；60 份精确累计；带版本头的内存编码；数据恢复和校验 |
| [MathExampleRunner.cs](MathExampleRunner.cs) | 组件入口、统一调用，以及 Unity 显式转换边界 |
| [Editor/MathExampleMenu.cs](Editor/MathExampleMenu.cs) | 编辑器菜单入口；所在程序集只在 Editor 编译 |

主题类也能从自己的代码中单独调用：

```csharp
using AshFramework.Math.Example;
using UnityEngine;

ScalarExamples.Run(message => Debug.Log(message));
VectorAndTransformExamples.Run(message => Debug.Log(message));
StateAndDataExamples.Run(message => Debug.Log(message));
```

以上调用语句应放在自己的方法体中，例如 `Start()`。
数学核心不会引用示例程序集；示例依赖方向为 `Example → Math / Math.Unity`。

## 预期观察到的结果

- 三种方式构造的 `1.5` 相同；`1.5 + 2 = 3.5`，`1.5 × 2 = 3`。
- `-1.5` 向下为 `-2`，向上和向零为 `-1`，四舍五入为 `-2`。
- `(3,4)` 的长度为 `5`，归一化方向约为 `(0.6,0.8)`。
- 绕 Z 轴旋转 `90°`，Right 约等于 Up；逆变换后约等于原方向。
- 二维局部点 `(1,0)` 经示例父变换后约为 `(10,22)`。
- 最近点为 `(2,0)`，重心权重为 `(0.5,0.25,0.25)`，二次 Bezier 中点为 `(1,1)`。
- 随机状态恢复与内存二进制往返输出的一致性判断均为 `True`。
- 余数累计 60 次后精确得到 `1`，余数为 `0`。
- 最后输出“使用示例完成”标记。

三角函数、归一化和变换结果带有定点量化误差，示例中标记“约”的结果应按容差理解；精确文本可能显示较多小数位。
二进制恢复和随机序列恢复则比较原始值，要求完全相等。

完整数值格式、范围、异常规则与验证边界见上一级 [README](../README.md)。

## 本次验证

- 解决方案构建、Allman、指定范围空白和大括号检查通过。
- AshFramework Unity Editor 编译通过，已在该工程实际点击“运行全部示例”。
- 本轮输出 37 条日志，开始和完成标记齐全，无新增错误或警告。
- 构造结果、精确文本、随机恢复、余数累计、二进制往返与校验值的一致性检查符合预期。
- 零向量的 `TryNormalize` 按约定返回 `False`，属于示例展示的正常边界。
- `MathExampleRunner.Start()` 的 Play 生命周期入口已编译，本轮实际执行入口为编辑器菜单。

构建与格式记录：`.codex/runs/20261027T080221Z-9aa5afa0/report.md`。
实际运行日志与摘要：`.codex/runs/math-example-smoke/console.json`、`summary.json`。
