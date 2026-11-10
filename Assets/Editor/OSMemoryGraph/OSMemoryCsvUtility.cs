using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MrTGame.Narrative;
using UnityEditor;
using UnityEngine;

namespace MrTGame.EditorTools.OSMemoryGraph
{
    public sealed class OSMemoryCsvReport
    {
        readonly List<string> errors = new List<string>();
        readonly List<string> warnings = new List<string>();

        public bool Success => errors.Count == 0;
        public bool Applied { get; internal set; }
        public int NodeCount { get; internal set; }
        public int OptionCount { get; internal set; }
        public int LinkCount { get; internal set; }
        public int ActionCount { get; internal set; }
        public IReadOnlyList<string> Errors => errors;
        public IReadOnlyList<string> Warnings => warnings;

        internal void AddError(string message)
        {
            errors.Add(message);
        }

        internal void AddWarning(string message)
        {
            warnings.Add(message);
        }
    }

    public static class OSMemoryCsvUtility
    {
        public const int SchemaVersion = 1;

        public const string Header =
            "schemaVersion,rowType,startDataIndex,nodeOrder,dataIndex,sourceText,nextDataIndex,itemOrder," +
            "optionId,targetDataIndex,heart,battery,fault,linkId,ownerType,ownerOrder,actionOrder," +
            "actionId,parameter,assetGlobalId,assetPath,assetType";

        const string DefaultAssetPath = "Assets/Data/OSMemoryData.asset";
        const string LastDirectoryEditorPrefsKey = "MrTGame.OSMemoryCsv.LastDirectory";
        const string UndoName = "Import OS Memory CSV";

        const string MetaRowType = "meta";
        const string NodeRowType = "node";
        const string OptionRowType = "option";
        const string LinkRowType = "link";
        const string ActionRowType = "action";

        const string NodeOwnerType = "node";
        const string OptionOwnerType = "option";
        const string LinkOwnerType = "link";

        static readonly string[] HeaderColumns = Header.Split(',');

        enum Column
        {
            SchemaVersion,
            RowType,
            StartDataIndex,
            NodeOrder,
            DataIndex,
            SourceText,
            NextDataIndex,
            ItemOrder,
            OptionId,
            TargetDataIndex,
            Heart,
            Battery,
            Fault,
            LinkId,
            OwnerType,
            OwnerOrder,
            ActionOrder,
            ActionId,
            Parameter,
            AssetGlobalId,
            AssetPath,
            AssetType
        }

        [MenuItem("Tools/MrTGame/OS Memory CSV/Export CSV")]
        static void ExportMenu()
        {
            OSMemoryData asset = ResolveMenuAsset();
            if (asset == null)
            {
                EditorUtility.DisplayDialog(
                    "OS Memory CSV",
                    "没有选中 OSMemoryData，也没有找到默认资产。",
                    "确定");
                return;
            }

            ExportWithDialog(asset);
        }

        [MenuItem("Tools/MrTGame/OS Memory CSV/Import CSV")]
        static void ImportMenu()
        {
            OSMemoryData asset = ResolveMenuAsset();
            if (asset == null)
            {
                EditorUtility.DisplayDialog(
                    "OS Memory CSV",
                    "没有选中 OSMemoryData，也没有找到默认资产。",
                    "确定");
                return;
            }

            ImportWithDialog(asset);
        }

        public static string Serialize(OSMemoryData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var rows = new List<string[]> { HeaderColumns };

            string[] meta = CreateRow(MetaRowType);
            meta[(int)Column.StartDataIndex] = FormatInt(data.StartDataIndex);
            rows.Add(meta);

            IReadOnlyList<OSMemoryTextData> dataList = data.DataList;
            if (dataList != null)
            {
                for (int nodeOrder = 0; nodeOrder < dataList.Count; nodeOrder++)
                {
                    OSMemoryTextData node = dataList[nodeOrder];
                    if (node == null)
                    {
                        throw new InvalidOperationException(
                            $"OSMemoryData.dataList[{nodeOrder}] 为空，无法无损导出 CSV。");
                    }

                    string[] nodeRow = CreateRow(NodeRowType);
                    nodeRow[(int)Column.NodeOrder] = FormatInt(nodeOrder);
                    nodeRow[(int)Column.DataIndex] = FormatInt(node.DataIndex);
                    nodeRow[(int)Column.SourceText] = node.SourceText ?? string.Empty;
                    nodeRow[(int)Column.NextDataIndex] = FormatInt(node.NextDataIndex);
                    rows.Add(nodeRow);

                    AppendActionRows(rows, node.DataIndex, NodeOwnerType, 0, node.OnEnterActions);

                    IReadOnlyList<OSMemoryOptionData> options = node.Options;
                    if (options != null)
                    {
                        for (int itemOrder = 0; itemOrder < options.Count; itemOrder++)
                        {
                            OSMemoryOptionData option = options[itemOrder];
                            if (option == null)
                            {
                                throw new InvalidOperationException(
                                    $"DataIndex {node.DataIndex} 的 options[{itemOrder}] 为空，无法无损导出 CSV。");
                            }

                            OSMemoryResourceChange change = option.ResourceChange;
                            if (change == null)
                            {
                                throw new InvalidOperationException(
                                    $"DataIndex {node.DataIndex} 的 options[{itemOrder}].resourceChange 为空。 ");
                            }

                            string[] optionRow = CreateRow(OptionRowType);
                            optionRow[(int)Column.DataIndex] = FormatInt(node.DataIndex);
                            optionRow[(int)Column.SourceText] = option.SourceText ?? string.Empty;
                            optionRow[(int)Column.ItemOrder] = FormatInt(itemOrder);
                            optionRow[(int)Column.OptionId] = FormatInt(option.OptionId);
                            optionRow[(int)Column.TargetDataIndex] = FormatInt(option.TargetDataIndex);
                            optionRow[(int)Column.Heart] = FormatFloat(change.Heart);
                            optionRow[(int)Column.Battery] = FormatFloat(change.Battery);
                            optionRow[(int)Column.Fault] = FormatFloat(change.Fault);
                            rows.Add(optionRow);

                            AppendActionRows(rows, node.DataIndex, OptionOwnerType, itemOrder, option.Actions);
                        }
                    }

                    IReadOnlyList<OSMemoryLinkData> links = node.Links;
                    if (links != null)
                    {
                        for (int itemOrder = 0; itemOrder < links.Count; itemOrder++)
                        {
                            OSMemoryLinkData link = links[itemOrder];
                            if (link == null)
                            {
                                throw new InvalidOperationException(
                                    $"DataIndex {node.DataIndex} 的 links[{itemOrder}] 为空，无法无损导出 CSV。");
                            }

                            string[] linkRow = CreateRow(LinkRowType);
                            linkRow[(int)Column.DataIndex] = FormatInt(node.DataIndex);
                            linkRow[(int)Column.ItemOrder] = FormatInt(itemOrder);
                            linkRow[(int)Column.LinkId] = link.LinkId ?? string.Empty;
                            rows.Add(linkRow);

                            AppendActionRows(rows, node.DataIndex, LinkOwnerType, itemOrder, link.Actions);
                        }
                    }
                }
            }

            return SerializeRows(rows);
        }

        public static OSMemoryCsvReport Validate(string csv)
        {
            return ParseDocument(csv).Report;
        }

        public static OSMemoryCsvReport Import(OSMemoryData target, string csv)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            ParseResult parsed = ParseDocument(csv);
            if (!parsed.Report.Success)
                return parsed.Report;

            try
            {
                ApplyDocument(target, parsed.Document);
                parsed.Report.Applied = true;
            }
            catch (Exception exception)
            {
                parsed.Report.AddError($"写入 OSMemoryData 失败：{exception.Message}");
                Debug.LogException(exception);
            }

            return parsed.Report;
        }

        public static bool TryImport(OSMemoryData target, string csv, out OSMemoryCsvReport report)
        {
            report = Import(target, csv);
            return report.Success && report.Applied;
        }

        public static bool ExportWithDialog(OSMemoryData data)
        {
            if (data == null)
                return false;

            string path = EditorUtility.SaveFilePanel(
                "导出 OS Memory CSV",
                GetInitialDirectory(data),
                data.name + ".csv",
                "csv");
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                File.WriteAllText(path, Serialize(data), new UTF8Encoding(true));
                RememberDirectory(path);
                EditorUtility.DisplayDialog(
                    "OS Memory CSV",
                    $"已导出 {data.DataList?.Count ?? 0} 个节点：\n{path}",
                    "确定");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "OS Memory CSV 导出失败",
                    exception.Message,
                    "确定");
                return false;
            }
        }

        public static bool ImportWithDialog(OSMemoryData target)
        {
            if (target == null)
                return false;

            string path = EditorUtility.OpenFilePanel(
                "导入 OS Memory CSV",
                GetInitialDirectory(target),
                "csv");
            if (string.IsNullOrEmpty(path))
                return false;

            string csv;
            try
            {
                csv = File.ReadAllText(path, new UTF8Encoding(false, true));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "OS Memory CSV 读取失败",
                    exception.Message,
                    "确定");
                return false;
            }

            ParseResult parsed = ParseDocument(csv);
            if (!parsed.Report.Success)
            {
                EditorUtility.DisplayDialog(
                    "OS Memory CSV 校验失败",
                    BuildReportMessage(parsed.Report, true),
                    "确定");
                return false;
            }

            string confirmation =
                $"将用 CSV 完整替换 {target.name}。\n" +
                $"节点 {parsed.Report.NodeCount}，选项 {parsed.Report.OptionCount}，" +
                $"链接 {parsed.Report.LinkCount}，动作 {parsed.Report.ActionCount}。";
            if (parsed.Report.Warnings.Count > 0)
                confirmation += "\n\n" + BuildReportMessage(parsed.Report, false);

            if (!EditorUtility.DisplayDialog(
                    "导入 OS Memory CSV",
                    confirmation,
                    "导入",
                    "取消"))
            {
                return false;
            }

            try
            {
                ApplyDocument(target, parsed.Document);
                parsed.Report.Applied = true;
                RememberDirectory(path);
                EditorUtility.DisplayDialog(
                    "OS Memory CSV",
                    $"已导入 {parsed.Report.NodeCount} 个节点。",
                    "确定");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog(
                    "OS Memory CSV 导入失败",
                    exception.Message,
                    "确定");
                return false;
            }
        }

        static void AppendActionRows(
            ICollection<string[]> rows,
            int dataIndex,
            string ownerType,
            int ownerOrder,
            IReadOnlyList<OSMemoryActionData> actions)
        {
            if (actions == null)
                return;

            for (int actionOrder = 0; actionOrder < actions.Count; actionOrder++)
            {
                OSMemoryActionData action = actions[actionOrder];
                if (action == null)
                {
                    throw new InvalidOperationException(
                        $"DataIndex {dataIndex} 的 {ownerType}[{ownerOrder}] actions[{actionOrder}] 为空。 ");
                }

                string[] row = CreateRow(ActionRowType);
                row[(int)Column.DataIndex] = FormatInt(dataIndex);
                row[(int)Column.OwnerType] = ownerType;
                row[(int)Column.OwnerOrder] = FormatInt(ownerOrder);
                row[(int)Column.ActionOrder] = FormatInt(actionOrder);
                row[(int)Column.ActionId] = action.ActionId ?? string.Empty;
                row[(int)Column.Parameter] = action.Parameter ?? string.Empty;

                UnityEngine.Object asset = action.Asset;
                if (asset != null)
                {
                    if (!EditorUtility.IsPersistent(asset))
                    {
                        throw new InvalidOperationException(
                            $"DataIndex {dataIndex} 的 {ownerType}[{ownerOrder}] actions[{actionOrder}] " +
                            $"引用了非持久化对象 '{asset.name}'，无法导出。 ");
                    }

                    GlobalObjectId globalId = GlobalObjectId.GetGlobalObjectIdSlow(asset);
                    row[(int)Column.AssetGlobalId] = globalId.ToString();
                    row[(int)Column.AssetPath] = AssetDatabase.GetAssetPath(asset) ?? string.Empty;
                    row[(int)Column.AssetType] =
                        asset.GetType().AssemblyQualifiedName ?? asset.GetType().FullName ?? string.Empty;
                }

                rows.Add(row);
            }
        }

        static ParseResult ParseDocument(string csv)
        {
            var report = new OSMemoryCsvReport();
            if (!TryParseRows(csv, out List<CsvRow> rows, out string parseError))
            {
                report.AddError(parseError);
                return new ParseResult(null, report);
            }

            if (rows.Count == 0)
            {
                report.AddError("CSV 为空。 ");
                return new ParseResult(null, report);
            }

            ValidateHeader(rows[0], report);
            if (!report.Success)
                return new ParseResult(null, report);

            var document = new CsvDocument();
            var rawOptions = new List<OptionDto>();
            var rawLinks = new List<LinkDto>();
            var rawActions = new List<ActionDto>();
            int metaCount = 0;

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                CsvRow row = rows[rowIndex];
                if (IsBlankRow(row))
                    continue;

                if (row.Fields.Length != HeaderColumns.Length)
                {
                    report.AddError(
                        $"第 {row.LineNumber} 行有 {row.Fields.Length} 列，应为 {HeaderColumns.Length} 列。 ");
                    continue;
                }

                if (!TryReadInt(row, Column.SchemaVersion, report, out int version))
                    continue;
                if (version != SchemaVersion)
                {
                    report.AddError(
                        $"第 {row.LineNumber} 行 schemaVersion={version}，当前仅支持 {SchemaVersion}。 ");
                    continue;
                }

                string rowType = row[Column.RowType];
                switch (rowType)
                {
                    case MetaRowType:
                        metaCount++;
                        ParseMetaRow(row, document, report);
                        break;
                    case NodeRowType:
                        ParseNodeRow(row, document.Nodes, report);
                        break;
                    case OptionRowType:
                        ParseOptionRow(row, rawOptions, report);
                        break;
                    case LinkRowType:
                        ParseLinkRow(row, rawLinks, report);
                        break;
                    case ActionRowType:
                        ParseActionRow(row, rawActions, report);
                        break;
                    default:
                        report.AddError(
                            $"第 {row.LineNumber} 行 rowType '{rowType}' 无效。 ");
                        break;
                }
            }

            if (metaCount != 1)
                report.AddError($"CSV 必须且只能包含一行 meta，当前为 {metaCount} 行。 ");

            BuildDocument(document, rawOptions, rawLinks, rawActions, report);
            ValidateDocument(document, report);

            report.NodeCount = document.Nodes.Count;
            report.OptionCount = rawOptions.Count;
            report.LinkCount = rawLinks.Count;
            report.ActionCount = rawActions.Count;
            return new ParseResult(document, report);
        }

        static void ParseMetaRow(CsvRow row, CsvDocument document, OSMemoryCsvReport report)
        {
            ValidateUnusedColumns(
                row,
                report,
                Column.SchemaVersion,
                Column.RowType,
                Column.StartDataIndex);

            if (TryReadInt(row, Column.StartDataIndex, report, out int startDataIndex))
                document.StartDataIndex = startDataIndex;
        }

        static void ParseNodeRow(CsvRow row, ICollection<NodeDto> nodes, OSMemoryCsvReport report)
        {
            ValidateUnusedColumns(
                row,
                report,
                Column.SchemaVersion,
                Column.RowType,
                Column.NodeOrder,
                Column.DataIndex,
                Column.SourceText,
                Column.NextDataIndex);

            bool valid = true;
            valid &= TryReadNonNegativeInt(row, Column.NodeOrder, report, out int nodeOrder);
            valid &= TryReadInt(row, Column.DataIndex, report, out int dataIndex);
            valid &= TryReadInt(row, Column.NextDataIndex, report, out int nextDataIndex);
            if (dataIndex == OSMemoryData.InvalidDataIndex)
            {
                report.AddError(
                    $"第 {row.LineNumber} 行 dataIndex 不能使用保留值 {OSMemoryData.InvalidDataIndex}。 ");
                valid = false;
            }

            if (!valid)
                return;

            nodes.Add(new NodeDto
            {
                SourceLine = row.LineNumber,
                NodeOrder = nodeOrder,
                DataIndex = dataIndex,
                SourceText = row[Column.SourceText],
                NextDataIndex = nextDataIndex
            });
        }

        static void ParseOptionRow(CsvRow row, ICollection<OptionDto> options, OSMemoryCsvReport report)
        {
            ValidateUnusedColumns(
                row,
                report,
                Column.SchemaVersion,
                Column.RowType,
                Column.DataIndex,
                Column.SourceText,
                Column.ItemOrder,
                Column.OptionId,
                Column.TargetDataIndex,
                Column.Heart,
                Column.Battery,
                Column.Fault);

            bool valid = true;
            valid &= TryReadInt(row, Column.DataIndex, report, out int dataIndex);
            valid &= TryReadNonNegativeInt(row, Column.ItemOrder, report, out int itemOrder);
            valid &= TryReadInt(row, Column.OptionId, report, out int optionId);
            valid &= TryReadInt(row, Column.TargetDataIndex, report, out int targetDataIndex);
            valid &= TryReadFloat(row, Column.Heart, report, out float heart);
            valid &= TryReadFloat(row, Column.Battery, report, out float battery);
            valid &= TryReadFloat(row, Column.Fault, report, out float fault);
            if (!valid)
                return;

            options.Add(new OptionDto
            {
                SourceLine = row.LineNumber,
                DataIndex = dataIndex,
                ItemOrder = itemOrder,
                OptionId = optionId,
                SourceText = row[Column.SourceText],
                TargetDataIndex = targetDataIndex,
                Heart = heart,
                Battery = battery,
                Fault = fault
            });
        }

        static void ParseLinkRow(CsvRow row, ICollection<LinkDto> links, OSMemoryCsvReport report)
        {
            ValidateUnusedColumns(
                row,
                report,
                Column.SchemaVersion,
                Column.RowType,
                Column.DataIndex,
                Column.ItemOrder,
                Column.LinkId);

            bool valid = true;
            valid &= TryReadInt(row, Column.DataIndex, report, out int dataIndex);
            valid &= TryReadNonNegativeInt(row, Column.ItemOrder, report, out int itemOrder);
            if (!valid)
                return;

            links.Add(new LinkDto
            {
                SourceLine = row.LineNumber,
                DataIndex = dataIndex,
                ItemOrder = itemOrder,
                LinkId = row[Column.LinkId]
            });
        }

        static void ParseActionRow(CsvRow row, ICollection<ActionDto> actions, OSMemoryCsvReport report)
        {
            ValidateUnusedColumns(
                row,
                report,
                Column.SchemaVersion,
                Column.RowType,
                Column.DataIndex,
                Column.OwnerType,
                Column.OwnerOrder,
                Column.ActionOrder,
                Column.ActionId,
                Column.Parameter,
                Column.AssetGlobalId,
                Column.AssetPath,
                Column.AssetType);

            bool valid = true;
            valid &= TryReadInt(row, Column.DataIndex, report, out int dataIndex);
            valid &= TryReadNonNegativeInt(row, Column.OwnerOrder, report, out int ownerOrder);
            valid &= TryReadNonNegativeInt(row, Column.ActionOrder, report, out int actionOrder);

            string ownerType = row[Column.OwnerType];
            if (ownerType != NodeOwnerType && ownerType != OptionOwnerType && ownerType != LinkOwnerType)
            {
                report.AddError(
                    $"第 {row.LineNumber} 行 ownerType '{ownerType}' 无效。 ");
                valid = false;
            }
            else if (ownerType == NodeOwnerType && ownerOrder != 0)
            {
                report.AddError(
                    $"第 {row.LineNumber} 行 node action 的 ownerOrder 必须为 0。 ");
                valid = false;
            }

            if (!valid)
                return;

            actions.Add(new ActionDto
            {
                SourceLine = row.LineNumber,
                DataIndex = dataIndex,
                OwnerType = ownerType,
                OwnerOrder = ownerOrder,
                ActionOrder = actionOrder,
                ActionId = row[Column.ActionId],
                Parameter = row[Column.Parameter],
                AssetGlobalId = row[Column.AssetGlobalId],
                AssetPath = row[Column.AssetPath],
                AssetType = row[Column.AssetType]
            });
        }

        static void BuildDocument(
            CsvDocument document,
            IEnumerable<OptionDto> rawOptions,
            IEnumerable<LinkDto> rawLinks,
            IEnumerable<ActionDto> rawActions,
            OSMemoryCsvReport report)
        {
            document.Nodes.Sort((left, right) => left.NodeOrder.CompareTo(right.NodeOrder));

            var nodesByDataIndex = new Dictionary<int, NodeDto>();
            var nodeOrders = new HashSet<int>();
            foreach (NodeDto node in document.Nodes)
            {
                if (!nodeOrders.Add(node.NodeOrder))
                {
                    report.AddError(
                        $"第 {node.SourceLine} 行 nodeOrder {node.NodeOrder} 重复。 ");
                }

                if (!nodesByDataIndex.TryAdd(node.DataIndex, node))
                {
                    report.AddError(
                        $"第 {node.SourceLine} 行 dataIndex {node.DataIndex} 重复。 ");
                }
            }

            foreach (OptionDto option in rawOptions)
            {
                if (!nodesByDataIndex.TryGetValue(option.DataIndex, out NodeDto node))
                {
                    report.AddError(
                        $"第 {option.SourceLine} 行 option 指向不存在的父节点 {option.DataIndex}。 ");
                    continue;
                }

                if (node.Options.Exists(item => item.ItemOrder == option.ItemOrder))
                {
                    report.AddError(
                        $"第 {option.SourceLine} 行 DataIndex {option.DataIndex} 的 option itemOrder " +
                        $"{option.ItemOrder} 重复。 ");
                    continue;
                }

                node.Options.Add(option);
            }

            foreach (LinkDto link in rawLinks)
            {
                if (!nodesByDataIndex.TryGetValue(link.DataIndex, out NodeDto node))
                {
                    report.AddError(
                        $"第 {link.SourceLine} 行 link 指向不存在的父节点 {link.DataIndex}。 ");
                    continue;
                }

                if (node.Links.Exists(item => item.ItemOrder == link.ItemOrder))
                {
                    report.AddError(
                        $"第 {link.SourceLine} 行 DataIndex {link.DataIndex} 的 link itemOrder " +
                        $"{link.ItemOrder} 重复。 ");
                    continue;
                }

                node.Links.Add(link);
            }

            foreach (NodeDto node in document.Nodes)
            {
                node.Options.Sort((left, right) => left.ItemOrder.CompareTo(right.ItemOrder));
                node.Links.Sort((left, right) => left.ItemOrder.CompareTo(right.ItemOrder));
            }

            foreach (ActionDto action in rawActions)
            {
                if (!nodesByDataIndex.TryGetValue(action.DataIndex, out NodeDto node))
                {
                    report.AddError(
                        $"第 {action.SourceLine} 行 action 指向不存在的父节点 {action.DataIndex}。 ");
                    continue;
                }

                List<ActionDto> ownerActions;
                if (action.OwnerType == NodeOwnerType)
                {
                    ownerActions = node.Actions;
                }
                else if (action.OwnerType == OptionOwnerType)
                {
                    OptionDto option = node.Options.Find(item => item.ItemOrder == action.OwnerOrder);
                    if (option == null)
                    {
                        report.AddError(
                            $"第 {action.SourceLine} 行 action 指向不存在的 option itemOrder " +
                            $"{action.OwnerOrder}。 ");
                        continue;
                    }

                    ownerActions = option.Actions;
                }
                else
                {
                    LinkDto link = node.Links.Find(item => item.ItemOrder == action.OwnerOrder);
                    if (link == null)
                    {
                        report.AddError(
                            $"第 {action.SourceLine} 行 action 指向不存在的 link itemOrder " +
                            $"{action.OwnerOrder}。 ");
                        continue;
                    }

                    ownerActions = link.Actions;
                }

                if (ownerActions.Exists(item => item.ActionOrder == action.ActionOrder))
                {
                    report.AddError(
                        $"第 {action.SourceLine} 行 actionOrder {action.ActionOrder} 在所属列表中重复。 ");
                    continue;
                }

                ResolveActionAsset(action, report);
                ownerActions.Add(action);
            }

            foreach (NodeDto node in document.Nodes)
            {
                node.Actions.Sort((left, right) => left.ActionOrder.CompareTo(right.ActionOrder));
                foreach (OptionDto option in node.Options)
                    option.Actions.Sort((left, right) => left.ActionOrder.CompareTo(right.ActionOrder));
                foreach (LinkDto link in node.Links)
                    link.Actions.Sort((left, right) => left.ActionOrder.CompareTo(right.ActionOrder));
            }
        }

        static void ValidateDocument(CsvDocument document, OSMemoryCsvReport report)
        {
            var nodesByDataIndex = new Dictionary<int, NodeDto>();
            foreach (NodeDto node in document.Nodes)
            {
                if (!nodesByDataIndex.ContainsKey(node.DataIndex))
                    nodesByDataIndex.Add(node.DataIndex, node);
            }

            if (!nodesByDataIndex.ContainsKey(document.StartDataIndex))
            {
                report.AddError(
                    $"startDataIndex {document.StartDataIndex} 对应的节点不存在。 ");
            }

            foreach (NodeDto node in document.Nodes)
            {
                ValidateTarget(node.DataIndex, "nextDataIndex", node.NextDataIndex, nodesByDataIndex, report);

                if (node.Options.Count > 3)
                {
                    report.AddWarning(
                        $"DataIndex {node.DataIndex} 有 {node.Options.Count} 个选项；当前运行时最多显示 3 个。 ");
                }

                var optionIds = new HashSet<int>();
                foreach (OptionDto option in node.Options)
                {
                    ValidateTarget(
                        node.DataIndex,
                        $"option {option.OptionId} targetDataIndex",
                        option.TargetDataIndex,
                        nodesByDataIndex,
                        report);

                    if (!optionIds.Add(option.OptionId))
                    {
                        report.AddWarning(
                            $"DataIndex {node.DataIndex} 的 optionId {option.OptionId} 重复。 ");
                    }
                }
            }

            if (!nodesByDataIndex.ContainsKey(document.StartDataIndex))
                return;

            var reached = new HashSet<int> { document.StartDataIndex };
            var queue = new Queue<int>();
            queue.Enqueue(document.StartDataIndex);
            while (queue.Count > 0)
            {
                NodeDto node = nodesByDataIndex[queue.Dequeue()];
                EnqueueTarget(node.NextDataIndex, nodesByDataIndex, reached, queue);
                foreach (OptionDto option in node.Options)
                    EnqueueTarget(option.TargetDataIndex, nodesByDataIndex, reached, queue);
                // Picture hotspots are buttons outside the text graph. Their entry action
                // explicitly owns the nodes that can be triggered by that scene.
                foreach (ActionDto action in node.Actions)
                {
                    var picture = action.ActionId == OSMemoryActionIds.EnterInteractiveScene
                        ? action.Asset as OSMemoryPicScenarioData : null;
                    if (picture == null) continue;
                    foreach (int index in picture.memoryIndices)
                    {
                        ValidateTarget(node.DataIndex, "picture interaction", index, nodesByDataIndex, report);
                        EnqueueTarget(index, nodesByDataIndex, reached, queue);
                    }
                }
            }

            foreach (NodeDto node in document.Nodes)
            {
                if (!reached.Contains(node.DataIndex))
                {
                    report.AddWarning(
                        $"DataIndex {node.DataIndex} 从 startDataIndex {document.StartDataIndex} 不可达。 ");
                }
            }
        }

        static void ResolveActionAsset(ActionDto action, OSMemoryCsvReport report)
        {
            bool hasGlobalId = !string.IsNullOrWhiteSpace(action.AssetGlobalId);
            bool hasPath = !string.IsNullOrWhiteSpace(action.AssetPath);
            bool hasType = !string.IsNullOrWhiteSpace(action.AssetType);
            if (!hasGlobalId && !hasPath && !hasType)
                return;

            Type expectedType = null;
            if (hasType)
            {
                expectedType = Type.GetType(action.AssetType, false);
                if (expectedType == null || !typeof(UnityEngine.Object).IsAssignableFrom(expectedType))
                {
                    report.AddWarning(
                        $"第 {action.SourceLine} 行 assetType '{action.AssetType}' 无法解析，将按资源引用本身校验。 ");
                    expectedType = null;
                }
            }

            UnityEngine.Object resolved = null;
            bool resolvedByGlobalId = false;
            if (hasGlobalId)
            {
                if (GlobalObjectId.TryParse(action.AssetGlobalId, out GlobalObjectId globalId))
                {
                    resolved = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId);
                    resolvedByGlobalId = resolved != null;
                    if (resolved == null)
                    {
                        report.AddWarning(
                            $"第 {action.SourceLine} 行 assetGlobalId 无法解析到资源，尝试 assetPath。 ");
                    }
                }
                else
                {
                    report.AddWarning(
                        $"第 {action.SourceLine} 行 assetGlobalId 格式无效，尝试 assetPath。 ");
                }
            }

            if (resolved == null && hasPath)
            {
                UnityEngine.Object[] pathAssets = AssetDatabase.LoadAllAssetsAtPath(action.AssetPath);
                var matches = new List<UnityEngine.Object>();
                foreach (UnityEngine.Object candidate in pathAssets)
                {
                    if (candidate == null)
                        continue;
                    if (expectedType == null || expectedType.IsInstanceOfType(candidate))
                        matches.Add(candidate);
                }

                if (matches.Count == 1)
                {
                    resolved = matches[0];
                }
                else if (matches.Count > 1)
                {
                    report.AddError(
                        $"第 {action.SourceLine} 行 assetPath '{action.AssetPath}' 匹配到 " +
                        $"{matches.Count} 个同类型资源，无法安全确定 subasset；请提供有效的 assetGlobalId。 ");
                    return;
                }

                if (resolved != null && hasGlobalId)
                {
                    report.AddWarning(
                        $"第 {action.SourceLine} 行使用 assetPath 回退解析资源。 ");
                }
            }

            if (resolved == null)
            {
                report.AddError(
                    $"第 {action.SourceLine} 行无法解析动作资源：" +
                    $"GlobalId='{action.AssetGlobalId}', Path='{action.AssetPath}'。 ");
                return;
            }

            if (!EditorUtility.IsPersistent(resolved))
            {
                report.AddError(
                    $"第 {action.SourceLine} 行动作资源 '{resolved.name}' 不是可持久化资产，" +
                    "不能写入 OSMemoryData。 ");
                return;
            }

            if (expectedType != null && !expectedType.IsInstanceOfType(resolved))
            {
                if (!resolvedByGlobalId)
                {
                    report.AddError(
                        $"第 {action.SourceLine} 行资源类型为 {resolved.GetType().FullName}，" +
                        $"与 assetType {expectedType.FullName} 不一致。 ");
                    return;
                }

                report.AddWarning(
                    $"第 {action.SourceLine} 行 GlobalObjectId 解析到 {resolved.GetType().FullName}，" +
                    $"与 assetType {expectedType.FullName} 不同；采用 GlobalObjectId。 ");
            }

            if (hasPath)
            {
                string resolvedPath = AssetDatabase.GetAssetPath(resolved);
                if (!string.Equals(resolvedPath, action.AssetPath, StringComparison.Ordinal))
                {
                    report.AddWarning(
                        $"第 {action.SourceLine} 行 GlobalObjectId 解析到 '{resolvedPath}'，" +
                        $"与 assetPath '{action.AssetPath}' 不同；采用 GlobalObjectId。 ");
                }
            }

            action.Asset = resolved;
        }

        static void ValidateTarget(
            int sourceDataIndex,
            string fieldName,
            int targetDataIndex,
            IReadOnlyDictionary<int, NodeDto> nodes,
            OSMemoryCsvReport report)
        {
            if (targetDataIndex == OSMemoryData.InvalidDataIndex || nodes.ContainsKey(targetDataIndex))
                return;

            report.AddError(
                $"DataIndex {sourceDataIndex} 的 {fieldName} 指向不存在的 {targetDataIndex}。 ");
        }

        static void EnqueueTarget(
            int target,
            IReadOnlyDictionary<int, NodeDto> nodes,
            ISet<int> reached,
            Queue<int> queue)
        {
            if (target == OSMemoryData.InvalidDataIndex || !nodes.ContainsKey(target) || !reached.Add(target))
                return;

            queue.Enqueue(target);
        }

        static void ApplyDocument(OSMemoryData target, CsvDocument document)
        {
            var serializedObject = new SerializedObject(target);
            serializedObject.Update();

            SerializedProperty start = RequireProperty(serializedObject, "startDataIndex");
            SerializedProperty dataList = RequireProperty(serializedObject, "dataList");
            start.intValue = document.StartDataIndex;
            dataList.arraySize = document.Nodes.Count;

            for (int nodeIndex = 0; nodeIndex < document.Nodes.Count; nodeIndex++)
            {
                NodeDto node = document.Nodes[nodeIndex];
                SerializedProperty nodeProperty = dataList.GetArrayElementAtIndex(nodeIndex);
                RequireRelative(nodeProperty, "dataIndex").intValue = node.DataIndex;
                RequireRelative(nodeProperty, "sourceText").stringValue = node.SourceText ?? string.Empty;
                RequireRelative(nodeProperty, "nextDataIndex").intValue = node.NextDataIndex;

                SerializedProperty links = RequireRelative(nodeProperty, "links");
                links.arraySize = node.Links.Count;
                for (int linkIndex = 0; linkIndex < node.Links.Count; linkIndex++)
                {
                    LinkDto link = node.Links[linkIndex];
                    SerializedProperty linkProperty = links.GetArrayElementAtIndex(linkIndex);
                    RequireRelative(linkProperty, "linkId").stringValue = link.LinkId ?? string.Empty;
                    SetActions(RequireRelative(linkProperty, "actions"), link.Actions);
                }

                SerializedProperty options = RequireRelative(nodeProperty, "options");
                options.arraySize = node.Options.Count;
                for (int optionIndex = 0; optionIndex < node.Options.Count; optionIndex++)
                {
                    OptionDto option = node.Options[optionIndex];
                    SerializedProperty optionProperty = options.GetArrayElementAtIndex(optionIndex);
                    RequireRelative(optionProperty, "optionId").intValue = option.OptionId;
                    RequireRelative(optionProperty, "sourceText").stringValue = option.SourceText ?? string.Empty;
                    RequireRelative(optionProperty, "targetDataIndex").intValue = option.TargetDataIndex;

                    SerializedProperty change = RequireRelative(optionProperty, "resourceChange");
                    RequireRelative(change, "heart").floatValue = option.Heart;
                    RequireRelative(change, "battery").floatValue = option.Battery;
                    RequireRelative(change, "fault").floatValue = option.Fault;
                    SetActions(RequireRelative(optionProperty, "actions"), option.Actions);
                }

                SetActions(RequireRelative(nodeProperty, "onEnterActions"), node.Actions);
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            Undo.RecordObject(target, UndoName);
            try
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }

        }

        static void SetActions(SerializedProperty actionsProperty, IReadOnlyList<ActionDto> actions)
        {
            actionsProperty.arraySize = actions.Count;
            for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
            {
                ActionDto action = actions[actionIndex];
                SerializedProperty actionProperty = actionsProperty.GetArrayElementAtIndex(actionIndex);
                RequireRelative(actionProperty, "actionId").stringValue = action.ActionId ?? string.Empty;
                RequireRelative(actionProperty, "parameter").stringValue = action.Parameter ?? string.Empty;
                RequireRelative(actionProperty, "asset").objectReferenceValue = action.Asset;
            }
        }

        static SerializedProperty RequireProperty(SerializedObject serializedObject, string name)
        {
            SerializedProperty property = serializedObject.FindProperty(name);
            if (property == null)
                throw new InvalidOperationException($"OSMemoryData 缺少序列化字段 '{name}'。 ");
            return property;
        }

        static SerializedProperty RequireRelative(SerializedProperty parent, string name)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property == null)
                throw new InvalidOperationException($"OSMemoryData 缺少序列化字段 '{name}'。 ");
            return property;
        }

        static void ValidateHeader(CsvRow header, OSMemoryCsvReport report)
        {
            if (header.Fields.Length != HeaderColumns.Length)
            {
                report.AddError(
                    $"CSV 表头有 {header.Fields.Length} 列，应为 {HeaderColumns.Length} 列。 ");
                return;
            }

            for (int i = 0; i < HeaderColumns.Length; i++)
            {
                if (string.Equals(header.Fields[i], HeaderColumns[i], StringComparison.Ordinal))
                    continue;

                report.AddError(
                    $"CSV 表头第 {i + 1} 列应为 '{HeaderColumns[i]}'，实际为 '{header.Fields[i]}'。 ");
            }
        }

        static void ValidateUnusedColumns(
            CsvRow row,
            OSMemoryCsvReport report,
            params Column[] allowedColumns)
        {
            var allowed = new HashSet<int>();
            foreach (Column column in allowedColumns)
                allowed.Add((int)column);

            for (int i = 0; i < row.Fields.Length; i++)
            {
                if (allowed.Contains(i) || string.IsNullOrEmpty(row.Fields[i]))
                    continue;

                report.AddError(
                    $"第 {row.LineNumber} 行 rowType '{row[Column.RowType]}' 不应填写列 '{HeaderColumns[i]}'。 ");
            }
        }

        static bool TryReadInt(
            CsvRow row,
            Column column,
            OSMemoryCsvReport report,
            out int value)
        {
            string text = row[column];
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return true;

            report.AddError(
                $"第 {row.LineNumber} 行 '{HeaderColumns[(int)column]}' 必须是 int，实际为 '{text}'。 ");
            return false;
        }

        static bool TryReadNonNegativeInt(
            CsvRow row,
            Column column,
            OSMemoryCsvReport report,
            out int value)
        {
            if (!TryReadInt(row, column, report, out value))
                return false;
            if (value >= 0)
                return true;

            report.AddError(
                $"第 {row.LineNumber} 行 '{HeaderColumns[(int)column]}' 不能小于 0。 ");
            return false;
        }

        static bool TryReadFloat(
            CsvRow row,
            Column column,
            OSMemoryCsvReport report,
            out float value)
        {
            string text = row[column];
            bool parsed = float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
            if (parsed && !float.IsNaN(value) && !float.IsInfinity(value))
                return true;

            report.AddError(
                $"第 {row.LineNumber} 行 '{HeaderColumns[(int)column]}' 必须是有限 float，实际为 '{text}'。 ");
            return false;
        }

        static bool TryParseRows(string csv, out List<CsvRow> rows, out string error)
        {
            rows = new List<CsvRow>();
            error = null;
            if (csv == null)
            {
                error = "CSV 文本为 null。 ";
                return false;
            }

            int index = csv.Length > 0 && csv[0] == '\uFEFF' ? 1 : 0;
            int lineNumber = 1;
            int rowStartLine = 1;
            var fields = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;
            bool quoteClosed = false;
            bool fieldStarted = false;

            while (index < csv.Length)
            {
                char character = csv[index];
                if (inQuotes)
                {
                    if (character == '"')
                    {
                        if (index + 1 < csv.Length && csv[index + 1] == '"')
                        {
                            field.Append('"');
                            index += 2;
                        }
                        else
                        {
                            inQuotes = false;
                            quoteClosed = true;
                            index++;
                        }
                    }
                    else if (character == '\r')
                    {
                        field.Append('\r');
                        index++;
                        if (index < csv.Length && csv[index] == '\n')
                        {
                            field.Append('\n');
                            index++;
                        }

                        lineNumber++;
                    }
                    else if (character == '\n')
                    {
                        field.Append('\n');
                        index++;
                        lineNumber++;
                    }
                    else
                    {
                        field.Append(character);
                        index++;
                    }

                    continue;
                }

                if (quoteClosed)
                {
                    if (character == ',')
                    {
                        fields.Add(field.ToString());
                        field.Clear();
                        fieldStarted = false;
                        quoteClosed = false;
                        index++;
                        continue;
                    }

                    if (character == '\r' || character == '\n')
                    {
                        fields.Add(field.ToString());
                        rows.Add(new CsvRow(rowStartLine, fields.ToArray()));
                        fields.Clear();
                        field.Clear();
                        fieldStarted = false;
                        quoteClosed = false;
                        ConsumeRecordNewline(csv, ref index, ref lineNumber);
                        rowStartLine = lineNumber;
                        continue;
                    }

                    error = $"第 {lineNumber} 行：结束引号后只能出现逗号或换行。 ";
                    return false;
                }

                if (character == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    index++;
                }
                else if (character == '"')
                {
                    if (fieldStarted || field.Length > 0)
                    {
                        error = $"第 {lineNumber} 行：未转义的双引号出现在非引用字段中。 ";
                        return false;
                    }

                    inQuotes = true;
                    fieldStarted = true;
                    index++;
                }
                else if (character == '\r' || character == '\n')
                {
                    fields.Add(field.ToString());
                    rows.Add(new CsvRow(rowStartLine, fields.ToArray()));
                    fields.Clear();
                    field.Clear();
                    fieldStarted = false;
                    ConsumeRecordNewline(csv, ref index, ref lineNumber);
                    rowStartLine = lineNumber;
                }
                else
                {
                    field.Append(character);
                    fieldStarted = true;
                    index++;
                }
            }

            if (inQuotes)
            {
                error = $"第 {rowStartLine} 行：引用字段缺少结束双引号。 ";
                return false;
            }

            if (fieldStarted || quoteClosed || field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                rows.Add(new CsvRow(rowStartLine, fields.ToArray()));
            }

            return true;
        }

        static void ConsumeRecordNewline(string csv, ref int index, ref int lineNumber)
        {
            if (csv[index] == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                index += 2;
            else
                index++;

            lineNumber++;
        }

        static string SerializeRows(IEnumerable<string[]> rows)
        {
            var builder = new StringBuilder();
            foreach (string[] row in rows)
            {
                for (int column = 0; column < row.Length; column++)
                {
                    if (column > 0)
                        builder.Append(',');
                    AppendEscapedField(builder, row[column]);
                }

                builder.Append("\r\n");
            }

            return builder.ToString();
        }

        static void AppendEscapedField(StringBuilder builder, string value)
        {
            value ??= string.Empty;
            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            if (!quote)
            {
                builder.Append(value);
                return;
            }

            builder.Append('"');
            foreach (char character in value)
            {
                if (character == '"')
                    builder.Append("\"\"");
                else
                    builder.Append(character);
            }

            builder.Append('"');
        }

        static string[] CreateRow(string rowType)
        {
            var row = new string[HeaderColumns.Length];
            for (int i = 0; i < row.Length; i++)
                row[i] = string.Empty;
            row[(int)Column.SchemaVersion] = FormatInt(SchemaVersion);
            row[(int)Column.RowType] = rowType;
            return row;
        }

        static string FormatInt(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        static bool IsBlankRow(CsvRow row)
        {
            return row.Fields.Length == 1 && string.IsNullOrEmpty(row.Fields[0]);
        }

        static string BuildReportMessage(OSMemoryCsvReport report, bool includeErrors)
        {
            var builder = new StringBuilder();
            int displayed = 0;
            const int maxMessages = 12;

            if (includeErrors)
            {
                builder.AppendLine($"错误：{report.Errors.Count}");
                foreach (string message in report.Errors)
                {
                    if (displayed >= maxMessages)
                        break;
                    builder.Append("- ").AppendLine(message);
                    displayed++;
                }
            }

            if (report.Warnings.Count > 0 && displayed < maxMessages)
            {
                if (builder.Length > 0)
                    builder.AppendLine();
                builder.AppendLine($"警告：{report.Warnings.Count}");
                foreach (string message in report.Warnings)
                {
                    if (displayed >= maxMessages)
                        break;
                    builder.Append("- ").AppendLine(message);
                    displayed++;
                }
            }

            int total = (includeErrors ? report.Errors.Count : 0) + report.Warnings.Count;
            if (total > displayed)
                builder.AppendLine($"……另有 {total - displayed} 条。 ");

            return builder.ToString().TrimEnd();
        }

        static OSMemoryData ResolveMenuAsset()
        {
            if (Selection.activeObject is OSMemoryData selected)
                return selected;

            OSMemoryData defaultAsset = AssetDatabase.LoadAssetAtPath<OSMemoryData>(DefaultAssetPath);
            if (defaultAsset != null)
                return defaultAsset;

            string[] guids = AssetDatabase.FindAssets("t:OSMemoryData", new[] { "Assets/Data" });
            if (guids.Length == 0)
                guids = AssetDatabase.FindAssets("t:OSMemoryData");

            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<OSMemoryData>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        static string GetInitialDirectory(OSMemoryData data)
        {
            string remembered = EditorPrefs.GetString(LastDirectoryEditorPrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(remembered) && Directory.Exists(remembered))
                return remembered;

            string assetPath = data == null ? string.Empty : AssetDatabase.GetAssetPath(data);
            if (!string.IsNullOrEmpty(assetPath))
            {
                string fullPath = Path.GetFullPath(assetPath);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    return directory;
            }

            return Application.dataPath;
        }

        static void RememberDirectory(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                EditorPrefs.SetString(LastDirectoryEditorPrefsKey, directory);
        }

        sealed class CsvRow
        {
            public CsvRow(int lineNumber, string[] fields)
            {
                LineNumber = lineNumber;
                Fields = fields;
            }

            public int LineNumber { get; }
            public string[] Fields { get; }
            public string this[Column column] => Fields[(int)column];
        }

        sealed class ParseResult
        {
            public ParseResult(CsvDocument document, OSMemoryCsvReport report)
            {
                Document = document;
                Report = report;
            }

            public CsvDocument Document { get; }
            public OSMemoryCsvReport Report { get; }
        }

        sealed class CsvDocument
        {
            public int StartDataIndex;
            public readonly List<NodeDto> Nodes = new List<NodeDto>();
        }

        sealed class NodeDto
        {
            public int SourceLine;
            public int NodeOrder;
            public int DataIndex;
            public string SourceText;
            public int NextDataIndex;
            public readonly List<OptionDto> Options = new List<OptionDto>();
            public readonly List<LinkDto> Links = new List<LinkDto>();
            public readonly List<ActionDto> Actions = new List<ActionDto>();
        }

        sealed class OptionDto
        {
            public int SourceLine;
            public int DataIndex;
            public int ItemOrder;
            public int OptionId;
            public string SourceText;
            public int TargetDataIndex;
            public float Heart;
            public float Battery;
            public float Fault;
            public readonly List<ActionDto> Actions = new List<ActionDto>();
        }

        sealed class LinkDto
        {
            public int SourceLine;
            public int DataIndex;
            public int ItemOrder;
            public string LinkId;
            public readonly List<ActionDto> Actions = new List<ActionDto>();
        }

        sealed class ActionDto
        {
            public int SourceLine;
            public int DataIndex;
            public string OwnerType;
            public int OwnerOrder;
            public int ActionOrder;
            public string ActionId;
            public string Parameter;
            public string AssetGlobalId;
            public string AssetPath;
            public string AssetType;
            public UnityEngine.Object Asset;
        }
    }
}
