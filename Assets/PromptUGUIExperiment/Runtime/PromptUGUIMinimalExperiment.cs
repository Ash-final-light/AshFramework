#if UNITY_EDITOR
using System;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using PuiUI = PromptUGUI.Application.UI;

namespace AshFramework.PromptUGUIExperiment
{
    internal static class PromptUGUIMinimalExperiment
    {
        private const string SceneName = "SampleScene";
        private const string DocumentSource = "PromptUGUIMinimal.ui";
        private const string ScreenName = "PromptUGUIMinimal";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static async void Run()
        {
            if (SceneManager.GetActiveScene().name != SceneName) return;

            try
            {
                EnsureEventSystem();

                PuiUI.UseResourcesResolver("UI");
                await PuiUI.LoadDocumentAsync(DocumentSource);

                var screen = PuiUI.Open(ScreenName);
                var status = screen.Get<PromptUGUI.Controls.Text>("status");

                screen.Get<Btn>("verify").OnClick
                    .Subscribe(_ =>
                    {
                        status.TextValue = PuiUI.Tr("Button click works");
                        Debug.Log("[PromptUGUI Experiment] PASS: uGUI button and R3 binding work.");
                    })
                    .AddTo(screen);

                Debug.Log("[PromptUGUI Experiment] READY: XML loaded and uGUI screen opened.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;

            var eventSystem = new GameObject("PromptUGUI Experiment EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }
    }
}
#endif
