namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 连接/模型下拉管理器 — 从 MainViewModel 提取
/// 管理供应商连接列表和模型下拉选项的构建、查找与刷新
/// </summary>
internal sealed class ConnectionDropdownManager {
    /// <summary>模型下拉选项 — ObservableCollection 双向绑定，供应商切换时清空重填</summary>
    public ObservableCollection<ModelOptionItem> ModelOptions { get; } = [];

    /// <summary>连接下拉候选 — ObservableCollection 绑定 ComboBox，引用固定不丢失选中项</summary>
    public ObservableCollection<ConnectionOptionItem> ConnectionOptions { get; } = [];

    private ILookup<string, ModelOptionItem> _modelById = Array.Empty<ModelOptionItem>().ToLookup(m => m.Id);
    private ILookup<string, ConnectionOptionItem> _connectionById = Array.Empty<ConnectionOptionItem>().ToLookup(c => c.Id);

    /// <summary>按 Id O(1) 查找模型项（RefreshModelOptions 时同步重建，OrdinalIgnoreCase；null 返回 null）</summary>
    public ModelOptionItem? GetModelById(string? id) => id is null ? null : _modelById[id].FirstOrDefault();

    /// <summary>按 Id O(1) 查找连接项（RebuildConnectionOptions 时同步重建；null 返回 null）</summary>
    public ConnectionOptionItem? GetConnectionById(string? id) => id is null ? null : _connectionById[id].FirstOrDefault();

    /// <summary>刷新模型下拉 — 从 VendorModelMap 取当前供应商模型列表填充 ObservableCollection</summary>
    /// <param name="session">引擎会话门面</param>
    /// <param name="configLoader">模型配置加载器</param>
    /// <param name="selectedConnectionId">当前选中的连接 Id（null 时回退到 session.CurrentVendor）</param>
    public void RefreshModelOptions(IJccChatSession session, IModelConfigLoader configLoader, string? selectedConnectionId) {
        var provider = selectedConnectionId ?? session.CurrentVendor;
        var providerDisplay = VendorKindExtensions.FromValue(provider)?.ToString() ?? provider;
        var map = session.VendorModelMap;
        var source = map.TryGetValue(provider, out var models) && models is not null
            ? models.ToList()
            : new List<string>();
        var current = session.CurrentModelId;
        if (!string.IsNullOrWhiteSpace(current)
            && source.All(id => !string.Equals(id, current, StringComparison.OrdinalIgnoreCase))) {
            // 归属判定优先用会话 VendorModelMap（测试/占位场景 configLoader 可能为空）：
            // 当前模型已存在于其他供应商的目录 → 属于旧供应商残留，不追加（防跨供应商污染）
            var ownedByOtherVendor = map.Any(kvp =>
                !string.Equals(kvp.Key, provider, StringComparison.OrdinalIgnoreCase)
                && kvp.Value is not null
                && kvp.Value.Contains(current, StringComparer.OrdinalIgnoreCase));
            var modelProvider = configLoader.FindProviderByModelId(current);
            if (!ownedByOtherVendor && (modelProvider is null || string.Equals(modelProvider, provider, StringComparison.OrdinalIgnoreCase)))
                source.Add(current);
        }
        ModelOptions.Clear();
        foreach (var id in source) {
            var tags = BuildModalityTags(configLoader, provider, id);
            ModelOptions.Add(new ModelOptionItem(id, $"{providerDisplay}:{id}", tags));
        }
        _modelById = ModelOptions.ToLookup(m => m.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>重建连接选项 — 从 VendorModelMap.Keys 填充 ObservableCollection（纯真实供应商，Mock 由独立按钮切换）</summary>
    /// <param name="session">引擎会话门面</param>
    public void RebuildConnectionOptions(IJccChatSession session) {
        ConnectionOptions.Clear();
        foreach (var provider in session.VendorModelMap.Keys) {
            var display = VendorKindExtensions.FromValue(provider)?.ToString() ?? provider;
            ConnectionOptions.Add(new ConnectionOptionItem {
                Id = provider,
                DisplayText = display,
                IsMock = false
            });
        }
        _connectionById = ConnectionOptions.ToLookup(c => c.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>根据模型模态能力生成标签文本（emoji 缩写）</summary>
    private static string BuildModalityTags(IModelConfigLoader configLoader, string provider, string modelId) {
        var modalities = configLoader.GetModalities(provider, modelId);
        if (modalities == ModelModalityKind.None || modalities == ModelModalityKind.Text)
            return "";

        var sb = new StringBuilder();
        if (modalities.HasFlag(ModelModalityKind.ReadImage)) sb.Append("\U0001F4F7");
        if (modalities.HasFlag(ModelModalityKind.ReadGif)) sb.Append("\U0001F3AC");
        if (modalities.HasFlag(ModelModalityKind.ReadVideo)) sb.Append("\U0001F3A5");
        if (modalities.HasFlag(ModelModalityKind.ReadAudio)) sb.Append("\U0001F3A7");
        if (modalities.HasFlag(ModelModalityKind.ReadPdf)) sb.Append("\U0001F4C4");
        if (modalities.HasFlag(ModelModalityKind.GenerateImage)) sb.Append("\U0001F5BC");
        if (modalities.HasFlag(ModelModalityKind.GenerateVideo)) sb.Append("\U0001F3EE");
        if (modalities.HasFlag(ModelModalityKind.GenerateAudio)) sb.Append("\U0001F50A");
        if (modalities.HasFlag(ModelModalityKind.Thinking)) sb.Append("\U0001F9E0");
        if (modalities.HasFlag(ModelModalityKind.CodeExecution)) sb.Append("\U0001F4BB");
        if (modalities.HasFlag(ModelModalityKind.WebSearch)) sb.Append("\U0001F50D");
        if (modalities.HasFlag(ModelModalityKind.ToolUse)) sb.Append("\U0001F527");
        return sb.ToString();
    }
}