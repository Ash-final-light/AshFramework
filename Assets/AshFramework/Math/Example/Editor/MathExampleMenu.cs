using UnityEditor;
using UnityEngine;

namespace AshFramework.Math.Example.Editor
{
    /// <summary>提供无需创建场景物体即可运行的编辑器示例入口。</summary>
    public static class MathExampleMenu
    {
        [MenuItem("Tools/AshFramework/Math/运行全部示例")]
        public static void RunAll()
        {
            MathExampleRunner.RunAllExamples(Log);
        }

        [MenuItem("Tools/AshFramework/Math/定点数与标量")]
        public static void RunScalars()
        {
            ScalarExamples.Run(Log);
        }

        [MenuItem("Tools/AshFramework/Math/向量与变换")]
        public static void RunVectorsAndTransforms()
        {
            VectorAndTransformExamples.Run(Log);
        }

        [MenuItem("Tools/AshFramework/Math/随机与数据读写")]
        public static void RunStateAndData()
        {
            StateAndDataExamples.Run(Log);
        }

        private static void Log(string message)
        {
            Debug.Log(message);
        }
    }
}
