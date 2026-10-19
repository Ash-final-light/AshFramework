using System;
using System.Collections.Generic;
using System.Globalization;
using AshACT.Attributes;
using AshACT.Effects;
using Framework.FixedPoint;
using UnityEditor;
using UnityEngine;

namespace AshACT.Unity.Editor
{
    public sealed class AttributeDebugWindow : EditorWindow
    {
        private AttributeSessionSetup m_Setup = new AttributeSessionSetup();
        [SerializeField] private AttributeRuntimeHost m_Host;
        private bool m_ShowSetup = true;
        private Vector2 m_SetupScroll;
        private AttributeSession m_Preview;
        private Vector2 m_Scroll;
        private IReadOnlyList<ConfigurationIssue> m_Issues = Array.Empty<ConfigurationIssue>();
        private string m_Message = "初始化后使用整数帧按钮驱动真实运行时。";
        private string m_Source = "1";
        private string m_Target = "2";
        private string m_Effect = "1001";
        private readonly Dictionary<uint, string> m_Parameters = new Dictionary<uint, string>();
        private readonly List<EffectParameter> m_ParameterValues = new List<EffectParameter>();
        private AttributeSession m_SampledSession;
        private bool m_AutoRefresh;
        private bool m_RefreshRequested;
        private double m_NextRefreshTime;
        public DebugStatePresentation Presentation { get; private set; }
        public int SnapshotRefreshCount { get; private set; }
        public int LastDrawnRowCount { get; private set; }

        public AttributeSession Session => m_Host != null ? m_Host.Session : m_Preview;
        public AttributeSessionSetup PreviewSetup => m_Setup;

        [MenuItem("AshACT/运行调试")]
        public static void ShowWindow()
        {
            AttributeDebugWindow window = Open();
            if (Selection.activeObject is AttributeCatalogAsset catalog)
            {
                window.m_Setup.Catalog = catalog;
            }
        }

        public static AttributeDebugWindow Open(AttributeSessionSetup setup = null)
        {
            AttributeDebugWindow window = GetWindow<AttributeDebugWindow>("AshACT 运行调试");
            window.minSize = new Vector2(960, 650);
            if (setup != null)
            {
                window.m_Setup = setup;
                window.m_Host = null;
                window.m_ShowSetup = true;
            }

            window.Show();
            return window;
        }

        public static AttributeDebugWindow OpenPreview(AttributeCatalogAsset catalog, EffectGraphAsset effect,
            CharacterAttributeAsset source = null, CharacterAttributeAsset target = null)
        {
            List<MarkerConfiguration> markers = new List<MarkerConfiguration>();
            HashSet<uint> ids = new HashSet<uint>();
            foreach (uint id in effect?.MarkerIds ?? Array.Empty<uint>())
            {
                if (ids.Add(id))
                {
                    markers.Add(new MarkerConfiguration { MarkerId = id, DisplayName = "预览标记" });
                }
            }

            return Open(new AttributeSessionSetup
            {
                Catalog = catalog,
                Effects = effect == null ? Array.Empty<EffectGraphAsset>() : new[] { effect },
                Markers = markers.ToArray(),
                Entities = new[]
                {
                    new EntityConfiguration { EntityId = 1, Profile = source },
                    new EntityConfiguration { EntityId = 2, Profile = target != null ? target : source }
                }
            });
        }

        public bool InitializePreview(AttributeSessionSetup setup)
        {
            m_Setup = setup ?? new AttributeSessionSetup();
            if (!ConfigurationCompiler.TryCompileSession(setup, out CompiledSessionConfiguration compiled, out m_Issues))
            {
                m_Message = "编译失败，未发布新会话。";
                m_ShowSetup = true;
                return false;
            }

            m_Preview = new AttributeSession(compiled);
            m_ShowSetup = false;
            m_RefreshRequested = true;
            m_Message = $"已锁定发布版本 {compiled.ReleaseVersion}。资产编辑在下一次初始化生效。";
            return true;
        }

        private void OnInspectorUpdate()
        {
            if (m_AutoRefresh && Session != null && EditorApplication.timeSinceStartup >= m_NextRefreshTime)
            {
                RefreshState();
                Repaint();
            }
        }

        public void RefreshState()
        {
            m_SampledSession = Session;
            Presentation = m_SampledSession == null ? null : new DebugStatePresentation(m_SampledSession.Runtime.CaptureState());
            SnapshotRefreshCount++;
            m_RefreshRequested = false;
            m_NextRefreshTime = EditorApplication.timeSinceStartup + 0.5;
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                m_Host = (AttributeRuntimeHost)EditorGUILayout.ObjectField("运行 Host（可选）", m_Host, typeof(AttributeRuntimeHost), true);
                if (GUILayout.Button("校验 / 编译", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    bool valid = ConfigurationCompiler.TryCompileSession(m_Host != null ? m_Host.Setup : m_Setup, out CompiledSessionConfiguration compiled, out m_Issues);
                    m_Message = valid ? $"编译成功，发布 {compiled.ReleaseVersion}；当前会话仍保持原版本。" : "编译失败，查看定位列表。";
                }

                if (GUILayout.Button("初始化新会话", EditorStyles.toolbarButton, GUILayout.Width(110)))
                {
                    m_RefreshRequested = true;
                    if (m_Host != null)
                    {
                        m_Message = m_Host.TryInitialize(out m_Issues) ? "已初始化所选 Host。" : "Host 配置编译失败，原会话保留。";
                    }
                    else
                    {
                        InitializePreview(m_Setup);
                    }
                }
            }

            if (m_Host == null)
            {
                DrawPreviewSetup();
            }

            EditorGUILayout.HelpBox(m_Message, MessageType.Info);
            foreach (ConfigurationIssue issue in m_Issues)
            {
                if (GUILayout.Button(issue.ToString(), EditorStyles.wordWrappedLabel))
                {
                    Selection.activeObject = issue.Asset;
                    EditorGUIUtility.PingObject(issue.Asset);
                    if (issue.Asset is EffectGraphAsset graph)
                    {
                        EffectGraphWindow.Open(graph, m_Host != null ? m_Host.Setup?.Catalog : m_Setup.Catalog);
                    }
                }
            }

            AttributeSession session = Session;
            if (session != null)
            {
                DrawCommands(session);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("刷新状态", GUILayout.Width(100)))
                    {
                        m_RefreshRequested = true;
                    }

                    m_AutoRefresh = EditorGUILayout.ToggleLeft("自动刷新（2 Hz）", m_AutoRefresh, GUILayout.Width(180));
                    GUILayout.Label("操作后立即采样；默认手动刷新，滚动和重绘只读取缓存。");
                }

                if (m_SampledSession != session || m_RefreshRequested)
                {
                    RefreshState();
                }

                DrawCachedState();
            }
        }

        private void DrawPreviewSetup()
        {
            m_ShowSetup = EditorGUILayout.Foldout(m_ShowSetup, "临时预览配置（不保存为资产）", true);
            if (!m_ShowSetup)
            {
                return;
            }

            EditorGUILayout.HelpBox("在此临时选择属性库、效果和实体。运行方使用普通 C# 对象组装同样的数据；程序集重载后需重新填写。", MessageType.None);
            using (EditorGUILayout.ScrollViewScope scroll = new EditorGUILayout.ScrollViewScope(m_SetupScroll,
                GUILayout.Height(Mathf.Min(300, position.height * 0.4f))))
            {
                m_SetupScroll = scroll.scrollPosition;
                m_Setup.Catalog = (AttributeCatalogAsset)EditorGUILayout.ObjectField("通用属性库", m_Setup.Catalog, typeof(AttributeCatalogAsset), false);
                DrawPreviewEffects();
                DrawPreviewEntities();
                DrawPreviewMarkers();
            }
        }

        private void DrawPreviewEffects()
        {
            EditorGUILayout.LabelField("效果清单", EditorStyles.boldLabel);
            EffectGraphAsset[] effects = m_Setup.Effects;
            for (int i = 0; effects != null && i < effects.Length; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    effects[i] = (EffectGraphAsset)EditorGUILayout.ObjectField(effects[i], typeof(EffectGraphAsset), false);
                    if (GUILayout.Button("移除", GUILayout.Width(50)))
                    {
                        m_Setup.Effects = RemoveAt(effects, i);
                        break;
                    }
                }
            }

            if (GUILayout.Button("添加预览效果"))
            {
                m_Setup.Effects = Append(m_Setup.Effects, (EffectGraphAsset)null);
            }
        }

        private void DrawPreviewEntities()
        {
            EditorGUILayout.LabelField("实体编号 / 角色属性配置", EditorStyles.boldLabel);
            EntityConfiguration[] entities = m_Setup.Entities;
            for (int i = 0; entities != null && i < entities.Length; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EntityConfiguration entity = entities[i];
                    if (entity != null)
                    {
                        string text = EditorGUILayout.DelayedTextField(entity.EntityId.ToString(CultureInfo.InvariantCulture), GUILayout.Width(130));
                        if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id))
                        {
                            entity.EntityId = id;
                        }
                        else
                        {
                            m_Message = "实体编号必须是非负整数，且不能超过 ulong 的范围。";
                        }

                        entity.Profile = (CharacterAttributeAsset)EditorGUILayout.ObjectField(entity.Profile, typeof(CharacterAttributeAsset), false);
                    }
                    else
                    {
                        GUILayout.Label("空实体配置");
                    }

                    if (GUILayout.Button("移除", GUILayout.Width(50)))
                    {
                        m_Setup.Entities = RemoveAt(entities, i);
                        break;
                    }
                }
            }

            if (GUILayout.Button("添加预览实体"))
            {
                m_Setup.Entities = Append(m_Setup.Entities, new EntityConfiguration());
            }
        }

        private void DrawPreviewMarkers()
        {
            EditorGUILayout.LabelField("标记编号 / 显示名", EditorStyles.boldLabel);
            MarkerConfiguration[] markers = m_Setup.Markers;
            for (int i = 0; markers != null && i < markers.Length; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    MarkerConfiguration marker = markers[i];
                    if (marker != null)
                    {
                        long id = EditorGUILayout.LongField(marker.MarkerId, GUILayout.Width(130));
                        if (id >= 0 && id <= uint.MaxValue)
                        {
                            marker.MarkerId = (uint)id;
                        }
                        else
                        {
                            m_Message = "标记编号必须在 uint 的范围内。";
                        }

                        marker.DisplayName = EditorGUILayout.TextField(marker.DisplayName);
                    }
                    else
                    {
                        GUILayout.Label("空标记配置");
                    }

                    if (GUILayout.Button("移除", GUILayout.Width(50)))
                    {
                        m_Setup.Markers = RemoveAt(markers, i);
                        break;
                    }
                }
            }

            if (GUILayout.Button("添加预览标记"))
            {
                m_Setup.Markers = Append(m_Setup.Markers, new MarkerConfiguration());
            }
        }

        private static T[] Append<T>(T[] entries, T entry)
        {
            List<T> list = new List<T>(entries ?? Array.Empty<T>());
            list.Add(entry);
            return list.ToArray();
        }

        private static T[] RemoveAt<T>(T[] entries, int index)
        {
            List<T> list = new List<T>(entries);
            list.RemoveAt(index);
            return list.ToArray();
        }

        private void DrawCommands(AttributeSession session)
        {
            EditorGUILayout.LabelField($"锁定 Schema {session.Configuration.SchemaVersion} / Release {session.Configuration.ReleaseVersion}", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("开始下一整数帧"))
                {
                    m_RefreshRequested = true;
                    m_Message = AttributeSession.Describe(session.BeginFrame(session.Runtime.CurrentFrame + 1));
                }

                if (GUILayout.Button("结束本帧 / 周期结算"))
                {
                    m_RefreshRequested = true;
                    m_Message = AttributeSession.Describe(session.EndFrame());
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                m_Source = EditorGUILayout.TextField("来源 EntityId", m_Source);
                m_Target = EditorGUILayout.TextField("目标 EntityId", m_Target);
                m_Effect = EditorGUILayout.TextField("EffectDefinitionId", m_Effect);
            }

            List<EffectParameter> parameters = m_ParameterValues;
            parameters.Clear();
            bool valid = ulong.TryParse(m_Source, NumberStyles.None, CultureInfo.InvariantCulture, out ulong source) &&
                ulong.TryParse(m_Target, NumberStyles.None, CultureInfo.InvariantCulture, out ulong target);
            ulong.TryParse(m_Target, NumberStyles.None, CultureInfo.InvariantCulture, out target);
            uint.TryParse(m_Effect, NumberStyles.None, CultureInfo.InvariantCulture, out uint definitionId);
            if (session.Configuration.Effects.TryGetValue(definitionId, out CompiledEffect effect))
            {
                foreach (uint id in effect.RequiredParameters)
                {
                    if (!m_Parameters.TryGetValue(id, out string text))
                    {
                        text = "1.0000";
                    }

                    text = EditorGUILayout.TextField($"必填参数 {id}", text);
                    m_Parameters[id] = text;
                    bool parsed = DecimalValue.TryParse(text, out FP64 value);
                    valid &= parsed;
                    parameters.Add(new EffectParameter(id, value));
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("施加锁定配置"))
                {
                    m_RefreshRequested = true;
                    if (valid)
                    {
                        m_Message = session.TryApply(definitionId, source, target, parameters, out string error) ? "效果已原子提交。" : error;
                    }
                    else
                    {
                        m_Message = "实体编号或四位小数参数无效。";
                    }
                }

                if (GUILayout.Button("来源离场 / 冻结"))
                {
                    m_RefreshRequested = true;
                    m_Message = AttributeSession.Describe(session.Deactivate(source));
                }

                if (GUILayout.Button("来源复活"))
                {
                    m_RefreshRequested = true;
                    m_Message = AttributeSession.Describe(session.Reactivate(source));
                }
            }
        }

        private void DrawCachedState()
        {
            LastDrawnRowCount = 0;
            if (Presentation == null)
            {
                return;
            }

            const float rowHeight = 21;
            Rect viewport = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            float width = Mathf.Max(viewport.width - 18, Presentation.MaximumLineLength * 10);
            Rect content = new Rect(0, 0, width, Presentation.Lines.Count * rowHeight);
            m_Scroll = GUI.BeginScrollView(viewport, m_Scroll, content);
            int first = Mathf.Max(0, (int)(m_Scroll.y / rowHeight));
            int end = Mathf.Min(Presentation.Lines.Count, first + Mathf.CeilToInt(viewport.height / rowHeight) + 1);
            for (int i = first; i < end; i++)
            {
                GUI.Label(new Rect(6, i * rowHeight, width - 12, rowHeight), Presentation.Lines[i]);
                LastDrawnRowCount++;
            }

            GUI.EndScrollView();
        }
    }
}
