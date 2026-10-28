using System;
using AshFramework.Collide.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AshFramework.Collide.Samples.Editor
{
    /// <summary>通过 Editor API 创建和保存场景；已有场景只打开，不覆盖用户调整。</summary>
    public static class CollisionWorldOverviewSceneBuilder
    {
        public const string ScenePath = "Assets/AshFramework/Collide/Samples/Scenes/CollideWorldOverview.unity";

        [MenuItem("Tools/AshFramework/Collide/创建或打开世界接入说明场景")]
        public static void CreateOrOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("请退出 Play 后再创建或打开示例。");
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogWarning("当前场景有未保存修改，请先保存，再打开世界接入说明场景。");
                    return;
                }
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EditorSceneManager.OpenScene(ScenePath);
                FocusOverview();
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CollisionWorldOverview overview = new GameObject("00_说明与操作").AddComponent<CollisionWorldOverview>();
            CollisionWorldBehaviour world = new GameObject("01_World_XY_固定范围").AddComponent<CollisionWorldBehaviour>();
            world.Configure(CollisionPlane.XY, Vector2.zero, new Vector2(9, 4));
            Transform cases = new GameObject("02_Body示例").transform;

            CollisionBodyBehaviour a = CreateBody(cases, world, "01_正常复合体A", new Vector3(-5, 1, 0));
            a.GetComponent<CircleShapeBehaviour>().Radius = 0.8f;
            GameObject boxObject = new GameObject("Box_旋转20度");
            boxObject.transform.SetParent(a.transform, false);
            boxObject.transform.localPosition = new Vector3(1, -0.2f, 0);
            boxObject.transform.localRotation = Quaternion.Euler(0, 0, 20);
            boxObject.AddComponent<BoxShapeBehaviour>().HalfExtents = new Vector2(1, 0.45f);

            CollisionBodyBehaviour b = CreateBody(cases, world, "02_正常圆B", new Vector3(-3.5f, 1, 0));
            b.LogicalHeight = LogicalHeight.Level3;
            CollisionBodyBehaviour disabled = CreateBody(cases, world, "03_禁用Body", new Vector3(3, 1, 0));
            disabled.enabled = false;
            CollisionBodyBehaviour mismatch = CreateBody(cases, world, "04_禁用且平面不一致", new Vector3(-5, -2, 0));
            mismatch.Plane = CollisionPlane.XZ;
            mismatch.enabled = false;
            CollisionBodyBehaviour outside = CreateBody(cases, world, "05_越界配置", new Vector3(9, -2, 0));
            CollisionBodyBehaviour unbound = CreateBody(cases, null, "06_未绑定Body", new Vector3(1, -2, 0));

            overview.Configure(world, new[]
            {
                new CollisionWorldOverview.Entry(a, "01 正常复合体 A：圆 + 旋转矩形，显式绑定 World，逻辑高度 1。", new Vector3(-2, 2.5f, 0)),
                new CollisionWorldOverview.Entry(b, "02 正常圆 B：几何与 A 重叠，逻辑高度 3。当前高度不参与过滤；预览重叠不等于已经执行世界检测。", new Vector3(0, 1.5f, 0)),
                new CollisionWorldOverview.Entry(disabled, "03 禁用 Body：绑定仍保留。当前未初始化，没有核心 ID；注入索引后才可观察禁用保留身份。灰叉仅为位置标记。", new Vector3(-1.5f, 1.5f, 0)),
                new CollisionWorldOverview.Entry(mismatch, "04 禁用且平面不一致：Body 为 XZ，World 为 XY。选中 World 检查，预期 5 个绑定对象中发现这 1 个问题，禁用也包含在内。", new Vector3(-2, 1.3f, 0)),
                new CollisionWorldOverview.Entry(outside, "05 越界配置：圆心 X=9、半径 0.9，整体 AABB 超过世界右边界。几何可预览；注入索引并 Step 提交时才会被世界暂停。平面按钮只检查平面。", new Vector3(-2.5f, 1.3f, 0)),
                new CollisionWorldOverview.Entry(unbound, "06 未绑定 Body：有可预览几何，但不属于此 World，平面检查及世界检测不收集它。层级位置不会代替显式绑定。", new Vector3(-1.5f, 1.3f, 0))
            });

            Camera camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -20);
            camera.orthographic = true;
            camera.orthographicSize = 7;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
            camera.gameObject.AddComponent<AudioListener>();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException("Failed to save collision world overview scene.");
            }

            FocusOverview();
            Debug.Log("已通过 Editor 创建世界接入说明场景：" + ScenePath + "。未注入任何空间索引。", overview);
        }

        private static CollisionBodyBehaviour CreateBody(Transform parent, CollisionWorldBehaviour world,
            string name, Vector3 position)
        {
            GameObject item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.position = position;
            CollisionBodyBehaviour body = item.AddComponent<CollisionBodyBehaviour>();
            body.Plane = CollisionPlane.XY;
            item.AddComponent<CircleShapeBehaviour>().Radius = 0.9f;
            body.BindWorld(world);
            return body;
        }

        private static void FocusOverview()
        {
            CollisionWorldOverview[] overviews = UnityEngine.Object.FindObjectsOfType<CollisionWorldOverview>();
            foreach (CollisionWorldOverview overview in overviews)
            {
                if (overview.gameObject.scene.path == ScenePath)
                {
                    Selection.activeGameObject = overview.gameObject;
                    break;
                }
            }

            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.in2DMode = true;
                view.orthographic = true;
                view.rotation = Quaternion.identity;
                view.pivot = new Vector3(0, 0.5f, 0);
                view.size = 9;
                view.drawGizmos = true;
                view.Repaint();
            }
        }
    }
}
