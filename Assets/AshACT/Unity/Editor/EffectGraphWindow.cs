using System;
using System.Collections.Generic;
using AshACT.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshACT.Unity.Editor
{
    public sealed class EffectGraphWindow : EditorWindow
    {
        [SerializeField] private EffectGraphAsset m_Graph;
        [SerializeField] private AttributeCatalogAsset m_Catalog;
        [SerializeField] private CharacterAttributeAsset m_SourcePreview;
        [SerializeField] private CharacterAttributeAsset m_TargetPreview;
        private EffectAttributeOptions m_SourceOptions;
        private EffectAttributeOptions m_TargetOptions;
        private Vector2 m_InspectorScroll;
        private EffectValueKind m_AddKind;
        private int m_Selected;
        private int m_ModifierIndex;
        private SerializedObject m_Serialized;
        private EffectGraphView m_View;
        private bool m_RefreshScheduled;
        private string[] m_NodeChoices = Array.Empty<string>();
        private string[] m_ModifierChoices = Array.Empty<string>();
        private string m_TimelineHeader;
        private string m_TimelineTicks;
        private string m_Validation = "从输出 port 按住拖到输入 port 后松开建立连接；选中连线按 Delete 删除。";

        public EffectGraphView Graph => m_View;

        public void SetPreviewContext(CharacterAttributeAsset source, CharacterAttributeAsset target = null)
        {
            m_SourcePreview = source;
            m_TargetPreview = target;
            Invalidate();
        }

        [MenuItem("AshACT/效果节点编辑器")]
        public static void ShowWindow()
        {
            Open(Selection.activeObject as EffectGraphAsset);
        }

        public static EffectGraphWindow Open(EffectGraphAsset graph, AttributeCatalogAsset catalog = null)
        {
            EffectGraphWindow window = GetWindow<EffectGraphWindow>("AshACT 效果图");
            window.minSize = new Vector2(1000, 650);
            window.m_Graph = graph;
            if (catalog != null)
            {
                window.m_Catalog = catalog;
            }

            window.Invalidate();
            window.Show();
            window.RefreshGraph();
            return window;
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += Invalidate;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Invalidate;
            m_Serialized?.Dispose();
            m_Serialized = null;
        }

        private void OnFocus()
        {
            Invalidate();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            IMGUIContainer toolbar = new IMGUIContainer(DrawToolbar);
            toolbar.style.height = 50;
            rootVisualElement.Add(toolbar);
            VisualElement body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.flexDirection = FlexDirection.Row;
            rootVisualElement.Add(body);
            m_View = new EffectGraphView(Record, Select, Changed);
            body.Add(m_View);
            IMGUIContainer inspector = new IMGUIContainer(DrawPanel);
            inspector.style.width = Length.Percent(38);
            inspector.style.minWidth = 320;
            body.Add(inspector);
            IMGUIContainer footer = new IMGUIContainer(DrawFooter);
            footer.style.height = 100;
            rootVisualElement.Add(footer);
            RefreshGraph();
        }

        private void Select(int nodeId, int modifierIndex)
        {
            if (modifierIndex >= 0)
            {
                m_ModifierIndex = modifierIndex;
            }
            else
            {
                m_Selected = nodeId;
            }

            Repaint();
        }

        private void Changed()
        {
            m_Validation = "配置已修改，请重新校验 / 编译。";
            Invalidate();
        }

        private void Invalidate()
        {
            if (m_RefreshScheduled)
            {
                return;
            }

            m_RefreshScheduled = true;
            rootVisualElement.schedule.Execute(() =>
            {
                if (m_RefreshScheduled)
                {
                    RefreshGraph();
                }
            });
        }

        private void RefreshGraph()
        {
            m_RefreshScheduled = false;
            if (m_View == null)
            {
                return;
            }

            m_Serialized?.Dispose();
            m_Serialized = m_Graph == null ? null : new SerializedObject(m_Graph);
            m_SourceOptions = new EffectAttributeOptions(m_Catalog, m_SourcePreview, true);
            m_TargetOptions = new EffectAttributeOptions(m_Catalog, m_TargetPreview, false);
            EffectValueNode[] nodes = m_Graph?.Nodes ?? Array.Empty<EffectValueNode>();
            m_NodeChoices = new string[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                m_NodeChoices[i] = nodes[i] == null ? "空节点" : $"#{nodes[i].NodeId} {nodes[i].Kind}";
            }

            m_ModifierChoices = new string[m_Graph?.Modifiers?.Length ?? 0];
            for (int i = 0; i < m_ModifierChoices.Length; i++)
            {
                m_ModifierChoices[i] = $"修改器 {i}";
            }

            m_View.Load(m_Graph);
            if (m_Graph != null)
            {
                BuildTimeline();
            }

            Repaint();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EffectGraphAsset graph = (EffectGraphAsset)EditorGUILayout.ObjectField(m_Graph, typeof(EffectGraphAsset), false);
                if (graph != m_Graph)
                {
                    m_Graph = graph;
                    Invalidate();
                }

                AttributeCatalogAsset catalog = (AttributeCatalogAsset)EditorGUILayout.ObjectField(m_Catalog, typeof(AttributeCatalogAsset), false);
                if (catalog != m_Catalog)
                {
                    m_Catalog = catalog;
                    Invalidate();
                }
                if (GUILayout.Button("校验 / 编译", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    ValidateCatalog();
                }

                if (GUILayout.Button("运行调试", EditorStyles.toolbarButton, GUILayout.Width(80)))
                {
                    AttributeDebugWindow.OpenPreview(m_Catalog, m_Graph, m_SourcePreview, m_TargetPreview);
                }

                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(50)))
                {
                    if (m_Graph != null)
                    {
                        AssetDatabase.SaveAssetIfDirty(m_Graph);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                m_AddKind = (EffectValueKind)EditorGUILayout.EnumPopup("新增数值节点", m_AddKind, GUILayout.Width(340));
                if (GUILayout.Button("添加", GUILayout.Width(65)))
                {
                    AddNode(m_AddKind);
                }

                if (GUILayout.Button("添加修改器", GUILayout.Width(100)) && m_Graph != null)
                {
                    Record();
                    List<EffectModifierNode> modifiers = new List<EffectModifierNode>(m_Graph.Modifiers ?? Array.Empty<EffectModifierNode>());
                    modifiers.Add(new EffectModifierNode());
                    m_Graph.Modifiers = modifiers.ToArray();
                }

                if (GUILayout.Button("适配画布", GUILayout.Width(85)))
                {
                    m_View?.FrameAll();
                }

                GUILayout.Label("拖拽 port 连线 / 滚轮缩放 / 中键平移");
            }
        }

        private void DrawPanel()
        {
            if (m_Graph == null || m_Serialized == null || m_Serialized.targetObject != m_Graph)
            {
                EditorGUILayout.HelpBox("选择效果图与目录后，从 port 拖拽连线。", MessageType.Info);
                return;
            }

            m_InspectorScroll = EditorGUILayout.BeginScrollView(m_InspectorScroll);
            EditorGUILayout.LabelField($"配置面板 / 选中节点 {m_Selected}", EditorStyles.boldLabel);
            DrawInspector();
            EditorGUILayout.EndScrollView();
        }

        private void DrawFooter()
        {
            EditorGUILayout.LabelField(m_TimelineHeader ?? "整数帧时间轴");
            EditorGUILayout.LabelField(m_TimelineTicks ?? "");
            EditorGUILayout.LabelField(m_Validation, EditorStyles.wordWrappedLabel, GUILayout.Height(44));
        }

        private void DrawInspector()
        {
            EditorGUI.BeginChangeCheck();
            CharacterAttributeAsset source = (CharacterAttributeAsset)EditorGUILayout.ObjectField("施加者预览角色", m_SourcePreview, typeof(CharacterAttributeAsset), false);
            CharacterAttributeAsset target = (CharacterAttributeAsset)EditorGUILayout.ObjectField("目标预览角色（可选）", m_TargetPreview, typeof(CharacterAttributeAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                SetPreviewContext(source, target);
            }

            EditorGUILayout.HelpBox("预览角色只用于筛选属性，不保存到 Effect。实际施加者和目标由运行时传入。", MessageType.None);
            m_Serialized.UpdateIfRequiredOrScript();
            foreach (string name in s_EffectFields)
            {
                EditorGUILayout.PropertyField(m_Serialized.FindProperty(name), true);
            }

            if (m_NodeChoices.Length > 0)
            {
                int index = Array.FindIndex(m_Graph.Nodes, node => node != null && node.NodeId == m_Selected);
                index = EditorGUILayout.Popup("选中节点", Math.Max(0, index), m_NodeChoices);
                EffectValueNode node = m_Graph.Nodes[index];
                if (node != null)
                {
                    m_Selected = node.NodeId;
                    SerializedProperty selected = m_Serialized.FindProperty("Nodes").GetArrayElementAtIndex(index);
                    Field(selected, "NodeId");
                    Field(selected, "Kind");
                    EffectValueKind kind = (EffectValueKind)selected.FindPropertyRelative("Kind").intValue;
                    if (kind == EffectValueKind.Constant)
                    {
                        Field(selected, "Constant");
                    }
                    else if (kind == EffectValueKind.SetByCaller)
                    {
                        Field(selected, "ParameterId");
                    }
                    else if (kind == EffectValueKind.Arithmetic)
                    {
                        Field(selected, "Arithmetic");
                        using (new EditorGUI.DisabledScope(true))
                        {
                            Field(selected, "LeftNodeId");
                            Field(selected, "RightNodeId");
                        }
                    }
                    else
                    {
                        bool isSource = kind == EffectValueKind.SourceBase || kind == EffectValueKind.SourceCurrent;
                        AttributeField(selected, isSource ? m_SourceOptions : m_TargetOptions);
                    }

                    if (GUILayout.Button("删除选中节点"))
                    {
                        Record();
                        List<EffectValueNode> nodes = new List<EffectValueNode>(m_Graph.Nodes);
                        nodes.RemoveAt(index);
                        m_Graph.Nodes = nodes.ToArray();
                        m_Serialized.Update();
                    }
                }
            }

            if (m_ModifierChoices.Length > 0)
            {
                m_ModifierIndex = EditorGUILayout.Popup("选中修改器", Math.Min(m_ModifierIndex, m_ModifierChoices.Length - 1), m_ModifierChoices);
                SerializedProperty modifier = m_Serialized.FindProperty("Modifiers").GetArrayElementAtIndex(m_ModifierIndex);
                AttributeField(modifier, m_TargetOptions);
                Field(modifier, "Operation");
                Field(modifier, "Priority");
                using (new EditorGUI.DisabledScope(true))
                {
                    Field(modifier, "ValueNodeId");
                }
                if (GUILayout.Button("删除选中修改器"))
                {
                    Record();
                    List<EffectModifierNode> modifiers = new List<EffectModifierNode>(m_Graph.Modifiers);
                    modifiers.RemoveAt(m_ModifierIndex);
                    m_Graph.Modifiers = modifiers.ToArray();
                    m_Serialized.Update();
                }
            }

            if (m_Serialized.ApplyModifiedProperties())
            {
                Invalidate();
                m_Validation = "配置已修改，请重新校验 / 编译。";
            }
        }

        private static readonly string[] s_EffectFields =
        {
            "SchemaVersion", "ReleaseVersion", "EffectDefinitionId", "DurationKind",
            "DurationFrames", "PeriodFrames", "MarkerIds", "RequiredParameters"
        };

        private static void Field(SerializedProperty parent, string name)
        {
            EditorGUILayout.PropertyField(parent.FindPropertyRelative(name));
        }

        private static void AttributeField(SerializedProperty parent, EffectAttributeOptions options)
        {
            SerializedProperty property = parent.FindPropertyRelative("AttributeId");
            uint id = (uint)property.longValue;
            int current = options.IndexOf(id) + 1;
            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup("属性", current, options.Labels);
            if (EditorGUI.EndChangeCheck() && selected > 0)
            {
                property.longValue = options.At(selected);
            }

            if (current == 0)
            {
                EditorGUILayout.HelpBox($"当前 AttributeId = {id} 不在可选范围中，请选择有效属性。不会自动替换编号。", MessageType.Warning);
            }
        }

        public void AddNode(EffectValueKind kind)
        {
            if (m_Graph == null)
            {
                return;
            }

            Record();
            List<EffectValueNode> nodes = new List<EffectValueNode>(m_Graph.Nodes ?? Array.Empty<EffectValueNode>());
            int id = 1;
            foreach (EffectValueNode node in nodes)
            {
                if (node != null)
                {
                    id = Math.Max(id, checked(node.NodeId + 1));
                }
            }

            nodes.Add(new EffectValueNode { NodeId = id, Kind = kind, Position = new Vector2(50 + nodes.Count % 3 * 250, 50 + nodes.Count / 3 * 145) });
            m_Graph.Nodes = nodes.ToArray();
            m_Selected = id;
        }

        public void Connect(int outputId, int inputId, bool right)
        {
            if (m_Graph == null)
            {
                return;
            }

            foreach (EffectValueNode node in m_Graph.Nodes)
            {
                if (node != null && node.NodeId == inputId && node.Kind == EffectValueKind.Arithmetic)
                {
                    Record();
                    if (right)
                    {
                        node.RightNodeId = outputId;
                    }
                    else
                    {
                        node.LeftNodeId = outputId;
                    }

                    return;
                }
            }
        }

        private void BuildTimeline()
        {
            string end = m_Graph.DurationKind == EffectDurationKind.Finite ? m_Graph.DurationFrames.ToString() : "∞";
            m_TimelineHeader = $"整数帧时间轴：施加 0    区间 [0, {end})    周期 {m_Graph.PeriodFrames} 帧    到期先移除，施加帧不触发周期";
            if (m_Graph.DurationKind == EffectDurationKind.Instant)
            {
                m_TimelineTicks = "0: 瞬时修改 Base，无活跃实例。";
                return;
            }

            List<string> ticks = new List<string> { "0 施加" };
            long period = m_Graph.PeriodFrames;
            for (int i = 1; period > 0 && i <= 8 && period <= long.MaxValue / i; i++)
            {
                long frame = period * i;
                if (m_Graph.DurationKind == EffectDurationKind.Finite && frame >= m_Graph.DurationFrames)
                {
                    break;
                }

                ticks.Add($"{frame} 周期");
            }

            ticks.Add(m_Graph.DurationKind == EffectDurationKind.Finite ? $"{end} 到期" : "∞ 持续");
            m_TimelineTicks = string.Join("  ───  ", ticks);
        }

        private void ValidateCatalog()
        {
            bool success = ConfigurationCompiler.TryCompileEffect(m_Catalog, m_Graph, out CompiledEffect compiled, out IReadOnlyList<ConfigurationIssue> issues);
            m_Validation = success ? $"效果 {compiled.Definition.Id} 编译成功；实际角色属性在运行时施加时校验。" :
                issues.Count == 0 ? "编译失败。" : issues[0].ToString() + $"（共 {issues.Count} 项，调试窗口可查看全部）";
            if (success && m_SourcePreview != null)
            {
                EffectAttributeOptions options = new EffectAttributeOptions(m_Catalog, m_SourcePreview, true);
                foreach (EffectValueNode node in m_Graph.Nodes)
                {
                    if ((node.Kind == EffectValueKind.SourceBase || node.Kind == EffectValueKind.SourceCurrent) && options.IndexOf(node.AttributeId) < 0)
                    {
                        m_Selected = node.NodeId;
                        m_Validation = $"通用效果编译成功，但施加者预览角色不具备节点 #{node.NodeId} 所读属性 {node.AttributeId}，该角色无法施加此效果。";
                        break;
                    }
                }
            }
            if (!success && issues.Count > 0)
            {
                m_Selected = issues[0].NodeId;
                EditorGUIUtility.PingObject(issues[0].Asset);
            }
        }

        private void Record()
        {
            Undo.RecordObject(m_Graph, "编辑 AshACT 效果图");
            EditorUtility.SetDirty(m_Graph);
            Invalidate();
        }
    }
}
