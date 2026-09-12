namespace McpClient;

/// <summary>
/// 模型搜索条目 — 模型查找引擎的数据单元，由 IModelConfigLoader 的模型表投影而来
/// </summary>
public sealed record ModelSearchEntry(
    string Vendor,
    string ModelId,
    string DisplayName,
    ModelModalityKind Modalities);

/// <summary>
/// 模型查找结果 — Lines 为输出行，IsGroupList/IsModelList 标记结果类型用于格式化
/// </summary>
public sealed class ModelSearchResult
{
    public IReadOnlyList<string> Lines { get; }
    public bool IsGroupList { get; }
    public bool IsModelList { get; }

    public ModelSearchResult(IReadOnlyList<string> lines, bool isGroupList = false, bool isModelList = false)
    {
        Lines = lines;
        IsGroupList = isGroupList;
        IsModelList = isModelList;
    }

    public static ModelSearchResult Empty => new([]);
}

/// <summary>
/// 模型查找引擎 — 按功能→型号渐进式展开模型表，对齐 ToolSearchEngine 的渐进式语法。
/// <para>list_groups → 列出所有功能分组（按 ModelModalityKind 单个位）</para>
/// <para>map[功能Key] → 列出支持该功能的所有模型（vendor/modelId (DisplayName)）</para>
/// <para>map[功能Key][vendor] → 列出该 vendor 下支持该功能的模型</para>
/// <para>关键词 → 按模型名/显示名/vendor 模糊搜索</para>
/// <para>功能Key 为 ModelModalityKind 的 [EnumValue] 字符串，如 readImage/generateImage</para>
/// <para>所有查找路径均为 FrozenDictionary O(1) — 构造时建索引，查询零遍历</para>
/// </summary>
public sealed class ModelSearchEngine
{
    private readonly List<ModelSearchEntry> _models;

    /// <summary>功能分组定义 — ModelModalityKind 单个位 → 中文描述（Text 不列出，所有模型基础能力）。
    /// 必须在 KeyToModality/ModalityToDescription 之前声明，因静态字段按声明顺序初始化。</summary>
    private static readonly (ModelModalityKind Kind, string Description)[] ModalityGroups =
    [
        (ModelModalityKind.ReadImage, "图片识别"),
        (ModelModalityKind.ReadGif, "动图识别"),
        (ModelModalityKind.ReadVideo, "视频识别"),
        (ModelModalityKind.ReadAudio, "音频识别"),
        (ModelModalityKind.ReadPdf, "PDF识别"),
        (ModelModalityKind.GenerateImage, "图片生成"),
        (ModelModalityKind.GenerateVideo, "视频生成"),
        (ModelModalityKind.GenerateAudio, "音频生成"),
        (ModelModalityKind.Thinking, "扩展思考"),
        (ModelModalityKind.CodeExecution, "代码执行"),
        (ModelModalityKind.WebSearch, "网页搜索"),
        (ModelModalityKind.ToolUse, "工具使用"),
    ];

    /// <summary>功能Key → ModelModalityKind — ParseModalityKey O(1) 查找</summary>
    private static readonly FrozenDictionary<string, ModelModalityKind> KeyToModality = BuildKeyToModality();

    /// <summary>ModelModalityKind → 中文描述 — list_groups 格式化 O(1) 查找</summary>
    private static readonly FrozenDictionary<ModelModalityKind, string> ModalityToDescription = BuildModalityToDescription();

    /// <summary>功能 → 支持该功能的模型列表 — map[功能Key] O(1) 查找</summary>
    private readonly FrozenDictionary<ModelModalityKind, List<ModelSearchEntry>> _byModality;

    /// <summary>(功能, vendor) → 模型列表 — map[功能Key][vendor] O(1) 查找</summary>
    private readonly FrozenDictionary<(ModelModalityKind Modality, string Vendor), List<ModelSearchEntry>> _byModalityAndVendor;

    private static FrozenDictionary<string, ModelModalityKind> BuildKeyToModality()
    {
        var dict = new Dictionary<string, ModelModalityKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, _) in ModalityGroups)
            dict[kind.ToValue()] = kind;
        return dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<ModelModalityKind, string> BuildModalityToDescription()
    {
        var dict = new Dictionary<ModelModalityKind, string>();
        foreach (var (kind, desc) in ModalityGroups)
            dict[kind] = desc;
        return dict.ToFrozenDictionary();
    }

    private static FrozenDictionary<ModelModalityKind, List<ModelSearchEntry>> BuildByModality(List<ModelSearchEntry> models)
    {
        var dict = new Dictionary<ModelModalityKind, List<ModelSearchEntry>>();
        foreach (var model in models)
        {
            foreach (var kind in ModalityToDescription.Keys)
            {
                if (model.Modalities.HasFlag(kind))
                {
                    if (!dict.TryGetValue(kind, out var list))
                    {
                        list = [];
                        dict[kind] = list;
                    }
                    list.Add(model);
                }
            }
        }
        return dict.ToFrozenDictionary();
    }

    private static FrozenDictionary<(ModelModalityKind, string), List<ModelSearchEntry>> BuildByModalityAndVendor(List<ModelSearchEntry> models)
    {
        var dict = new Dictionary<(ModelModalityKind, string), List<ModelSearchEntry>>();
        foreach (var model in models)
        {
            foreach (var kind in ModalityToDescription.Keys)
            {
                if (model.Modalities.HasFlag(kind))
                {
                    var key = (kind, model.Vendor);
                    if (!dict.TryGetValue(key, out var list))
                    {
                        list = [];
                        dict[key] = list;
                    }
                    list.Add(model);
                }
            }
        }
        return dict.ToFrozenDictionary();
    }

    public ModelSearchEngine(IReadOnlyList<ModelSearchEntry>? models)
    {
        _models = models != null ? [.. models] : [];
        _byModality = BuildByModality(_models);
        _byModalityAndVendor = BuildByModalityAndVendor(_models);
    }

    /// <summary>
    /// 渐进式查询 — 优先级：list_groups → map[...] → 关键词搜索
    /// </summary>
    public ModelSearchResult Search(string query, int maxResults = 20)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);

        var listResult = TryListGroups(query);
        if (listResult != null) return listResult;

        var mapResult = TryMap(query);
        if (mapResult != null) return mapResult;

        return KeywordSearch(query, maxResults);
    }

    /// <summary>
    /// list_groups → 列出所有功能分组（仅列出有模型支持的分组，从 _byModality.Keys 取）
    /// </summary>
    private ModelSearchResult? TryListGroups(string query)
    {
        if (!query.Equals("list_groups", StringComparison.OrdinalIgnoreCase))
            return null;

        var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in _byModality.Keys)
        {
            if (ModalityToDescription.TryGetValue(kind, out var desc))
                groups.Add($"{kind.ToValue()}:{desc}");
        }

        var sorted = groups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToList();
        return new ModelSearchResult(sorted, isGroupList: true);
    }

    /// <summary>
    /// map[功能Key] → 列出支持该功能的所有模型（_byModality O(1) 查找）
    /// map[功能Key][vendor] → 列出该 vendor 下支持该功能的模型（_byModalityAndVendor O(1) 查找）
    /// </summary>
    private ModelSearchResult? TryMap(string query)
    {
        if (!query.StartsWith("map[", StringComparison.OrdinalIgnoreCase) || !query.EndsWith("]", StringComparison.Ordinal))
            return null;

        var inner = query["map[".Length..^1];
        if (string.IsNullOrWhiteSpace(inner)) return null;

        var segments = inner.Split("][", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return null;

        if (!KeyToModality.TryGetValue(segments[0].Trim(), out var modality))
            return new ModelSearchResult([], isModelList: true);

        if (segments.Length == 1)
        {
            if (!_byModality.TryGetValue(modality, out var models))
                return new ModelSearchResult([], isModelList: true);
            return FormatModels(models);
        }

        var vendor = segments[1].Trim();
        if (_byModalityAndVendor.TryGetValue((modality, vendor), out var vendorModels))
            return FormatModels(vendorModels);
        return new ModelSearchResult([], isModelList: true);
    }

    private static ModelSearchResult FormatModels(List<ModelSearchEntry> models)
    {
        var lines = models
            .OrderBy(m => m.Vendor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.ModelId, StringComparer.OrdinalIgnoreCase)
            .Select(m => $"{m.Vendor}/{m.ModelId} ({m.DisplayName})")
            .ToList();
        return new ModelSearchResult(lines, isModelList: true);
    }

    private ModelSearchResult KeywordSearch(string query, int maxResults)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0) return ModelSearchResult.Empty;

        var scored = new List<(ModelSearchEntry Model, int Score)>();
        foreach (var model in _models)
        {
            var score = 0;
            foreach (var term in terms)
            {
                if (model.ModelId.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 5;
                else if (model.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 3;
                else if (model.Vendor.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 2;
            }
            if (score > 0) scored.Add((model, score));
        }

        var results = scored
            .OrderByDescending(s => s.Score)
            .Take(maxResults)
            .Select(s => $"{s.Model.Vendor}/{s.Model.ModelId} ({s.Model.DisplayName})")
            .ToList();
        return new ModelSearchResult(results);
    }
}
