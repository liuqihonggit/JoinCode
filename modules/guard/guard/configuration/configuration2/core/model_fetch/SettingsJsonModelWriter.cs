namespace Core.Configuration.ModelFetch;

/// <summary>
/// 供应商模型列表写回器 — 持久化到 providers_llm/{vendor}/models.json（VendorModelsFile 包裹，
/// TASK054 P6 拆分布局），settings.json 零触碰（模型更新不涉及 current 指针 — 用户布局约定
/// "settings.json 只留当前配置"，模型列表内联会膨胀到 29KB+）。
/// 写入用 FileShare.ReadWrite 避免与 jcc 内部并发的文件读取冲突；
/// 写前调用 IConfigChangeNotifier.MarkInternalWrite 防抖，避免自身写入触发循环刷新。
/// </summary>
public sealed class SettingsJsonModelWriter {
    private readonly IFileSystem _fs;
    private readonly IConfigChangeNotifier? _changeNotifier;
    private readonly ILogger<SettingsJsonModelWriter>? _logger;
    private readonly string _baseDir;

    /// <summary>
    /// 构造函数 — 注入文件系统、可选的配置变更通知器、日志器和根目录
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="changeNotifier">配置变更通知器（可选 — MarkInternalWrite 防抖）</param>
    /// <param name="logger">日志器（可选）</param>
    /// <param name="baseDir">jcc 配置根目录（默认 AppDataConstants.Paths.JccDirectory — 测试注入临时目录）</param>
    public SettingsJsonModelWriter(
        IFileSystem fs,
        IConfigChangeNotifier? changeNotifier = null,
        ILogger<SettingsJsonModelWriter>? logger = null,
        string? baseDir = null) {
        _fs = fs;
        _changeNotifier = changeNotifier;
        _logger = logger;
        _baseDir = baseDir ?? AppDataConstants.Paths.JccDirectory;
    }

    /// <summary>
    /// 将合并后的模型列表写回 providers_llm/{vendor}/models.json — 每供应商一个子文件，
    /// settings.json 不动（current 指针与模型列表解耦）
    /// </summary>
    /// <param name="updates">供应商名 → 合并后的模型列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task WriteAsync(
        IReadOnlyDictionary<string, List<ModelItemConfig>> updates,
        CancellationToken cancellationToken = default) {
        if (updates.Count == 0) return;

        var vendorsDir = Path.Combine(_baseDir, "providers_llm");
        DirectoryHelper.EnsureDirectoryExists(_fs, vendorsDir);

        foreach (var (profile, models) in updates) {
            var modelsPath = Path.Combine(vendorsDir, profile, "models.json");
            DirectoryHelper.EnsureDirectoryExists(_fs, Path.GetDirectoryName(modelsPath));
            var modelsJson = RelaxedJsonSerializer.SerializeIndented(new VendorModelsFile { Models = models }, ConfigIndentedJsonContext.Default);
            _changeNotifier?.MarkInternalWrite(modelsPath);
            await WriteWithSharedAccessAsync(modelsPath, modelsJson, cancellationToken).ConfigureAwait(false);
        }

        _logger?.LogInformation("[SettingsJsonModelWriter] 已更新 {Count} 个供应商的模型列表（providers_llm 子文件）", updates.Count);
    }

    /// <summary>
    /// 用 FileShare.ReadWrite 写入 — 允许其他线程同时读取，避免与 jcc 内部并发冲突
    /// </summary>
    private async Task WriteWithSharedAccessAsync(string path, string content, CancellationToken cancellationToken) {
        await using var stream = _fs.CreateStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
