using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AshFramework.UI.UIModule.Editor
{
    public static class RootUIFactory
    {
        [MenuItem("GameObject/AshFramework/UIModule/RootUI", false, 10)]
        private static void CreateFromMenu(MenuCommand command)
        {
            RootUI root = Create(command.context as GameObject);
            Selection.activeGameObject = root.gameObject;
        }

        public static RootUI Create(GameObject parent = null)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create AshFramework RootUI");

            try
            {
                GameObject rootObject = new GameObject("RootUI", typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(rootObject, "Create RootUI");
                UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(rootObject);
                GameObjectUtility.SetParentAndAlign(rootObject, parent);
                RootUI root = Undo.AddComponent<RootUI>(rootObject);
                Canvas main = CreateCanvas(rootObject.transform, "MainCanvas", 0);
                Canvas popup = CreateCanvas(rootObject.transform, "PopupCanvas", 100);
                Canvas overlay = CreateCanvas(rootObject.transform, "OverlayCanvas", 200);
                RectTransform staging = CreateRect(rootObject.transform, "StagingRoot");
                staging.gameObject.SetActive(false);
                root.Configure(main, popup, overlay, staging);

                // Operate only in the root's scene, including Prefab Stage isolation.
                int eventSystems = 0;

                foreach (GameObject sceneRoot in rootObject.scene.GetRootGameObjects())
                {
                    eventSystems += sceneRoot.GetComponentsInChildren<EventSystem>(true).Length;
                }

                if (eventSystems == 0 && !EditorUtility.IsPersistent(rootObject) &&
                    UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() == null)
                {
                    GameObject eventObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(eventObject, rootObject.scene);
                    Undo.RegisterCreatedObjectUndo(eventObject, "Create EventSystem");
                }
                else if (eventSystems > 1)
                {
                    Debug.LogWarning("Multiple EventSystems already exist in this scene. RootUI did not modify them.", root);
                }

                Undo.CollapseUndoOperations(group);
                return root;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }

        private static Canvas CreateCanvas(Transform parent, string name, int sortingOrder)
        {
            RectTransform rect = CreateRect(parent, name);
            Canvas canvas = Undo.AddComponent<Canvas>(rect.gameObject);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            var scaler = Undo.AddComponent<UnityEngine.UI.CanvasScaler>(rect.gameObject);
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            Undo.AddComponent<UnityEngine.UI.GraphicRaycaster>(rect.gameObject);
            return canvas;
        }

        private static RectTransform CreateRect(Transform parent, string name)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(gameObject, "Create " + name);
            gameObject.transform.SetParent(parent, false);
            var rect = (RectTransform)gameObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
