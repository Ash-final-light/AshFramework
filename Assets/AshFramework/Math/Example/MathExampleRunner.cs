using System;
using AshFramework.Math.Unity;
using UnityEngine;

namespace AshFramework.Math.Example
{
    /// <summary>
    /// 挂到任意 GameObject 后，可在进入 Play 时输出示例结果。
    /// 也可通过组件右键菜单运行指定主题，不修改场景对象的变换。
    /// </summary>
    [AddComponentMenu("AshFramework/Math/数学库使用示例")]
    public sealed class MathExampleRunner : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("进入 Play 后自动运行全部示例，结果输出到 Console。")]
        private bool runOnStart = true;

        private void Start()
        {
            if (runOnStart)
            {
                RunAll();
            }
        }

        [ContextMenu("运行全部数学示例")]
        public void RunAll()
        {
            RunAllExamples(Log);
        }

        [ContextMenu("运行定点数与标量示例")]
        public void RunScalars()
        {
            ScalarExamples.Run(Log);
        }

        [ContextMenu("运行向量与变换示例")]
        public void RunVectorsAndTransforms()
        {
            VectorAndTransformExamples.Run(Log);
        }

        [ContextMenu("运行随机与数据示例")]
        public void RunStateAndData()
        {
            StateAndDataExamples.Run(Log);
        }

        public static void RunAllExamples(Action<string> log)
        {
            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            log("========== AshFramework.Math 使用示例开始 ==========");
            ScalarExamples.Run(log);
            VectorAndTransformExamples.Run(log);
            StateAndDataExamples.Run(log);
            RunUnityConversions(log);
            log("========== AshFramework.Math 使用示例完成 ==========");
        }

        public static void RunUnityConversions(Action<string> log)
        {
            if (log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            log("【Unity 显式转换边界】");

            // 逻辑结果转换为 Unity 类型，用于表现；此处仅输出，不移动场景物体。
            FixVector3 logicalPosition = new FixVector3(Fix64.FromRatio(3, 2), 2, 3);
            Vector3 displayPosition = logicalPosition.ToUnityVector();
            Quaternion displayRotation = FixQuaternion.Identity.ToUnityQuaternion();
            log($"逻辑位置转 Unity Vector3：{displayPosition}；单位旋转转 Unity Quaternion：{displayRotation}");

            // 示例值 1.5 和 -2.25 可被浮点数精确表示，便于观察转换结果。
            // 实际输入应在统一边界量化后分发原始值，不能依赖各端独立浮点模拟。
            Vector2 input = new Vector2(1.5f, -2.25f);
            FixVector2 quantized = input.ToFixVector();
            log($"Unity 输入量化为定点向量：{quantized}；原始值=({quantized.X.RawValue},{quantized.Y.RawValue})");
        }

        private void Log(string message)
        {
            Debug.Log(message, this);
        }
    }
}
