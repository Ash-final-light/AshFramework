using System;
using System.Collections.Generic;
using AshFramework.Collide.Unity;
using UnityEngine;

namespace AshFramework.Collide.Samples
{
    /// <summary>仅用于说明当前接入状态，不创建空间索引，不自动驱动世界。</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("AshFramework/Collide/Samples/World Overview")]
    public sealed class CollisionWorldOverview : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField]
            private CollisionBodyBehaviour _body;

            [SerializeField]
            [TextArea(2, 4)]
            private string _description;

            [SerializeField]
            private Vector3 _labelOffset;

            public CollisionBodyBehaviour Body => _body;
            public string Description => _description;
            public Vector3 LabelOffset => _labelOffset;

            public Entry(CollisionBodyBehaviour body, string description, Vector3 labelOffset)
            {
                _body = body;
                _description = description;
                _labelOffset = labelOffset;
            }
        }

        [SerializeField]
        private CollisionWorldBehaviour _world;

        [SerializeField]
        private Entry[] _entries = Array.Empty<Entry>();

        private readonly List<CollisionPair> _results = new List<CollisionPair>();
        private Vector2 _scroll;
        private GUIStyle _textStyle;

        public CollisionWorldBehaviour World => _world;
        public int CaseCount => _entries.Length;
        public string StepMessage { get; private set; } = "尚未调用 Step；场景轮廓是配置预览。";
        public string GeometryMessage { get; private set; } = "可手动刷新 Body 缓存；此操作不向世界提交。";

        public Entry GetCase(int index)
        {
            return _entries[index];
        }

        public void Configure(CollisionWorldBehaviour world, Entry[] entries)
        {
            _world = world != null ? world : throw new ArgumentNullException(nameof(world));
            _entries = entries != null ? (Entry[])entries.Clone() : throw new ArgumentNullException(nameof(entries));
        }

        public void RefreshExampleGeometry()
        {
            int prepared = 0;
            int disabled = 0;
            int failed = 0;
            foreach (Entry entry in _entries)
            {
                CollisionBodyBehaviour body = entry.Body;
                if (body == null)
                {
                    failed++;
                }
                else if (!body.isActiveAndEnabled)
                {
                    disabled++;
                }
                else
                {
                    body.RebuildShapes();
                    if (body.RefreshGeometry())
                    {
                        prepared++;
                    }
                    else
                    {
                        failed++;
                    }
                }
            }

            GeometryMessage = "几何准备成功 " + prepared + "，禁用 " + disabled + "，失败 " + failed
                + "。仅刷新源缓存，未提交世界、未检查覆盖范围、未执行检测。";
        }

        public bool AttemptStep()
        {
            if (_world == null)
            {
                StepMessage = "缺少 World 引用。";
                return false;
            }

            bool success = _world.Step(_results, out string error);
            StepMessage = success ? "本次 Step 成功，碰撞对数量：" + _results.Count : "本次 Step 失败：" + error;
            return success;
        }

        private void OnGUI()
        {
            if (_textStyle == null)
            {
                _textStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    fontSize = 15
                };
            }

            float width = Mathf.Max(120, Mathf.Min(860, Screen.width - 24));
            float height = Mathf.Max(100, Screen.height - 24);
            GUILayout.BeginArea(new Rect(12, 12, width, height), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("碰撞世界接入示例", _textStyle);
            GUILayout.Label("当前只接 IBroadPhase2D 接口。未提供具体索引，因此世界保持未初始化。", _textStyle);
            GUILayout.Label("在 Scene 视图开启 Gizmos 查看几何和覆盖范围；此面板不绘制碰撞结果。", _textStyle);
            if (_world != null)
            {
                GUILayout.Label("World：" + _world.Plane + " | " + (_world.IsInitialized ? "已初始化" : "未初始化，无有效核心 ID"), _textStyle);
            }

            if (GUILayout.Button("刷新示例几何（不提交世界）"))
            {
                RefreshExampleGeometry();
            }

            GUILayout.Label(GeometryMessage, _textStyle);
            if (GUILayout.Button("尝试显式 Step（初始配置预期失败）"))
            {
                AttemptStep();
            }

            GUILayout.Label(StepMessage, _textStyle);
            foreach (Entry entry in _entries)
            {
                GUILayout.Space(8);
                GUILayout.Label(entry.Description, _textStyle);
            }

            GUILayout.Label("未加入：具体空间算法、自动检测循环、碰撞事件、位置修正。", _textStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
