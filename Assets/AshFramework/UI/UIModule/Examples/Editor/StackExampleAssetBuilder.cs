using System;
using AshFramework.UI.UIModule.Editor;
using AshFramework.UI.UIModule.Examples.StackDemo;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AshFramework.UI.UIModule.Examples.Editor
{
    public static class StackExampleAssetBuilder
    {
        private const string TypographyRoot = StackExampleScaffolder.ExampleRoot + "/Typography";
        private const string OwnTypographyImport = "AshFramework.StackDemo.OwnTypographyImport";
        private static readonly Color Background = new Color32(16, 22, 34, 255);
        private static readonly Color Card = new Color32(33, 44, 62, 255);
        private static readonly Color Blue = new Color32(63, 133, 238, 255);
        private static readonly Color Coral = new Color32(222, 104, 83, 255);
        private static readonly Color Secondary = new Color32(183, 198, 219, 255);

        [MenuItem("Tools/AshFramework/UIModule/Examples/2. Create Scene And Prefabs")]
        public static void CreateAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Exit Play Mode before creating example assets.");
            }

            foreach (string path in new[] { StackExampleScaffolder.ScenePath, PrefabPath("StackFirst"), PrefabPath("StackSecond") })
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    throw new InvalidOperationException("Asset already exists; edit it in place instead of regenerating: " + path);
                }
            }

            TMP_FontAsset font = EnsureTypography();

            if (font == null)
            {
                Debug.Log("[StackDemo] TMP Essentials are importing. After import finishes, run step 2 again.");
                return;
            }

            StackExampleScaffolder.EnsureFolder(StackExampleScaffolder.ResourceFolder);
            StackExampleScaffolder.EnsureFolder(StackExampleScaffolder.ExampleRoot + "/Scenes");
            Scene previous = SceneManager.GetActiveScene();
            bool replaceCleanUntitled = string.IsNullOrEmpty(previous.path);

            if (replaceCleanUntitled && previous.isDirty)
            {
                throw new InvalidOperationException("Save the modified Untitled scene before creating the example.");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                replaceCleanUntitled ? NewSceneMode.Single : NewSceneMode.Additive);

            try
            {
                SceneManager.SetActiveScene(scene);
                var cameraObject = new GameObject("Main Camera", typeof(Camera));
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Background;
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                RootUI root = RootUIFactory.Create();
                CreatePanel<StackFirstView>("StackFirst", Blue, new Vector2(-70f, 65f), font);
                CreatePanel<StackSecondView>("StackSecond", Coral, new Vector2(55f, -35f), font);
                CreateDashboard(root, font);

                if (!EditorSceneManager.SaveScene(scene, StackExampleScaffolder.ScenePath))
                {
                    throw new InvalidOperationException("The example scene was not saved.");
                }
            }
            finally
            {
                if (!replaceCleanUntitled)
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(StackExampleScaffolder.ScenePath));
            Debug.Log("[StackDemo] Two wired prefabs and StackDemo.unity created. Use step 3 to open the scene.");
        }

        [MenuItem("Tools/AshFramework/UIModule/Examples/3. Open Stack Demo")]
        public static void OpenScene()
        {
            if (AssetDatabase.LoadMainAssetAtPath(StackExampleScaffolder.ScenePath) == null)
            {
                throw new InvalidOperationException("Create the example assets with step 2 first.");
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(StackExampleScaffolder.ScenePath, OpenSceneMode.Single);
            }
        }

        private static TMP_FontAsset EnsureTypography()
        {
            const string original = "Assets/TextMesh Pro";
            string imported = TypographyRoot + "/TextMesh Pro";
            string suffix = "/Resources/Fonts & Materials/LiberationSans SDF.asset";
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(imported + suffix);

            if (font != null)
            {
                SessionState.SetBool(OwnTypographyImport, false);
                return font;
            }

            // Reuse existing project typography without moving or replacing user-owned assets.
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(original + suffix);

            if (font != null)
            {
                if (SessionState.GetBool(OwnTypographyImport, false))
                {
                    StackExampleScaffolder.EnsureFolder(TypographyRoot);
                    string moveError = AssetDatabase.MoveAsset(original, imported);

                    if (!string.IsNullOrEmpty(moveError))
                    {
                        throw new InvalidOperationException(moveError);
                    }

                    SessionState.SetBool(OwnTypographyImport, false);
                }

                return font;
            }

            if (AssetDatabase.IsValidFolder(original))
            {
                throw new InvalidOperationException("Existing TMP resources need a valid font; they were not modified.");
            }

            // ImportResources returns before the package's assets are registered by AssetDatabase.
            SessionState.SetBool(OwnTypographyImport, true);
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            return null;
        }

        private static string PrefabPath(string name)
        {
            return StackExampleScaffolder.ResourceFolder + "/" + name + ".prefab";
        }

        private static void CreatePanel<TView>(string name, Color accent, Vector2 offset, TMP_FontAsset font)
            where TView : StackPanelView
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));

            try
            {
                var rect = (RectTransform)panel.transform;
                rect.anchorMin = new Vector2(0.72f, 0.52f);
                rect.anchorMax = rect.anchorMin;
                rect.sizeDelta = new Vector2(690f, 510f);
                rect.anchoredPosition = offset;
                panel.GetComponent<UnityEngine.UI.Image>().color = Card;
                RectTransform header = FixedRect("Header", rect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    Vector2.zero, new Vector2(690f, 100f));
                AddImage(header, accent, false);
                TMP_Text title = Label("Title", rect, font, 42f, Color.white, new Vector2(32f, -24f), new Vector2(626f, 58f));
                TMP_Text message = Label("Message", rect, font, 26f, Secondary, new Vector2(32f, -130f), new Vector2(626f, 250f));
                title.text = name == "StackFirst" ? "WINDOW A" : "WINDOW B";
                message.text = "Loaded on demand through Resources.\nRuntime arguments are rendered here.";
                UnityEngine.UI.Button hide = Button("HideButton", rect, font, "Hide (reuse)",
                    new Vector2(32f, 30f), new Vector2(292f, 60f), Card + new Color(0.04f, 0.04f, 0.04f, 0f));
                UnityEngine.UI.Button close = Button("CloseButton", rect, font, "Close (release)",
                    new Vector2(366f, 30f), new Vector2(292f, 60f), accent);
                TView view = panel.AddComponent<TView>();
                view.Configure(title, message, hide, close);
                PrefabUtility.SaveAsPrefabAsset(panel, PrefabPath(name));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(panel);
            }
        }

        private static void CreateDashboard(RootUI root, TMP_FontAsset font)
        {
            var main = (RectTransform)root.MainCanvas.transform;
            RectTransform background = StretchRect("Background", main);
            AddImage(background, Background, false);
            TMP_Text title = Label("Title", main, font, 52f, Color.white, new Vector2(72f, -60f), new Vector2(520f, 70f));
            title.text = "UI STACK";
            TMP_Text subtitle = Label("Subtitle", main, font, 24f, Secondary, new Vector2(76f, -146f), new Vector2(500f, 70f));
            subtitle.text = "Two windows. One Popup layer.\nLater Show requests move to the top.";
            RectTransform controls = FixedRect("Controls", main, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(76f, -260f), new Vector2(480f, 350f));
            var layout = controls.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            UnityEngine.UI.Button showA = ControlButton("ShowA", controls, font, "Show A / bring to top", Blue);
            UnityEngine.UI.Button showB = ControlButton("ShowB", controls, font, "Show B / bring to top", Coral);
            UnityEngine.UI.Button hide = ControlButton("HideTop", controls, font, "Hide top / keep instance", Card);
            UnityEngine.UI.Button close = ControlButton("CloseTop", controls, font, "Close top / release", Card);
            UnityEngine.UI.Button reset = ControlButton("Reset", controls, font, "Reset / open A then B", Card);
            TMP_Text status = Label("Status", main, font, 25f, Secondary, new Vector2(76f, -670f), new Vector2(530f, 300f));
            status.text = "Enter Play Mode to load the windows.";
            TMP_Text footer = Label("Footer", main, font, 24f, Secondary, new Vector2(740f, -965f), new Vector2(1100f, 70f));
            footer.text = "Hide reuses the instance. Close destroys it.\nThe lower window stays visible; this example does not add navigation or modal rules.";
            var controller = root.gameObject.AddComponent<StackExampleController>();
            controller.Configure(root, status, showA, showB, hide, close, reset);
        }

        private static UnityEngine.UI.Button ControlButton(string name, RectTransform parent,
            TMP_FontAsset font, string text, Color color)
        {
            UnityEngine.UI.Button button = Button(name, parent, font, text, Vector2.zero, new Vector2(480f, 58f), color);
            var element = button.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.preferredHeight = 58f;
            return button;
        }

        private static UnityEngine.UI.Button Button(string name, RectTransform parent, TMP_FontAsset font,
            string text, Vector2 position, Vector2 size, Color color)
        {
            RectTransform rect = FixedRect(name, parent, Vector2.zero, Vector2.zero, position, size);
            UnityEngine.UI.Image image = AddImage(rect, color, true);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            UnityEngine.UI.ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.16f, 1.16f, 1.16f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            RectTransform labelRect = StretchRect("Label", rect);
            var label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = 25f;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.text = text;
            return button;
        }

        private static TMP_Text Label(string name, RectTransform parent, TMP_FontAsset font,
            float size, Color color, Vector2 position, Vector2 bounds)
        {
            RectTransform rect = FixedRect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), position, bounds);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.enableAutoSizing = false;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;
            return label;
        }

        private static UnityEngine.UI.Image AddImage(RectTransform rect, Color color, bool raycast)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static RectTransform FixedRect(string name, Transform parent, Vector2 anchor,
            Vector2 pivot, Vector2 position, Vector2 size)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static RectTransform StretchRect(string name, Transform parent)
        {
            RectTransform rect = FixedRect(name, parent, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
