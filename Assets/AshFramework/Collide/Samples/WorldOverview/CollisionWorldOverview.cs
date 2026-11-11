using System;
using System.Collections.Generic;
using System.Globalization;
using AshFramework.Collide.Unity;
using AshFramework.Math.Unity;
using UnityEngine;

namespace AshFramework.Collide.Samples
{
    /// <summary>示例启动入口显式注入均匀网格；检测仍由按钮显式驱动。</summary>
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

        [SerializeField]
        [Min(0.01f)]
        private float _cellSize = 2;

        private readonly List<CollisionPair> _results = new List<CollisionPair>();
        private readonly List<string> _pairDescriptions = new List<string>();
        private readonly List<CollisionContact> _contacts = new List<CollisionContact>();
        private readonly List<Vector3?> _contactOrigins = new List<Vector3?>();
        private UniformGridBroadPhase2D _grid;
        private Vector3 _originalBPosition;
        private bool _hasOriginalBPosition;
        private bool _bSeparated;
        private Vector2 _scroll;
        private GUIStyle _textStyle;

        public CollisionWorldBehaviour World => _world;
        public int CaseCount => _entries.Length;
        public int PairCount => _results.Count;
        public int ContactCount => _contacts.Count;
        public int GridColumns => _grid == null ? 0 : _grid.Columns;
        public int GridRows => _grid == null ? 0 : _grid.Rows;
        public float GridCellSize => _grid == null ? 0 : _grid.CellSize.ToSingle();
        public bool LastStepSucceeded
        {
            get;
            private set;
        }

        public string IndexMessage
        {
            get;
            private set;
        } = "编辑状态尚未初始化；可手动注入网格，进入 Play 时由示例 Start 注入。";

        public string StepMessage
        {
            get;
            private set;
        } = "尚未调用 Step；场景轮廓是配置预览。";

        public string GeometryMessage
        {
            get;
            private set;
        } = "可手动刷新 Body 缓存；此操作不向世界提交。";

        public Entry GetCase(int index)
        {
            return _entries[index];
        }

        public void Configure(CollisionWorldBehaviour world, Entry[] entries)
        {
            _world = world != null ? world : throw new ArgumentNullException(nameof(world));
            _entries = entries != null ? (Entry[])entries.Clone() : throw new ArgumentNullException(nameof(entries));
        }

        public bool InitializeGrid()
        {
            if (_world == null)
            {
                IndexMessage = "缺少 World 引用。";
                return false;
            }

            if (_world.IsInitialized)
            {
                IndexMessage = "世界已经初始化，不替换已有索引。";
                return false;
            }

            try
            {
                Aabb2D coverage = new Aabb2D(_world.BoundsCenter.ToFixVector(), _world.BoundsHalfExtents.ToFixVector());
                UniformGridBroadPhase2D grid = new UniformGridBroadPhase2D(coverage, _cellSize.ToFix64());
                _world.Initialize(grid);
                _grid = grid;
                IndexMessage = "已显式注入均匀网格：" + grid.Columns + " 列 × " + grid.Rows + " 行，格子边长 " + _cellSize + "。";
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is OverflowException)
            {
                IndexMessage = "初始化失败：" + exception.Message;
                return false;
            }
        }

        private void Start()
        {
            if (_world != null && !_world.IsInitialized)
            {
                InitializeGrid();
            }
        }

        public CollisionPair GetPair(int index)
        {
            return _results[index];
        }

        public string GetPairDescription(int index)
        {
            return _pairDescriptions[index];
        }

        public CollisionContact GetContact(int index)
        {
            return _contacts[index];
        }

        public string GetContactDescription(int index)
        {
            CollisionContact contact = _contacts[index];
            return "ID " + contact.Pair.A + " 子形状 " + contact.Shapes.ShapeIndexA
                + " → ID " + contact.Pair.B + " 子形状 " + contact.Shapes.ShapeIndexB
                + " | 方向 " + contact.Shapes.Contact.Normal.ToUnityVector().ToString("F3")
                + " | " + FormatDepth(contact);
        }

        public bool TryGetContactGizmo(int index, out Vector3 origin, out Vector3 direction, out string label)
        {
            Vector3? position = _contactOrigins[index];
            if (!position.HasValue)
            {
                origin = default;
                direction = default;
                label = string.Empty;
                return false;
            }

            CollisionContact contact = _contacts[index];
            origin = position.Value;
            direction = OnWorldPlane(contact.Shapes.Contact.Normal.ToUnityVector());
            label = FormatDepth(contact);
            return true;
        }

        public CollisionBodyBehaviour FindBody(CollisionBodyId id)
        {
            if (_world != null)
            {
                foreach (Entry entry in _entries)
                {
                    if (entry.Body != null && _world.TryGetBodyId(entry.Body, out CollisionBodyId current) && current == id)
                    {
                        return entry.Body;
                    }
                }
            }

            return null;
        }

        public string GetBodyStatus(int index)
        {
            CollisionBodyBehaviour body = _entries[index].Body;
            if (body == null)
            {
                return "对象已销毁。";
            }

            if (body.World == null || body.World != _world)
            {
                return "未绑定此 World，无本世界 ID。";
            }

            if (!_world.TryGetBodyId(body, out CollisionBodyId id))
            {
                return "已配置绑定，尚无有效核心 ID。";
            }

            string state = _world.IsBodyActive(body) ? "参与检测" : "暂停 / 尚未提交";
            return "ID " + id + " | " + state + " | " + _world.GetBodyError(body);
        }

        public void ToggleExampleBPosition()
        {
            CollisionBodyBehaviour body = _entries[1].Body;
            if (!_hasOriginalBPosition)
            {
                _originalBPosition = body.transform.position;
                _hasOriginalBPosition = true;
            }

            _bSeparated = !_bSeparated;
            body.transform.position = _bSeparated ? _originalBPosition + Vector3.right * 6 : _originalBPosition;
        }

        public void ToggleExampleBEnabled()
        {
            CollisionBodyBehaviour body = _entries[1].Body;
            body.enabled = !body.enabled;
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
            _results.Clear();
            _pairDescriptions.Clear();
            _contacts.Clear();
            _contactOrigins.Clear();
            LastStepSucceeded = false;
            if (_world == null)
            {
                StepMessage = "缺少 World 引用。";
                return false;
            }

            bool success = _world.Step(_results, _contacts, out string error);
            LastStepSucceeded = success;
            StepMessage = success ? "本次 Step 成功，碰撞对 " + _results.Count + "，子形状接触 " + _contacts.Count
                : "本次 Step 失败：" + error;
            if (success)
            {
                foreach (CollisionPair pair in _results)
                {
                    CollisionBodyBehaviour a = FindBody(pair.A);
                    CollisionBodyBehaviour b = FindBody(pair.B);
                    _pairDescriptions.Add("ID " + pair.A + " " + (a == null ? "未列入示例" : a.name)
                        + " ↔ ID " + pair.B + " " + (b == null ? "未列入示例" : b.name));
                }

                foreach (CollisionContact contact in _contacts)
                {
                    _contactOrigins.Add(GetContactOrigin(contact));
                }
            }

            return success;
        }

        private Vector3? GetContactOrigin(CollisionContact contact)
        {
            CollisionBodyBehaviour a = FindBody(contact.Pair.A);
            CollisionBodyBehaviour b = FindBody(contact.Pair.B);
            if (a == null || b == null || !a.HasGeometry || !b.HasGeometry)
            {
                return null;
            }

            Vector2 centerA = a.Geometry[contact.Shapes.ShapeIndexA].GetBounds().Center.ToUnityVector();
            Vector2 centerB = b.Geometry[contact.Shapes.ShapeIndexB].GetBounds().Center.ToUnityVector();
            return OnWorldPlane((centerA + centerB) * 0.5f);
        }

        private Vector3 OnWorldPlane(Vector2 value)
        {
            return _world.Plane == CollisionPlane.XY ? new Vector3(value.x, value.y, 0) : new Vector3(value.x, 0, value.y);
        }

        private static string FormatDepth(CollisionContact contact)
        {
            return "深度 " + contact.Shapes.Contact.Depth.ToDecimal().ToString("G9", CultureInfo.InvariantCulture);
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
            GUILayout.Label("示例通过 IBroadPhase2D 注入均匀网格；仅在点击 Step 时检测。", _textStyle);
            GUILayout.Label("Scene 的红线显示 Body 配对，黄色箭头显示子形状接触方向与深度。", _textStyle);
            if (_world != null)
            {
                GUILayout.Label("World：" + _world.Plane + " | " + (_world.IsInitialized ? "已初始化" : "未初始化，无有效核心 ID"), _textStyle);
            }

            GUILayout.Label(IndexMessage, _textStyle);
            if (_world != null && !_world.IsInitialized && GUILayout.Button("显式初始化均匀网格"))
            {
                InitializeGrid();
            }

            if (GUILayout.Button("刷新示例几何（不提交世界）"))
            {
                RefreshExampleGeometry();
            }

            GUILayout.Label(GeometryMessage, _textStyle);
            if (GUILayout.Button("执行显式 Step"))
            {
                AttemptStep();
            }

            GUILayout.Label(StepMessage, _textStyle);
            GUILayout.Label("下面是上次成功 Step 的配对和接触。移动或启停后请再次 Step。", _textStyle);
            foreach (string description in _pairDescriptions)
            {
                GUILayout.Label(description, _textStyle);
            }

            for (int i = 0; i < ContactCount; i++)
            {
                GUILayout.Label(GetContactDescription(i), _textStyle);
            }

            if (GUILayout.Button("移开 / 还原 B（随后点击 Step）"))
            {
                ToggleExampleBPosition();
            }

            if (GUILayout.Button("禁用 / 启用 B（随后点击 Step）"))
            {
                ToggleExampleBEnabled();
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                GUILayout.Space(8);
                GUILayout.Label(_entries[i].Description, _textStyle);
                GUILayout.Label(GetBodyStatus(i), _textStyle);
            }

            GUILayout.Label("当前不自动 Step，不派发事件，不执行位置修正。", _textStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
