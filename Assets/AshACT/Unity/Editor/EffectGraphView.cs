using System;
using System.Collections.Generic;
using System.Linq;
using AshACT.Effects;
using Framework.FixedPoint;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace AshACT.Unity.Editor
{
    /// <summary>使用原生端口和拖拽连线器；图形变更才写入资产，重绘不重建节点。</summary>
    public sealed class EffectGraphView : GraphView
    {
        private sealed class Binding
        {
            internal int NodeId;
            internal int ModifierIndex = -1;
            internal bool Right;
        }

        private readonly Action m_Record;
        private readonly Action<int, int> m_Select;
        private readonly Action m_Changed;
        private EffectGraphAsset m_Asset;
        private bool m_Loading;
        public int BuildCount { get; private set; }
        public EffectGraphAsset Asset => m_Asset;

        public EffectGraphView(Action record, Action<int, int> select, Action changed)
        {
            m_Record = record;
            m_Select = select;
            m_Changed = changed;
            style.flexGrow = 1;
            style.minWidth = 300;
            SetupZoom(0.25f, 2);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            GridBackground grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();
            graphViewChanged = OnGraphChanged;
        }

        public void Load(EffectGraphAsset asset)
        {
            m_Loading = true;
            try
            {
                DeleteElements(graphElements.ToList());
                m_Asset = asset;
                BuildCount++;
                if (asset == null)
                {
                    return;
                }

                Dictionary<int, Port> outputs = new Dictionary<int, Port>();
                List<Port> inputs = new List<Port>();
                foreach (EffectValueNode value in asset.Nodes ?? Array.Empty<EffectValueNode>())
                {
                    if (value == null)
                    {
                        continue;
                    }

                    Node node = new Node { title = $"#{value.NodeId} {value.Kind}", userData = new Binding { NodeId = value.NodeId } };
                    int id = value.NodeId;
                    node.RegisterCallback<MouseDownEvent>(evt => m_Select(id, -1), TrickleDown.TrickleDown);
                    if (value.Kind == EffectValueKind.Arithmetic)
                    {
                        inputs.Add(Input(node, "L", new Binding { NodeId = id }));
                        inputs.Add(Input(node, "R", new Binding { NodeId = id, Right = true }));
                        node.extensionContainer.Add(new Label(value.Arithmetic.ToString()));
                    }
                    else
                    {
                        string label = value.Kind == EffectValueKind.SetByCaller ? $"参数 {value.ParameterId}" : $"属性 {value.AttributeId}";
                        if (value.Kind == EffectValueKind.Constant)
                        {
                            label = DecimalValue.TryParse(value.Constant, out FP64 number) ?
                                $"{DecimalValue.Format(number)} / Raw {number.Raw}" : value.Constant;
                        }

                        node.extensionContainer.Add(new Label(label));
                    }

                    Port output = node.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(FP64));
                    output.portName = "数值";
                    output.userData = new Binding { NodeId = id };
                    node.outputContainer.Add(output);
                    outputs[id] = output;
                    Finish(node, value.Position);
                }

                EffectModifierNode[] modifiers = asset.Modifiers ?? Array.Empty<EffectModifierNode>();
                for (int i = 0; i < modifiers.Length; i++)
                {
                    EffectModifierNode modifier = modifiers[i];
                    if (modifier == null)
                    {
                        continue;
                    }

                    int index = i;
                    Node node = new Node
                    {
                        title = $"修改器 {i} → 属性 {modifier.AttributeId}",
                        userData = new Binding { ModifierIndex = i }
                    };
                    node.RegisterCallback<MouseDownEvent>(evt => m_Select(0, index), TrickleDown.TrickleDown);
                    inputs.Add(Input(node, "数值输入", new Binding { ModifierIndex = i }));
                    node.extensionContainer.Add(new Label($"{modifier.Operation} / Priority {modifier.Priority}"));
                    Finish(node, modifier.Position == Vector2.zero ? new Vector2(700, 35 + i * 150) : modifier.Position);
                }

                foreach (Port input in inputs)
                {
                    if (outputs.TryGetValue(Read((Binding)input.userData), out Port output))
                    {
                        AddElement(output.ConnectTo(input));
                    }
                }
            }
            finally
            {
                m_Loading = false;
            }
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter adapter)
        {
            return ports.ToList().Where(port => port != startPort && port.node != startPort.node &&
                port.direction != startPort.direction && port.portType == startPort.portType).ToList();
        }

        private static Port Input(Node node, string name, Binding binding)
        {
            Port port = node.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(FP64));
            port.portName = name;
            port.userData = binding;
            node.inputContainer.Add(port);
            return port;
        }

        private void Finish(Node node, Vector2 position)
        {
            node.RefreshExpandedState();
            node.RefreshPorts();
            node.SetPosition(new Rect(position, new Vector2(230, 130)));
            AddElement(node);
        }

        private int Read(Binding binding)
        {
            if (binding.ModifierIndex >= 0)
            {
                return m_Asset.Modifiers[binding.ModifierIndex].ValueNodeId;
            }

            EffectValueNode node = Array.Find(m_Asset.Nodes, candidate => candidate != null && candidate.NodeId == binding.NodeId);
            return node == null ? 0 : binding.Right ? node.RightNodeId : node.LeftNodeId;
        }

        private void Write(Binding binding, int outputId)
        {
            if (binding.ModifierIndex >= 0 && binding.ModifierIndex < m_Asset.Modifiers.Length)
            {
                m_Asset.Modifiers[binding.ModifierIndex].ValueNodeId = outputId;
            }
            else
            {
                EffectValueNode node = Array.Find(m_Asset.Nodes, candidate => candidate != null && candidate.NodeId == binding.NodeId);
                if (node != null)
                {
                    if (binding.Right)
                    {
                        node.RightNodeId = outputId;
                    }
                    else
                    {
                        node.LeftNodeId = outputId;
                    }
                }
            }
        }

        private GraphViewChange OnGraphChanged(GraphViewChange change)
        {
            if (m_Loading || m_Asset == null)
            {
                return change;
            }

            m_Record();
            foreach (GraphElement element in change.elementsToRemove ?? new List<GraphElement>())
            {
                if (element is Edge edge && edge.input?.userData is Binding input)
                {
                    Write(input, 0);
                }
            }

            foreach (Edge edge in change.edgesToCreate ?? new List<Edge>())
            {
                if (edge.input?.userData is Binding input && edge.output?.userData is Binding output)
                {
                    Write(input, output.NodeId);
                }
            }

            foreach (GraphElement element in change.movedElements ?? new List<GraphElement>())
            {
                if (element is Node node && node.userData is Binding binding)
                {
                    if (binding.ModifierIndex >= 0)
                    {
                        m_Asset.Modifiers[binding.ModifierIndex].Position = node.GetPosition().position;
                    }
                    else
                    {
                        EffectValueNode model = Array.Find(m_Asset.Nodes, candidate => candidate != null && candidate.NodeId == binding.NodeId);
                        if (model != null)
                        {
                            model.Position = node.GetPosition().position;
                        }
                    }
                }
            }

            HashSet<int> removedModifiers = new HashSet<int>();
            HashSet<int> removedNodes = new HashSet<int>();
            foreach (GraphElement element in change.elementsToRemove ?? new List<GraphElement>())
            {
                if (element is Node node && node.userData is Binding binding)
                {
                    if (binding.ModifierIndex >= 0)
                    {
                        removedModifiers.Add(binding.ModifierIndex);
                    }
                    else
                    {
                        removedNodes.Add(binding.NodeId);
                    }
                }
            }

            if (removedModifiers.Count > 0)
            {
                m_Asset.Modifiers = m_Asset.Modifiers.Where((modifier, index) => !removedModifiers.Contains(index)).ToArray();
            }

            if (removedNodes.Count > 0)
            {
                m_Asset.Nodes = m_Asset.Nodes.Where(model => model == null || !removedNodes.Contains(model.NodeId)).ToArray();
            }

            m_Changed();
            return change;
        }
    }
}
