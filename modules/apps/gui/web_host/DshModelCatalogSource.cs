namespace JoinCode.Gui.WebHost;

/// <summary>
/// 模型目录来源（ADR 0143 M2-b2）：把自有引擎的供应商/模型配置投影成 DSH 前端认得的模型目录，
/// 消除「正在加载模型…」长期停留。启动时读 <c>~/.jcc/settings.json</c>（引擎尚未就绪也能出目录），
/// 引擎 attach 后热切换成引擎的运行时真值（<see cref="IJccChatSession.VendorModelMap"/>）。
/// </summary>
internal sealed class DshModelCatalogSource {
    private volatile DshModelCatalog _catalog = new();

    /// <summary>当前模型目录快照（线程安全读取）</summary>
    internal DshModelCatalog Current => _catalog;

    /// <summary>
    /// 从 settings.json 构建模型目录 — 读用户配置的供应商预设与模型清单；
    /// 文件缺失或损坏时退化为空目录（不抛错，前端仍可渲染）。
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="settingsPath">settings.json 路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task LoadAsync(IFileSystem fs, string settingsPath, CancellationToken cancellationToken) {
        if (!fs.FileExists(settingsPath)) {
            _catalog = Build(null, null, null);
            return;
        }
        var json = await fs.ReadAllTextAsync(settingsPath, cancellationToken);
        var settings = RelaxedJsonSerializer.Deserialize<SettingsJson>(json, ConfigJsonContext.Default);
        var profile = Environment.GetEnvironmentVariable(JccEnvVar.Vendor.ToValue());
        if (string.IsNullOrWhiteSpace(profile)) {
            profile = settings?.Current?.Profile;
        }
        var models = ToModelMap(settings);
        var defaultModel = profile is not null && settings?.Vendor.TryGetValue(profile, out var preset) == true
            ? preset.Model
            : null;
        _catalog = Build(profile, defaultModel, models);
    }

    /// <summary>
    /// 热切换成引擎真值 — 供应商、当前模型与各供应商模型清单都来自 <see cref="IJccChatSession"/>。
    /// </summary>
    /// <param name="vendor">引擎当前供应商</param>
    /// <param name="modelId">引擎当前模型</param>
    /// <param name="models">供应商 → 模型清单</param>
    internal void AttachEngine(string? vendor, string? modelId, IReadOnlyDictionary<string, IReadOnlyList<string>>? models) =>
        _catalog = Build(vendor, modelId, models);

    /// <summary>把 settings.json 的 vendor 预设投影成「供应商 → 模型 ID」映射</summary>
    private static Dictionary<string, IReadOnlyList<string>> ToModelMap(SettingsJson? settings) {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (settings is null) {
            return map;
        }
        foreach (var (name, preset) in settings.Vendor) {
            var ids = preset.Models.Select(model => model.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
            if (ids.Length > 0) {
                map[name] = ids;
            }
        }
        return map;
    }

    /// <summary>
    /// 构建模型目录 — 默认选择取当前供应商/模型；分组按供应商排列，模型清单为空的分组不输出，
    /// 但当前供应商即使没有清单也保留分组（否则前端选不中当前模型）。
    /// </summary>
    /// <param name="provider">当前供应商</param>
    /// <param name="modelId">当前模型</param>
    /// <param name="models">供应商 → 模型清单</param>
    internal static DshModelCatalog Build(string? provider, string? modelId, IReadOnlyDictionary<string, IReadOnlyList<string>>? models) {
        var catalog = new DshModelCatalog {
            Default = new DshModelChoice { Provider = provider ?? "", Model = modelId ?? "" },
        };
        var map = models ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (vendor, ids) in map) {
            var items = ids
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => new DshModelItem { Id = id, Name = id })
                .ToList();
            if (items.Count == 0 && !string.Equals(vendor, provider, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            catalog.Groups.Add(new DshModelGroup { Id = vendor, Name = vendor, Models = items });
        }
        var hasCurrent = catalog.Groups.Any(group => string.Equals(group.Id, provider, StringComparison.OrdinalIgnoreCase));
        if (!hasCurrent && !string.IsNullOrWhiteSpace(provider)) {
            catalog.Groups.Add(new DshModelGroup { Id = provider, Name = provider });
        }
        catalog.RoutableProviders.AddRange(catalog.Groups.Select(group => group.Id));
        return catalog;
    }
}
