using System.Linq;
using MrTGame.Narrative;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MrTGame.EditorTools.OSMemoryGraph
{
    internal sealed class OSMemoryGraphWindow : EditorWindow
    {
        const string WindowTitle = "OS Memory Graph";
        const string StylePath = "Assets/Editor/OSMemoryGraph/OSMemoryGraph.uss";
        const string DefaultAssetPath = "Assets/Data/OSMemoryData.asset";

        [SerializeField] OSMemoryData dataAsset;

        OSMemoryGraphView graphView;
        ObjectField assetField;
        ScrollView inspector;
        Label statusLabel;
        ToolbarSearchField searchField;
        Button addButton;
        Button duplicateButton;
        Button deleteButton;
        ToolbarMenu csvMenu;
        ToolbarMenu viewMenu;
        SerializedObject inspectorSerializedData;
        bool inspectorRefreshScheduled;

        [MenuItem("Tools/MrTGame/OS Memory Graph")]
        public static void ShowWindow()
        {
            OSMemoryGraphWindow window = GetWindow<OSMemoryGraphWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(980f, 560f);
            window.Show();
        }

        public static void Open(OSMemoryData asset)
        {
            OSMemoryGraphWindow window = GetWindow<OSMemoryGraphWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(980f, 560f);
            window.dataAsset = asset;
            window.Show();
            window.Focus();

            if (window.graphView != null)
                window.SetData(asset);
        }

        [OnOpenAsset(0)]
        static bool OnOpenAsset(EntityId entityId, int line)
        {
            OSMemoryData asset = EditorUtility.EntityIdToObject(entityId) as OSMemoryData;
            if (asset == null)
                return false;

            Open(asset);
            return true;
        }

        void OnEnable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            Undo.undoRedoPerformed += HandleUndoRedo;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (styleSheet != null)
                rootVisualElement.styleSheets.Add(styleSheet);

            rootVisualElement.AddToClassList("os-memory-window");
            BuildToolbar();

            TwoPaneSplitView content = new TwoPaneSplitView(1, 340f, TwoPaneSplitViewOrientation.Horizontal);
            content.AddToClassList("os-memory-content");
            rootVisualElement.Add(content);

            graphView = new OSMemoryGraphView(HandleNodeSelected, HandleGraphDataChanged);
            graphView.AddToClassList("os-memory-graph");
            content.Add(graphView);

            inspector = new ScrollView(ScrollViewMode.Vertical);
            inspector.AddToClassList("os-memory-inspector");
            content.Add(inspector);

            statusLabel = new Label();
            statusLabel.AddToClassList("os-memory-status");
            rootVisualElement.Add(statusLabel);

            SetData(dataAsset != null ? dataAsset : FindDefaultAsset());
        }

        void BuildToolbar()
        {
            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("os-memory-toolbar");
            rootVisualElement.Add(toolbar);

            assetField = new ObjectField("数据")
            {
                objectType = typeof(OSMemoryData),
                allowSceneObjects = false
            };
            assetField.AddToClassList("os-memory-toolbar__asset");
            assetField.labelElement.style.minWidth = 30f;
            assetField.labelElement.style.width = 30f;
            assetField.RegisterValueChangedCallback(evt => SetData(evt.newValue as OSMemoryData));
            toolbar.Add(assetField);

            addButton = CreateIconButton("Toolbar Plus", "新增文本节点", AddNode);
            toolbar.Add(addButton);
            duplicateButton = CreateIconButton("TreeEditor.Duplicate", "复制当前文本节点", DuplicateNode);
            toolbar.Add(duplicateButton);
            deleteButton = CreateIconButton("TreeEditor.Trash", "删除当前文本节点", DeleteNode);
            toolbar.Add(deleteButton);
            toolbar.Add(CreateIconButton("Refresh", "从当前 SO 重新载入", RefreshGraph));
            csvMenu = new ToolbarMenu { text = "CSV", tooltip = "导入 / 导出节点数据" };
            csvMenu.menu.AppendAction("导入 CSV", _ => ImportCsv());
            csvMenu.menu.AppendAction("导出 CSV", _ => ExportCsv());
            csvMenu.AddToClassList("os-memory-toolbar__menu");
            toolbar.Add(csvMenu);

            viewMenu = new ToolbarMenu { text = "视图" };
            viewMenu.menu.AppendAction("定位起点", _ => FocusStart());
            viewMenu.menu.AppendAction("查看全图", _ => graphView?.FrameAll());
            viewMenu.menu.AppendSeparator();
            viewMenu.menu.AppendAction("全部收起", _ => graphView?.SetAllNodesExpanded(false));
            viewMenu.menu.AppendAction("全部展开", _ => graphView?.SetAllNodesExpanded(true));
            viewMenu.AddToClassList("os-memory-toolbar__menu");
            toolbar.Add(viewMenu);
            toolbar.Add(CreateButton("自动排布", "按起点和跳转层级排布", AutoLayout));
            toolbar.Add(CreateButton("校验", "检查索引、跳转和可达性", ValidateGraph));

            VisualElement spacer = new VisualElement();
            spacer.AddToClassList("os-memory-toolbar__spacer");
            toolbar.Add(spacer);

            searchField = new ToolbarSearchField();
            searchField.tooltip = "搜索 DataIndex 或原始文本";
            searchField.AddToClassList("os-memory-toolbar__search");
            searchField.RegisterValueChangedCallback(evt => graphView?.ApplySearch(evt.newValue));
            searchField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
                    return;

                graphView?.FocusFirstSearchResult();
                evt.StopPropagation();
            });
            toolbar.Add(searchField);
            toolbar.Add(CreateIconButton("ViewToolZoom", "定位首个搜索结果", () => graphView?.FocusFirstSearchResult()));
        }

        static Button CreateIconButton(string iconName, string tooltip, System.Action action)
        {
            Button button = CreateButton(string.Empty, tooltip, action);
            button.AddToClassList("os-memory-toolbar__icon");
            button.Add(new Image
            {
                image = EditorGUIUtility.IconContent(iconName).image,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore
            });
            return button;
        }

        static Button CreateButton(string text, string tooltip, System.Action action)
        {
            Button button = new Button(action)
            {
                text = text,
                tooltip = tooltip
            };
            button.AddToClassList("os-memory-toolbar__button");
            return button;
        }

        void SetData(OSMemoryData asset)
        {
            dataAsset = asset;
            assetField?.SetValueWithoutNotify(asset);
            searchField?.SetValueWithoutNotify(string.Empty);
            graphView?.SetData(asset);
            BuildInspector();
            UpdateStatus();
        }

        void BuildInspector()
        {
            if (inspector == null)
                return;

            inspector.Unbind();
            inspector.Clear();
            inspectorSerializedData = null;

            Label title = new Label("节点属性");
            title.AddToClassList("os-memory-inspector__title");
            inspector.Add(title);

            if (dataAsset == null)
            {
                Label empty = new Label("未选择 OSMemoryData");
                empty.AddToClassList("os-memory-inspector__empty");
                inspector.Add(empty);
                UpdateActionButtons();
                return;
            }

            inspectorSerializedData = new SerializedObject(dataAsset);
            inspectorSerializedData.UpdateIfRequiredOrScript();

            int selectedIndex = graphView == null ? -1 : graphView.SelectedDataListIndex;
            SerializedProperty list = inspectorSerializedData.FindProperty("dataList");
            if (selectedIndex < 0 || selectedIndex >= list.arraySize)
            {
                Label empty = new Label("未选择文本节点");
                empty.AddToClassList("os-memory-inspector__empty");
                inspector.Add(empty);
            }
            else
            {
                OSMemoryTextData selectedData = dataAsset.DataList[selectedIndex];
                Label nodeTitle = new Label(selectedData == null
                    ? $"dataList[{selectedIndex}]"
                    : $"#{selectedData.DataIndex}");
                nodeTitle.AddToClassList("os-memory-inspector__node-title");
                inspector.Add(nodeTitle);

                SerializedProperty dataProperty = list.GetArrayElementAtIndex(selectedIndex);
                AddInspectorProperty(inspector, dataProperty.FindPropertyRelative("dataIndex"), "节点编号");
                AddInspectorProperty(inspector, dataProperty.FindPropertyRelative("nextDataIndex"), "下一节点");

                TextField body = new TextField("正文")
                {
                    multiline = true,
                    isDelayed = true,
                    bindingPath = dataProperty.FindPropertyRelative("sourceText").propertyPath
                };
                body.AddToClassList("os-memory-inspector__body");
                body.RegisterValueChangedCallback(_ => HandleInspectorPropertyChanged());
                inspector.Add(body);
                AddInspectorProperty(inspector, dataProperty.FindPropertyRelative("options"), "选项", true);
                AddInspectorProperty(inspector, dataProperty.FindPropertyRelative("links"), "文本链接", false);
                AddInspectorProperty(inspector, dataProperty.FindPropertyRelative("onEnterActions"), "进入节点时触发", false);
            }

            Foldout settings = new Foldout { text = "图设置", value = false };
            settings.AddToClassList("os-memory-inspector__settings");
            AddInspectorProperty(settings, inspectorSerializedData.FindProperty("startDataIndex"), "起始节点");
            inspector.Add(settings);
            inspector.Bind(inspectorSerializedData);
            UpdateActionButtons();
        }

        void AddInspectorProperty(VisualElement parent, SerializedProperty property, string label, bool? expanded = null)
        {
            if (expanded.HasValue)
                property.isExpanded = expanded.Value;

            PropertyField field = new PropertyField(property, label);
            field.AddToClassList("os-memory-inspector__field");
            field.RegisterValueChangeCallback(_ => HandleInspectorPropertyChanged());
            parent.Add(field);
        }

        void HandleInspectorPropertyChanged()
        {
            if (dataAsset == null)
                return;

            EditorUtility.SetDirty(dataAsset);
            if (inspectorRefreshScheduled)
                return;

            inspectorRefreshScheduled = true;
            rootVisualElement.schedule.Execute(() =>
            {
                inspectorRefreshScheduled = false;
                graphView?.RefreshGraph(true);
                UpdateStatus();
            }).StartingIn(100);
        }

        void HandleNodeSelected(int listIndex)
        {
            BuildInspector();
            UpdateActionButtons();
        }

        void HandleGraphDataChanged()
        {
            BuildInspector();
            UpdateStatus();
        }

        void AddNode()
        {
            int listIndex = graphView == null ? -1 : graphView.AddNewData();
            if (listIndex >= 0)
                graphView.SelectDataListIndex(listIndex, true);
        }

        void DuplicateNode()
        {
            int listIndex = graphView == null ? -1 : graphView.DuplicateSelectedData();
            if (listIndex >= 0)
                graphView.SelectDataListIndex(listIndex, true);
        }

        void DeleteNode()
        {
            graphView?.DeleteSelectedData();
        }

        void RefreshGraph()
        {
            graphView?.RefreshGraph(true);
            BuildInspector();
            UpdateStatus();
        }

        void ImportCsv()
        {
            if (dataAsset == null || !OSMemoryCsvUtility.ImportWithDialog(dataAsset))
                return;

            graphView?.RefreshGraph(false);
            graphView?.FocusStart();
            BuildInspector();
            UpdateStatus();
        }

        void ExportCsv()
        {
            if (dataAsset != null)
                OSMemoryCsvUtility.ExportWithDialog(dataAsset);
        }

        void AutoLayout()
        {
            graphView?.AutoLayout();
        }

        void FocusStart()
        {
            graphView?.FocusStart();
        }

        void ValidateGraph()
        {
            if (graphView == null || dataAsset == null)
                return;

            graphView.RefreshGraph(true);
            OSMemoryGraphValidation validation = graphView.Validation;
            UpdateStatus();

            if (validation.ErrorCount == 0 && validation.WarningCount == 0)
            {
                EditorUtility.DisplayDialog("OS Memory 校验", "未发现索引、跳转或可达性问题。", "确定");
                return;
            }

            string details = string.Join("\n", validation.Messages.Take(24));
            if (validation.Messages.Count > 24)
                details += $"\n... 其余 {validation.Messages.Count - 24} 项请先修复后再次校验。";

            Debug.LogWarning($"[OSMemoryGraph] {validation.Summary}\n{string.Join("\n", validation.Messages)}", dataAsset);
            EditorUtility.DisplayDialog("OS Memory 校验", validation.Summary + "\n\n" + details, "确定");
        }

        void UpdateStatus()
        {
            if (statusLabel == null)
                return;

            statusLabel.RemoveFromClassList("os-memory-status--error");
            statusLabel.RemoveFromClassList("os-memory-status--warning");

            if (dataAsset == null)
            {
                statusLabel.text = "未选择数据资产";
                return;
            }

            int nodeCount = dataAsset.DataList == null ? 0 : dataAsset.DataList.Count;
            OSMemoryGraphValidation validation = graphView?.Validation;
            string summary = validation == null ? string.Empty : validation.Summary;
            statusLabel.text = $"{dataAsset.name}    {nodeCount} 个节点    {summary}";

            if (validation != null && validation.ErrorCount > 0)
                statusLabel.AddToClassList("os-memory-status--error");
            else if (validation != null && validation.WarningCount > 0)
                statusLabel.AddToClassList("os-memory-status--warning");
        }

        void UpdateActionButtons()
        {
            bool hasSelection = dataAsset != null && graphView != null && graphView.SelectedDataListIndex >= 0;
            addButton?.SetEnabled(dataAsset != null);
            duplicateButton?.SetEnabled(hasSelection);
            deleteButton?.SetEnabled(hasSelection);
            csvMenu?.SetEnabled(dataAsset != null);
            viewMenu?.SetEnabled(dataAsset != null);
        }

        void HandleUndoRedo()
        {
            if (graphView == null)
                return;

            graphView.RefreshGraph(true);
            BuildInspector();
            UpdateStatus();
        }

        static OSMemoryData FindDefaultAsset()
        {
            OSMemoryData asset = AssetDatabase.LoadAssetAtPath<OSMemoryData>(DefaultAssetPath);
            if (asset != null)
                return asset;

            string guid = AssetDatabase.FindAssets("t:OSMemoryData", new[] { "Assets/Data" }).FirstOrDefault();
            if (string.IsNullOrEmpty(guid))
                guid = AssetDatabase.FindAssets("t:OSMemoryData").FirstOrDefault();

            return string.IsNullOrEmpty(guid)
                ? null
                : AssetDatabase.LoadAssetAtPath<OSMemoryData>(AssetDatabase.GUIDToAssetPath(guid));
        }
    }
}
