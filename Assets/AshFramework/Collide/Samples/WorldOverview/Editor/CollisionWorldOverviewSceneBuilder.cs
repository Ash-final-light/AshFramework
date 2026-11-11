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
        public const string CompoundContactsScenePath = "Assets/AshFramework/Collide/Samples/Scenes/CollideCompoundContacts.unity";
        private static readonly string[] Descriptions =
        {
            "01 正常复合体 A：圆 + 旋转矩形，显式绑定 World，逻辑高度 1。",
            "02 正常圆 B：与 A 重叠，高度 3 不参与过滤。初始化网格后 Step 应得到 A/B 一对；在 Play 移开 B 后再次 Step 应为零对。",
            "03 禁用 Body：初始化后也有 ID，禁用期间暂停，启用并 Step 后恢复且 ID 不变。灰叉只是位置标记。",
            "04 禁用且平面不一致：XZ Body 与 XY World 不一致。平面按钮仍会发现它；启用并 Step 后因平面错误暂停。",
            "05 越界配置：圆心 X=9、半径 0.9，整体范围越界。Step 暂停它并报告原因，其余有效对象继续检测。",
            "06 未绑定 Body：可预览几何，但没有此世界 ID，不参与此世界的检测及平面检查。"
        };

        [MenuItem("Tools/AshFramework/Collide/更新当前示例为网格检测说明")]
        public static void UpdateCurrentDescriptions()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath)
            {
                Debug.LogWarning("请在编辑状态打开 CollideWorldOverview 场景后再更新说明。");
                return;
            }

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!root.TryGetComponent(out CollisionWorldOverview overview) || overview.CaseCount != Descriptions.Length)
                {
                    continue;
                }

                Undo.RecordObject(overview, "Update grid example descriptions");
                CollisionWorldOverview.Entry[] entries = new CollisionWorldOverview.Entry[overview.CaseCount];
                for (int i = 0; i < entries.Length; i++)
                {
                    CollisionWorldOverview.Entry old = overview.GetCase(i);
                    entries[i] = new CollisionWorldOverview.Entry(old.Body, Descriptions[i], old.LabelOffset);
                }

                overview.Configure(overview.World, entries);
                Undo.FlushUndoRecordObjects();
                EditorUtility.SetDirty(overview);
                EditorSceneManager.MarkSceneDirty(overview.gameObject.scene);
                if (!EditorSceneManager.SaveScene(overview.gameObject.scene))
                {
                    throw new InvalidOperationException("Failed to save updated example descriptions.");
                }

                Selection.activeGameObject = overview.gameObject;
                return;
            }
        }

        [MenuItem("Tools/AshFramework/Collide/创建或打开世界接入说明场景")]
        public static void CreateOrOpen()
        {
            if (!CanOpenExample() || OpenExistingExample(ScenePath))
            {
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
                new CollisionWorldOverview.Entry(a, Descriptions[0], new Vector3(-2, 2.5f, 0)),
                new CollisionWorldOverview.Entry(b, Descriptions[1], new Vector3(0, 1.5f, 0)),
                new CollisionWorldOverview.Entry(disabled, Descriptions[2], new Vector3(-1.5f, 1.5f, 0)),
                new CollisionWorldOverview.Entry(mismatch, Descriptions[3], new Vector3(-2, 1.3f, 0)),
                new CollisionWorldOverview.Entry(outside, Descriptions[4], new Vector3(-2.5f, 1.3f, 0)),
                new CollisionWorldOverview.Entry(unbound, Descriptions[5], new Vector3(-1.5f, 1.3f, 0))
            });

            CreateCamera();
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException("Failed to save collision world overview scene.");
            }

            FocusOverview(ScenePath);
            Debug.Log("已通过 Editor 创建网格碰撞示例：" + ScenePath + "。进入 Play 注入网格，手动 Step 检测。", overview);
        }

        [MenuItem("Tools/AshFramework/Collide/创建或打开复合体接触示例")]
        public static void CreateOrOpenCompoundContacts()
        {
            if (!CanOpenExample() || OpenExistingExample(CompoundContactsScenePath))
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CollisionWorldOverview overview = new GameObject("00_说明与操作").AddComponent<CollisionWorldOverview>();
            CollisionWorldBehaviour world = new GameObject("01_World_XY_固定范围").AddComponent<CollisionWorldBehaviour>();
            world.Configure(CollisionPlane.XY, Vector2.zero, new Vector2(9, 4));
            Transform cases = new GameObject("02_Body示例").transform;

            CollisionBodyBehaviour wall = new GameObject("01_复合墙 A").AddComponent<CollisionBodyBehaviour>();
            wall.transform.SetParent(cases, false);
            wall.transform.position = new Vector3(3, 0, 0);
            wall.Plane = CollisionPlane.XY;
            CreateWallPart(wall.transform, "上半墙", 1);
            CreateWallPart(wall.transform, "下半墙", -1);
            wall.BindWorld(world);

            CollisionBodyBehaviour circle = CreateBody(cases, world, "02_圆 B", new Vector3(1.5f, 0, 0));
            circle.GetComponent<CircleShapeBehaviour>().Radius = 1;
            overview.Configure(world, new[]
            {
                new CollisionWorldOverview.Entry(wall, "复合墙 A：两个子矩形共用 x = 2 的左边界。", new Vector3(0.5f, 2.5f, 0)),
                new CollisionWorldOverview.Entry(circle, "圆 B：圆心 x = 1.5，半径 1。Step 后应得到 1 个 Body 对和 2 条深度 0.5 的接触。", new Vector3(-2, 1.6f, 0))
            });

            CreateCamera();
            if (!EditorSceneManager.SaveScene(scene, CompoundContactsScenePath))
            {
                throw new InvalidOperationException("Failed to save compound contacts scene.");
            }

            FocusOverview(CompoundContactsScenePath);
        }

        private static bool CanOpenExample()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("请退出 Play 后再创建或打开示例。");
                return false;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    Debug.LogWarning("当前场景有未保存修改，请先保存，再打开示例场景。");
                    return false;
                }
            }

            return true;
        }

        private static bool OpenExistingExample(string scenePath)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                return false;
            }

            EditorSceneManager.OpenScene(scenePath);
            FocusOverview(scenePath);
            return true;
        }

        private static void CreateWallPart(Transform parent, string name, float y)
        {
            GameObject part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = new Vector3(0, y, 0);
            part.AddComponent<BoxShapeBehaviour>().HalfExtents = Vector2.one;
        }

        private static void CreateCamera()
        {
            Camera camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -20);
            camera.orthographic = true;
            camera.orthographicSize = 7;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
            camera.gameObject.AddComponent<AudioListener>();
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

        private static void FocusOverview(string scenePath)
        {
            CollisionWorldOverview[] overviews = UnityEngine.Object.FindObjectsOfType<CollisionWorldOverview>();
            foreach (CollisionWorldOverview overview in overviews)
            {
                if (overview.gameObject.scene.path == scenePath)
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
