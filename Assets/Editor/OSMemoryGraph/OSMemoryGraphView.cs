using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using MrTGame.Narrative;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTGame.EditorTools.OSMemoryGraph
{
    internal enum OSMemoryPortKind
    {
        Next,
        Option
    }

    internal sealed class OSMemoryPortBinding
    {
        public OSMemoryPortBinding(int dataListIndex, OSMemoryPortKind kind, int optionIndex, Color color)
        {
            DataListIndex = dataListIndex;
            Kind = kind;
            OptionIndex = optionIndex;
            Color = color;
        }

        public int DataListIndex { get; }
        public OSMemoryPortKind Kind { get; }
        public int OptionIndex { get; }
        public Color Color { get; }
    }

    internal sealed class OSMemoryGraphValidation
    {
        public readonly HashSet<int> DuplicateListIndices = new HashSet<int>();
        public readonly HashSet<int> MissingTargetListIndices = new HashSet<int>();
        public readonly HashSet<int> UnreachableListIndices = new HashSet<int>();
        public readonly List<string> Messages = new List<string>();

        public int ErrorCount { get; set; }
        public int WarningCount { get; set; }

        public string Summary
        {
            get
            {
                if (ErrorCount == 0 && WarningCount == 0)
                    return "校验通过";

                return $"{ErrorCount} 个错误，{WarningCount} 个警告";
            }
        }
    }

    internal sealed class OSMemoryGraphNode : Node
    {
        public const float ExpandedWidth = 320f;
        public const float CompactWidth = 264f;

        static readonly Color NextColor = new Color(0.53f, 0.71f, 0.60f);
        static readonly Color[] OptionColors =
        {
            new Color(1.00f, 0.63f, 0.20f),
            new Color(0.25f, 0.68f, 0.95f),
            new Color(0.90f, 0.38f, 0.68f),
            new Color(0.68f, 0.55f, 0.96f)
        };

        readonly Action<OSMemoryGraphNode> onSelected;
        readonly Action<OSMemoryGraphNode, bool> onExpandedChanged;
        readonly Button toggleButton;
        readonly VisualElement compactContainer;
        readonly List<VisualElement> compactOptionRows = new List<VisualElement>();
        readonly bool isStart;

        public OSMemoryGraphNode(int dataListIndex, OSMemoryTextData data, bool isStart, bool initiallyExpanded,
            Action<OSMemoryGraphNode> onSelected, Action<OSMemoryGraphNode, bool> onExpandedChanged)
        {
            DataListIndex = dataListIndex;
            DataIndex = data.DataIndex;
            this.onSelected = onSelected;
            this.onExpandedChanged = onExpandedChanged;
            this.isStart = isStart;

            title = isStart ? $"{DataIndex}  START" : DataIndex.ToString();
            capabilities &= ~Capabilities.Deletable;
            AddToClassList("os-memory-node");
            if (isStart)
                AddToClassList("os-memory-node--start");

            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(int));
            InputPort.portName = "进入";
            InputPort.portColor = new Color(0.68f, 0.72f, 0.76f);
            inputContainer.Add(InputPort);

            Label preview = new Label(BuildPreview(data.SourceText, 150));
            preview.tooltip = data.SourceText;
            preview.AddToClassList("os-memory-node__preview");
            extensionContainer.Add(preview);
            if (extensionContainer.parent != null)
                extensionContainer.parent.Insert(0, extensionContainer);

            NextPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(int));
            NextPort.portName = data.NextDataIndex == OSMemoryData.InvalidDataIndex
                ? "下一句"
                : $"下一句  ->  {data.NextDataIndex}";
            NextPort.portColor = NextColor;
            NextPort.userData = new OSMemoryPortBinding(dataListIndex, OSMemoryPortKind.Next, -1, NextColor);
            NextPort.AddToClassList("os-memory-port--next");
            outputContainer.Add(NextPort);

            IReadOnlyList<OSMemoryOptionData> options = data.Options;
            EnableInClassList("os-memory-node--choice", options != null && options.Count > 0);
            if (options != null)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    OSMemoryOptionData option = options[i];
                    Color color = OptionColors[i % OptionColors.Length];
                    Port port = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(int));
                    int targetIndex = option == null ? OSMemoryData.InvalidDataIndex : option.TargetDataIndex;
                    string optionPreview = option == null ? "空选项" : BuildPreview(option.SourceText, 24);
                    string optionLabel = OSOptionLabel.Format(optionPreview, i);
                    port.portName = targetIndex == OSMemoryData.InvalidDataIndex
                        ? optionLabel
                        : $"{optionLabel}  → {targetIndex}";
                    port.tooltip = port.portName;
                    port.portColor = color;
                    port.userData = new OSMemoryPortBinding(dataListIndex, OSMemoryPortKind.Option, i, color);
                    port.AddToClassList("os-memory-port--option");
                    outputContainer.Add(port);
                    OptionPorts.Add(port);
                }
            }

            compactContainer = CreateCompactContainer(data);
            mainContainer.Add(compactContainer);

            RefreshPorts();
            toggleButton = new Button(() => SetExpanded(!expanded))
            {
                name = "os-memory-node-toggle",
                tooltip = "展开/收起节点",
                text = "\u25B6"
            };
            toggleButton.style.width = 22f;
            toggleButton.style.minWidth = 22f;
            toggleButton.style.height = 22f;
            toggleButton.style.minHeight = 22f;
            toggleButton.style.marginLeft = 2f;
            toggleButton.style.marginRight = 2f;
            toggleButton.style.paddingLeft = 0f;
            toggleButton.style.paddingRight = 0f;
            toggleButton.style.fontSize = 9f;

            VisualElement collapseButton = this.Q<VisualElement>("collapse-button");
            if (collapseButton != null)
                collapseButton.style.display = DisplayStyle.None;

            VisualElement titleButtonContainer = this.Q<VisualElement>("title-button-container");
            (titleButtonContainer ?? titleContainer).Add(toggleButton);

            SetExpanded(initiallyExpanded, false);
        }

        public int DataListIndex { get; }
        public int DataIndex { get; }
        public Port InputPort { get; }
        public Port NextPort { get; }
        public List<Port> OptionPorts { get; } = new List<Port>();
        public float PreferredWidth => expanded ? ExpandedWidth : CompactWidth;

        public override void OnSelected()
        {
            base.OnSelected();
            onSelected?.Invoke(this);
        }

        public void SetExpanded(bool value, bool notify = true)
        {
            expanded = value;
            title = isStart ? $"{DataIndex}  起点" : DataIndex.ToString();
            toggleButton.text = value ? "\u25BC" : "\u25B6";
            EnableInClassList("os-memory-node--compact", !value);

            if (value)
            {
                RestorePortsToContents();
                ConfigureExpandedChrome();
            }

            RefreshExpandedState();

            VisualElement contents = this.Q<VisualElement>("contents");
            if (contents != null)
                contents.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;

            compactContainer.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;

            if (!value)
            {
                MovePortsToCompactContainer();
                ConfigureCompactChrome();
            }

            RefreshPorts();
            schedule.Execute(() => RefreshPorts());

            style.width = PreferredWidth;
            style.minWidth = PreferredWidth;

            Rect position = GetPosition();
            if (position.width > 0f)
            {
                position.width = PreferredWidth;
                SetPosition(position);
            }

            if (notify)
                onExpandedChanged?.Invoke(this, value);
        }

        void MovePortsToCompactContainer()
        {
            MovePort(InputPort, mainContainer);
            ConfigureCompactPort(InputPort, true);

            MovePort(NextPort, mainContainer);
            ConfigureCompactPort(NextPort, false);

            for (int i = 0; i < OptionPorts.Count; i++)
            {
                Port port = OptionPorts[i];
                MovePort(port, compactOptionRows[i]);
                ConfigureCompactPort(port, false, 0f);
                port.style.right = -24f;
            }
        }

        void RestorePortsToContents()
        {
            MovePort(InputPort, inputContainer);
            ConfigureExpandedPort(InputPort);

            MovePort(NextPort, outputContainer);
            ConfigureExpandedPort(NextPort);

            foreach (Port port in OptionPorts)
            {
                MovePort(port, outputContainer);
                ConfigureExpandedPort(port);
            }
        }

        static void MovePort(Port port, VisualElement destination)
        {
            if (port.parent == destination)
                return;

            port.RemoveFromHierarchy();
            destination.Add(port);
        }

        static void ConfigureCompactPort(Port port, bool isInput, float top = 34f)
        {
            port.pickingMode = PickingMode.Position;
            port.style.position = Position.Absolute;
            port.style.top = top;
            port.style.bottom = StyleKeyword.Auto;
            port.style.width = 30f;
            port.style.height = 22f;
            port.style.display = DisplayStyle.Flex;

            if (isInput)
            {
                port.style.left = 0f;
                port.style.right = StyleKeyword.Auto;
            }
            else
            {
                port.style.left = StyleKeyword.Auto;
                port.style.right = 0f;
            }

            Label label = port.Q<Label>();
            if (label != null)
                label.style.display = DisplayStyle.None;
        }

        static void ConfigureExpandedPort(Port port)
        {
            port.pickingMode = PickingMode.Position;
            port.style.position = Position.Relative;
            port.style.left = StyleKeyword.Null;
            port.style.right = StyleKeyword.Null;
            port.style.top = StyleKeyword.Null;
            port.style.bottom = StyleKeyword.Null;
            port.style.width = StyleKeyword.Null;
            port.style.height = StyleKeyword.Null;
            port.style.display = StyleKeyword.Null;

            Label label = port.Q<Label>();
            if (label != null)
                label.style.display = StyleKeyword.Null;
        }

        VisualElement CreateCompactContainer(OSMemoryTextData data)
        {
            VisualElement container = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };
            container.style.paddingLeft = 24f;
            container.style.paddingRight = 24f;
            container.style.paddingTop = 4f;
            container.style.paddingBottom = 6f;
            container.AddToClassList("os-memory-node__compact-body");

            Label textPreview = new Label(BuildPreview(data.SourceText, 48))
            {
                tooltip = data.SourceText,
                pickingMode = PickingMode.Ignore
            };
            textPreview.style.fontSize = 12f;
            textPreview.style.height = 48f;
            textPreview.style.overflow = Overflow.Hidden;
            textPreview.style.whiteSpace = WhiteSpace.Normal;
            container.Add(textPreview);

            Label nextPreview = new Label(data.NextDataIndex == OSMemoryData.InvalidDataIndex
                ? (data.Options.Count > 0 ? "选项分支" : "终点 / 待续")
                : $"→ {data.NextDataIndex}");
            nextPreview.AddToClassList("os-memory-node__meta");
            container.Add(nextPreview);

            IReadOnlyList<OSMemoryOptionData> options = data.Options;
            for (int i = 0; i < options.Count; i++)
            {
                OSMemoryOptionData option = options[i];
                string optionText = option == null ? "(空选项)" : BuildPreview(option.SourceText, 10);
                string target = option == null || option.TargetDataIndex == OSMemoryData.InvalidDataIndex
                    ? string.Empty : $" → {option.TargetDataIndex}";
                VisualElement row = new VisualElement();
                row.style.height = 22f;
                row.style.flexShrink = 0f;
                if (i == 0)
                    row.style.marginTop = 4f;
                Label optionsPreview = new Label(OSOptionLabel.Format(optionText, i) + target)
                {
                    pickingMode = PickingMode.Ignore
                };
                optionsPreview.style.fontSize = 11f;
                optionsPreview.style.height = 22f;
                optionsPreview.style.unityTextAlign = TextAnchor.MiddleLeft;
                optionsPreview.style.whiteSpace = WhiteSpace.NoWrap;
                optionsPreview.style.overflow = Overflow.Hidden;
                optionsPreview.style.color = new Color(0.94f, 0.74f, 0.38f);
                row.tooltip = OSOptionLabel.Format(option == null ? "(空选项)" : option.SourceText, i) + target;
                row.Add(optionsPreview);
                container.Add(row);
                compactOptionRows.Add(row);
            }

            return container;
        }

        void ConfigureCompactChrome()
        {
            Label titleLabel = this.Q<Label>("title-label");
            if (titleLabel != null)
                titleLabel.style.marginLeft = 8f;
        }

        void ConfigureExpandedChrome()
        {
            Label titleLabel = this.Q<Label>("title-label");
            if (titleLabel != null)
                titleLabel.style.marginLeft = StyleKeyword.Null;
        }

        static string BuildPreview(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "(空文本)";

            string plainText = Regex.Replace(text, "<[^>]+>", string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Trim();
            return plainText.Length <= maxLength ? plainText : plainText.Substring(0, maxLength) + "...";
        }
    }

    internal sealed class OSMemoryGraphView : GraphView
    {
        const float ColumnSpacing = 410f;
        const float RowSpacing = 285f;
        const string LayoutKeyPrefix = "MrTGame.OSMemoryGraph.Layout.";

        readonly Dictionary<int, OSMemoryGraphNode> nodesByListIndex =
            new Dictionary<int, OSMemoryGraphNode>();
        readonly Dictionary<int, OSMemoryGraphNode> firstNodeByDataIndex =
            new Dictionary<int, OSMemoryGraphNode>();
        readonly HashSet<int> expandedDataIndices = new HashSet<int>();
        readonly Action<int> onNodeSelected;
        readonly Action onDataChanged;

        OSMemoryData dataAsset;
        SerializedObject serializedData;
        string assetGuid;
        string searchQuery = string.Empty;
        bool suppressGraphChanges;
        bool refreshScheduled;

        public OSMemoryGraphView(Action<int> onNodeSelected, Action onDataChanged)
        {
            this.onNodeSelected = onNodeSelected;
            this.onDataChanged = onDataChanged;

            style.flexGrow = 1f;
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            GridBackground grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            graphViewChanged = HandleGraphViewChanged;
        }

        public int SelectedDataListIndex { get; private set; } = -1;
        public OSMemoryGraphValidation Validation { get; private set; } = new OSMemoryGraphValidation();

        public void SetData(OSMemoryData asset)
        {
            dataAsset = asset;
            serializedData = asset == null ? null : new SerializedObject(asset);
            assetGuid = asset == null ? string.Empty : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
            SelectedDataListIndex = -1;
            expandedDataIndices.Clear();
            RefreshGraph(false);
            schedule.Execute(FocusStart);
        }

        public void RefreshGraph(bool preserveSelection, bool frameAll = false)
        {
            int previousSelection = preserveSelection ? SelectedDataListIndex : -1;
            suppressGraphChanges = true;

            try
            {
                ClearSelection();
                DeleteElements(graphElements.ToList());
                nodesByListIndex.Clear();
                firstNodeByDataIndex.Clear();

                if (dataAsset == null || serializedData == null)
                {
                    SelectedDataListIndex = -1;
                    Validation = new OSMemoryGraphValidation();
                    return;
                }

                serializedData.UpdateIfRequiredOrScript();
                bool hasStoredLayout = false;
                IReadOnlyList<OSMemoryTextData> dataList = dataAsset.DataList;
                if (dataList != null)
                {
                    for (int i = 0; i < dataList.Count; i++)
                    {
                        OSMemoryTextData data = dataList[i];
                        if (data == null)
                            continue;

                        OSMemoryGraphNode node = new OSMemoryGraphNode(
                            i,
                            data,
                            data.DataIndex == dataAsset.StartDataIndex,
                            expandedDataIndices.Contains(data.DataIndex),
                            HandleNodeSelected,
                            HandleNodeExpandedChanged);

                        if (TryLoadPosition(data.DataIndex, out Vector2 position))
                            hasStoredLayout = true;
                        else
                            position = new Vector2((i / 8) * ColumnSpacing, (i % 8) * RowSpacing);

                        node.SetPosition(new Rect(position, new Vector2(node.PreferredWidth, 210f)));
                        AddElement(node);
                        nodesByListIndex[i] = node;
                        if (!firstNodeByDataIndex.ContainsKey(data.DataIndex))
                            firstNodeByDataIndex.Add(data.DataIndex, node);
                    }

                    BuildEdges(dataList);
                    if (!hasStoredLayout && nodesByListIndex.Count > 0)
                        AutoLayoutInternal(true);
                }

                Validation = ValidateData();
                ApplyValidationClasses();
                ApplySearch(searchQuery);

                if (previousSelection >= 0 && nodesByListIndex.TryGetValue(previousSelection, out OSMemoryGraphNode selected))
                {
                    SelectedDataListIndex = previousSelection;
                    AddToSelection(selected);
                }
                else
                {
                    SelectedDataListIndex = -1;
                }
            }
            finally
            {
                suppressGraphChanges = false;
            }

            ScheduleEdgeGeometryRefresh();

            if (frameAll)
                schedule.Execute(() => FrameAll());
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports.ToList().Where(port =>
                port != startPort &&
                port.direction != startPort.direction &&
                port.node != startPort.node).ToList();
        }

        public int AddNewData()
        {
            if (dataAsset == null)
                return -1;

            Undo.RecordObject(dataAsset, "Add OS Memory Data");
            SerializedObject so = new SerializedObject(dataAsset);
            so.Update();
            SerializedProperty list = so.FindProperty("dataList");
            int listIndex = list.arraySize;
            int newDataIndex = GetNextDataIndex();
            list.arraySize++;
            SerializedProperty element = list.GetArrayElementAtIndex(listIndex);
            ResetNewElement(element, newDataIndex);

            if (listIndex == 0)
                so.FindProperty("startDataIndex").intValue = newDataIndex;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(dataAsset);
            SelectedDataListIndex = listIndex;
            RefreshGraph(true);
            onDataChanged?.Invoke();
            return listIndex;
        }

        public int DuplicateSelectedData()
        {
            if (dataAsset == null || SelectedDataListIndex < 0)
                return -1;

            Undo.RecordObject(dataAsset, "Duplicate OS Memory Data");
            SerializedObject so = new SerializedObject(dataAsset);
            so.Update();
            SerializedProperty list = so.FindProperty("dataList");
            if (SelectedDataListIndex >= list.arraySize)
                return -1;

            int duplicateIndex = SelectedDataListIndex + 1;
            list.InsertArrayElementAtIndex(duplicateIndex);
            SerializedProperty duplicate = list.GetArrayElementAtIndex(duplicateIndex);
            duplicate.FindPropertyRelative("dataIndex").intValue = GetNextDataIndex();
            duplicate.FindPropertyRelative("nextDataIndex").intValue = OSMemoryData.InvalidDataIndex;

            SerializedProperty options = duplicate.FindPropertyRelative("options");
            for (int i = 0; i < options.arraySize; i++)
            {
                options.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("targetDataIndex").intValue = OSMemoryData.InvalidDataIndex;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(dataAsset);
            SelectedDataListIndex = duplicateIndex;
            RefreshGraph(true);
            onDataChanged?.Invoke();
            return duplicateIndex;
        }

        public bool DeleteSelectedData()
        {
            if (dataAsset == null || SelectedDataListIndex < 0 || dataAsset.DataList == null ||
                SelectedDataListIndex >= dataAsset.DataList.Count)
                return false;

            OSMemoryTextData selected = dataAsset.DataList[SelectedDataListIndex];
            if (selected == null)
                return false;

            int deletedDataIndex = selected.DataIndex;
            bool confirmed = EditorUtility.DisplayDialog(
                "删除文本节点",
                $"确定删除 DataIndex {deletedDataIndex}？\n所有指向它的 Next 和选项目标都会被清空。",
                "删除",
                "取消");
            if (!confirmed)
                return false;

            Undo.RecordObject(dataAsset, "Delete OS Memory Data");
            SerializedObject so = new SerializedObject(dataAsset);
            so.Update();
            SerializedProperty list = so.FindProperty("dataList");

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty item = list.GetArrayElementAtIndex(i);
                SerializedProperty next = item.FindPropertyRelative("nextDataIndex");
                if (next.intValue == deletedDataIndex)
                    next.intValue = OSMemoryData.InvalidDataIndex;

                SerializedProperty options = item.FindPropertyRelative("options");
                for (int optionIndex = 0; optionIndex < options.arraySize; optionIndex++)
                {
                    SerializedProperty target = options.GetArrayElementAtIndex(optionIndex)
                        .FindPropertyRelative("targetDataIndex");
                    if (target.intValue == deletedDataIndex)
                        target.intValue = OSMemoryData.InvalidDataIndex;
                }
            }

            if (so.FindProperty("startDataIndex").intValue == deletedDataIndex)
                so.FindProperty("startDataIndex").intValue = OSMemoryData.InvalidDataIndex;

            list.DeleteArrayElementAtIndex(SelectedDataListIndex);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(dataAsset);
            SelectedDataListIndex = -1;
            RefreshGraph(false);
            onDataChanged?.Invoke();
            return true;
        }

        public void AutoLayout()
        {
            AutoLayoutInternal(true);
            schedule.Execute(FocusStart);
        }

        public void SetAllNodesExpanded(bool value)
        {
            expandedDataIndices.Clear();
            foreach (OSMemoryGraphNode node in nodesByListIndex.Values)
            {
                node.SetExpanded(value, false);
                if (value)
                    expandedDataIndices.Add(node.DataIndex);
            }

            ScheduleEdgeGeometryRefresh();
        }


        public void FocusStart()
        {
            if (dataAsset == null || !firstNodeByDataIndex.TryGetValue(dataAsset.StartDataIndex, out OSMemoryGraphNode node))
                return;

            SelectAndFrame(node);
        }

        public void ApplySearch(string query)
        {
            searchQuery = query ?? string.Empty;
            foreach (OSMemoryGraphNode node in nodesByListIndex.Values)
                node.RemoveFromClassList("os-memory-node--search");

            if (string.IsNullOrWhiteSpace(searchQuery) || dataAsset == null || dataAsset.DataList == null)
                return;

            string term = searchQuery.Trim();
            foreach (KeyValuePair<int, OSMemoryGraphNode> pair in nodesByListIndex)
            {
                OSMemoryTextData data = dataAsset.DataList[pair.Key];
                if (data.DataIndex.ToString().IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(data.SourceText) &&
                     data.SourceText.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    pair.Value.AddToClassList("os-memory-node--search");
                }
            }
        }

        public void FocusFirstSearchResult()
        {
            OSMemoryGraphNode result = nodesByListIndex.Values
                .Where(node => node.ClassListContains("os-memory-node--search"))
                .OrderBy(node => node.DataIndex)
                .FirstOrDefault();
            if (result != null)
                SelectAndFrame(result);
        }

        public void SelectDataListIndex(int listIndex, bool frame)
        {
            if (!nodesByListIndex.TryGetValue(listIndex, out OSMemoryGraphNode node))
                return;

            if (frame)
                SelectAndFrame(node);
            else
            {
                ClearSelection();
                AddToSelection(node);
            }
        }

        public OSMemoryGraphValidation ValidateData()
        {
            OSMemoryGraphValidation result = new OSMemoryGraphValidation();
            if (dataAsset == null || dataAsset.DataList == null)
                return result;

            IReadOnlyList<OSMemoryTextData> dataList = dataAsset.DataList;
            Dictionary<int, int> firstIndexById = new Dictionary<int, int>();
            for (int i = 0; i < dataList.Count; i++)
            {
                OSMemoryTextData data = dataList[i];
                if (data == null)
                {
                    result.ErrorCount++;
                    result.Messages.Add($"dataList[{i}] 为空。 ");
                    continue;
                }

                if (firstIndexById.TryGetValue(data.DataIndex, out int firstIndex))
                {
                    result.ErrorCount++;
                    result.DuplicateListIndices.Add(firstIndex);
                    result.DuplicateListIndices.Add(i);
                    result.Messages.Add($"DataIndex {data.DataIndex} 重复，位于 {firstIndex} 和 {i}。 ");
                }
                else
                {
                    firstIndexById.Add(data.DataIndex, i);
                }
            }

            if (!firstIndexById.ContainsKey(dataAsset.StartDataIndex))
            {
                result.ErrorCount++;
                result.Messages.Add($"起始 DataIndex {dataAsset.StartDataIndex} 不存在。 ");
            }

            for (int i = 0; i < dataList.Count; i++)
            {
                OSMemoryTextData data = dataList[i];
                if (data == null)
                    continue;

                ValidateTarget(data.NextDataIndex, $"{data.DataIndex} 的 Next", i, firstIndexById, result);
                IReadOnlyList<OSMemoryOptionData> options = data.Options;
                if (options == null)
                    continue;

                for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
                {
                    OSMemoryOptionData option = options[optionIndex];
                    if (option == null)
                    {
                        result.ErrorCount++;
                        result.MissingTargetListIndices.Add(i);
                        result.Messages.Add($"{data.DataIndex} 的选项 [{optionIndex}] 为空。 ");
                        continue;
                    }

                    ValidateTarget(
                        option.TargetDataIndex,
                        $"{data.DataIndex} 的选项 {option.OptionId}",
                        i,
                        firstIndexById,
                        result);
                }
            }

            HashSet<int> reachableIds = FindReachableIds(firstIndexById);
            for (int i = 0; i < dataList.Count; i++)
            {
                OSMemoryTextData data = dataList[i];
                if (data == null || reachableIds.Contains(data.DataIndex))
                    continue;

                result.UnreachableListIndices.Add(i);
                result.WarningCount++;
                result.Messages.Add($"DataIndex {data.DataIndex} 从起点不可达。 ");
            }

            return result;
        }

        void BuildEdges(IReadOnlyList<OSMemoryTextData> dataList)
        {
            for (int i = 0; i < dataList.Count; i++)
            {
                if (!nodesByListIndex.TryGetValue(i, out OSMemoryGraphNode source))
                    continue;

                OSMemoryTextData data = dataList[i];
                TryAddEdge(source.NextPort, data.NextDataIndex);

                IReadOnlyList<OSMemoryOptionData> options = data.Options;
                if (options == null)
                    continue;

                int count = Mathf.Min(options.Count, source.OptionPorts.Count);
                for (int optionIndex = 0; optionIndex < count; optionIndex++)
                {
                    OSMemoryOptionData option = options[optionIndex];
                    if (option != null)
                        TryAddEdge(source.OptionPorts[optionIndex], option.TargetDataIndex);
                }
            }
        }

        void TryAddEdge(Port output, int targetDataIndex)
        {
            if (targetDataIndex == OSMemoryData.InvalidDataIndex ||
                !firstNodeByDataIndex.TryGetValue(targetDataIndex, out OSMemoryGraphNode target))
                return;

            Edge edge = output.ConnectTo(target.InputPort);
            edge.userData = output.userData;
            AddElement(edge);
            ApplyEdgeColor(edge, ((OSMemoryPortBinding)output.userData).Color);
        }

        GraphViewChange HandleGraphViewChanged(GraphViewChange change)
        {
            if (suppressGraphChanges || dataAsset == null)
                return change;

            bool dataChanged = false;
            if (change.elementsToRemove != null)
            {
                foreach (GraphElement element in change.elementsToRemove)
                {
                    if (element is Edge edge && edge.output?.userData is OSMemoryPortBinding binding)
                        dataChanged |= SetBindingTarget(binding, OSMemoryData.InvalidDataIndex);
                }
            }

            if (change.edgesToCreate != null)
            {
                List<Edge> acceptedEdges = new List<Edge>();
                foreach (Edge edge in change.edgesToCreate)
                {
                    if (!(edge.output?.userData is OSMemoryPortBinding binding) ||
                        !(edge.input?.node is OSMemoryGraphNode targetNode))
                        continue;

                    if (!SetBindingTarget(binding, targetNode.DataIndex))
                        continue;

                    edge.userData = binding;
                    schedule.Execute(() => ApplyEdgeColor(edge, binding.Color));
                    acceptedEdges.Add(edge);
                    dataChanged = true;
                }

                change.edgesToCreate = acceptedEdges;
            }

            if (change.movedElements != null && change.movedElements.Count > 0)
                schedule.Execute(SaveAllNodePositions);

            if (dataChanged)
                ScheduleRefreshAfterGraphChange();

            return change;
        }

        bool SetBindingTarget(OSMemoryPortBinding binding, int targetDataIndex)
        {
            SerializedObject so = new SerializedObject(dataAsset);
            so.Update();
            SerializedProperty list = so.FindProperty("dataList");
            if (binding.DataListIndex < 0 || binding.DataListIndex >= list.arraySize)
                return false;

            SerializedProperty item = list.GetArrayElementAtIndex(binding.DataListIndex);
            SerializedProperty target;
            string undoName;
            if (binding.Kind == OSMemoryPortKind.Next)
            {
                target = item.FindPropertyRelative("nextDataIndex");
                undoName = "Change OS Memory Next Target";
            }
            else
            {
                SerializedProperty options = item.FindPropertyRelative("options");
                if (binding.OptionIndex < 0 || binding.OptionIndex >= options.arraySize)
                    return false;

                target = options.GetArrayElementAtIndex(binding.OptionIndex)
                    .FindPropertyRelative("targetDataIndex");
                undoName = "Change OS Memory Option Target";
            }

            if (target.intValue == targetDataIndex)
                return true;

            Undo.RecordObject(dataAsset, undoName);
            target.intValue = targetDataIndex;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(dataAsset);
            return true;
        }

        void ScheduleRefreshAfterGraphChange()
        {
            if (refreshScheduled)
                return;

            refreshScheduled = true;
            schedule.Execute(() =>
            {
                refreshScheduled = false;
                RefreshGraph(true);
                onDataChanged?.Invoke();
            });
        }

        void HandleNodeSelected(OSMemoryGraphNode node)
        {
            SelectedDataListIndex = node.DataListIndex;
            if (!suppressGraphChanges)
                onNodeSelected?.Invoke(node.DataListIndex);
        }

        void HandleNodeExpandedChanged(OSMemoryGraphNode node, bool value)
        {
            if (value)
                expandedDataIndices.Add(node.DataIndex);
            else
                expandedDataIndices.Remove(node.DataIndex);

            ScheduleEdgeGeometryRefresh();
        }

        void ScheduleEdgeGeometryRefresh()
        {
            schedule.Execute(() => UpdateEdgeGeometry());
            schedule.Execute(() => UpdateEdgeGeometry()).ExecuteLater(50);
            schedule.Execute(() => UpdateEdgeGeometry()).ExecuteLater(150);
        }

        void UpdateEdgeGeometry()
        {
            foreach (OSMemoryGraphNode node in nodesByListIndex.Values)
                node.RefreshPorts();

            foreach (Edge edge in edges)
                edge.UpdateEdgeControl();
        }


        void SelectAndFrame(OSMemoryGraphNode node)
        {
            ClearSelection();
            AddToSelection(node);
            Vector2 position = node.GetPosition().position;
            UpdateViewTransform(new Vector3(40f - position.x, 80f - position.y, 0f), Vector3.one);
        }

        int GetNextDataIndex()
        {
            int max = 990;
            IReadOnlyList<OSMemoryTextData> dataList = dataAsset.DataList;
            if (dataList != null)
            {
                foreach (OSMemoryTextData data in dataList)
                {
                    if (data != null)
                        max = Mathf.Max(max, data.DataIndex);
                }
            }

            return ((max / 10) + 1) * 10;
        }

        static void ResetNewElement(SerializedProperty element, int dataIndex)
        {
            element.FindPropertyRelative("dataIndex").intValue = dataIndex;
            element.FindPropertyRelative("sourceText").stringValue = string.Empty;
            element.FindPropertyRelative("nextDataIndex").intValue = OSMemoryData.InvalidDataIndex;
            element.FindPropertyRelative("links").ClearArray();
            element.FindPropertyRelative("options").ClearArray();
            element.FindPropertyRelative("onEnterActions").ClearArray();
        }

        static void ValidateTarget(int target, string label, int sourceListIndex,
            IReadOnlyDictionary<int, int> idLookup, OSMemoryGraphValidation result)
        {
            if (target == OSMemoryData.InvalidDataIndex || idLookup.ContainsKey(target))
                return;

            result.ErrorCount++;
            result.MissingTargetListIndices.Add(sourceListIndex);
            result.Messages.Add($"{label} 指向不存在的 DataIndex {target}。 ");
        }

        HashSet<int> FindReachableIds(IReadOnlyDictionary<int, int> firstIndexById)
        {
            HashSet<int> reached = new HashSet<int>();
            if (dataAsset == null || dataAsset.DataList == null ||
                !firstIndexById.ContainsKey(dataAsset.StartDataIndex))
                return reached;

            Queue<int> queue = new Queue<int>();
            queue.Enqueue(dataAsset.StartDataIndex);
            reached.Add(dataAsset.StartDataIndex);

            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                OSMemoryTextData data = dataAsset.DataList[firstIndexById[id]];
                EnqueueTarget(data.NextDataIndex, firstIndexById, reached, queue);

                IReadOnlyList<OSMemoryOptionData> options = data.Options;
                if (options == null)
                    continue;

                foreach (OSMemoryOptionData option in options)
                {
                    if (option != null)
                        EnqueueTarget(option.TargetDataIndex, firstIndexById, reached, queue);
                }
            }

            return reached;
        }

        static void EnqueueTarget(int target, IReadOnlyDictionary<int, int> idLookup,
            ISet<int> reached, Queue<int> queue)
        {
            if (target == OSMemoryData.InvalidDataIndex || !idLookup.ContainsKey(target) || !reached.Add(target))
                return;

            queue.Enqueue(target);
        }

        void ApplyValidationClasses()
        {
            foreach (KeyValuePair<int, OSMemoryGraphNode> pair in nodesByListIndex)
            {
                OSMemoryGraphNode node = pair.Value;
                if (Validation.DuplicateListIndices.Contains(pair.Key))
                    node.AddToClassList("os-memory-node--error");
                if (Validation.MissingTargetListIndices.Contains(pair.Key))
                    node.AddToClassList("os-memory-node--missing");
                if (Validation.UnreachableListIndices.Contains(pair.Key))
                    node.AddToClassList("os-memory-node--unreachable");
            }
        }

        void AutoLayoutInternal(bool save)
        {
            if (dataAsset == null || dataAsset.DataList == null || nodesByListIndex.Count == 0)
                return;

            IReadOnlyList<OSMemoryTextData> dataList = dataAsset.DataList;
            Dictionary<int, int> firstIndexById = new Dictionary<int, int>();
            for (int i = 0; i < dataList.Count; i++)
            {
                OSMemoryTextData data = dataList[i];
                if (data != null && !firstIndexById.ContainsKey(data.DataIndex))
                    firstIndexById.Add(data.DataIndex, i);
            }

            Dictionary<int, int> depthById = new Dictionary<int, int>();
            if (firstIndexById.ContainsKey(dataAsset.StartDataIndex))
            {
                Queue<int> queue = new Queue<int>();
                depthById[dataAsset.StartDataIndex] = 0;
                queue.Enqueue(dataAsset.StartDataIndex);

                while (queue.Count > 0)
                {
                    int id = queue.Dequeue();
                    OSMemoryTextData data = dataList[firstIndexById[id]];
                    int nextDepth = depthById[id] + 1;
                    AddLayoutTarget(data.NextDataIndex, nextDepth, firstIndexById, depthById, queue);

                    IReadOnlyList<OSMemoryOptionData> options = data.Options;
                    if (options == null)
                        continue;

                    foreach (OSMemoryOptionData option in options)
                    {
                        if (option != null)
                            AddLayoutTarget(option.TargetDataIndex, nextDepth, firstIndexById, depthById, queue);
                    }
                }
            }

            int maxDepth = depthById.Count == 0 ? -1 : depthById.Values.Max();
            IEnumerable<IGrouping<int, OSMemoryGraphNode>> columns = nodesByListIndex.Values
                .Where(node => depthById.ContainsKey(node.DataIndex))
                .GroupBy(node => depthById[node.DataIndex])
                .OrderBy(group => group.Key);

            foreach (IGrouping<int, OSMemoryGraphNode> column in columns)
            {
                float y = 0f;
                foreach (OSMemoryGraphNode node in column.OrderBy(item => item.DataIndex))
                {
                    node.SetPosition(new Rect(
                        new Vector2(column.Key * ColumnSpacing, y),
                        new Vector2(node.PreferredWidth, 210f)));
                    y += RowSpacing;
                }
            }

            float unreachableY = 0f;
            foreach (OSMemoryGraphNode node in nodesByListIndex.Values
                         .Where(item => !depthById.ContainsKey(item.DataIndex))
                         .OrderBy(item => item.DataIndex))
            {
                node.SetPosition(new Rect(
                    new Vector2((maxDepth + 1) * ColumnSpacing, unreachableY),
                    new Vector2(node.PreferredWidth, 210f)));
                unreachableY += RowSpacing;
            }

            if (save)
                SaveAllNodePositions();
        }

        static void AddLayoutTarget(int target, int depth, IReadOnlyDictionary<int, int> idLookup,
            IDictionary<int, int> depthById, Queue<int> queue)
        {
            if (target == OSMemoryData.InvalidDataIndex || !idLookup.ContainsKey(target) || depthById.ContainsKey(target))
                return;

            depthById[target] = depth;
            queue.Enqueue(target);
        }

        void SaveAllNodePositions()
        {
            if (string.IsNullOrEmpty(assetGuid))
                return;

            foreach (OSMemoryGraphNode node in nodesByListIndex.Values)
            {
                float x = node.style.left.value.value;
                float y = node.style.top.value.value;
                string value = x.ToString("R", CultureInfo.InvariantCulture) + "|" +
                               y.ToString("R", CultureInfo.InvariantCulture);
                EditorPrefs.SetString(GetLayoutKey(node.DataIndex), value);
            }
        }

        bool TryLoadPosition(int dataIndex, out Vector2 position)
        {
            position = default;
            if (string.IsNullOrEmpty(assetGuid))
                return false;

            string value = EditorPrefs.GetString(GetLayoutKey(dataIndex), string.Empty);
            string[] parts = value.Split('|');
            if (parts.Length != 2 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return false;

            position = new Vector2(x, y);
            return true;
        }

        string GetLayoutKey(int dataIndex)
        {
            return LayoutKeyPrefix + assetGuid + "." + dataIndex;
        }

        static void ApplyEdgeColor(Edge edge, Color color)
        {
            edge.edgeControl.inputColor = color;
            edge.edgeControl.outputColor = color;
            edge.edgeControl.fromCapColor = color;
            edge.edgeControl.toCapColor = color;
            edge.MarkDirtyRepaint();
        }
    }
}
