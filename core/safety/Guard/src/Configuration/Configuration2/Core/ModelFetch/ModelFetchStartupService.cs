namespace Core.Configuration.ModelFetch;

/// <summary>
/// 模型拉取启动服务 — 编排"拉取 → 合并 → 写回"流程
/// 在 EngineSessionFactory 中非阻塞并行调用，失败不影响启动
/// </summary>
public sealed class ModelFetchStartupService
{
    private readonly IModelListFetcher _fetcher;
    private readonly SettingsJsonModelWriter _writer;
    private readonly ILogger<ModelFetchStartupService>? _logger;

    public ModelFetchStartupService(
        IModelListFetcher fetcher,
        SettingsJsonModelWriter writer,
        ILogger<ModelFetchStartupService>? logger = null)
    {
        _fetcher = fetcher;
        _writer = writer;
        _logger = logger;
    }

    /// <summary>
    /// 执行模型列表拉取+合并+写回
    /// <para>1. 检查 autoFetchModels 开关</para>
    /// <para>2. 并行拉取各供应商模型 id 列表</para>
    /// <para>3. 智能合并：以远程 id 为准增删，保留本地元数据</para>
    /// <para>4. 写回 settings.json（触发文件监控自动刷新内存和GUI）</para>
    /// </summary>
    public async Task ExecuteAsync(
        SettingsJson settings,
        CancellationToken cancellationToken = default)
    {
        if (!settings.AutoFetchModels) return;
        if (settings.Vendor is null || settings.Vendor.Count == 0) return;

        try
        {
            _logger?.LogInformation("[ModelFetchStartupService] 开始拉取模型列表");

            var remoteModels = await _fetcher.FetchAllAsync(settings.Vendor, cancellationToken).ConfigureAwait(false);
            if (remoteModels.Count == 0)
            {
                _logger?.LogInformation("[ModelFetchStartupService] 未拉取到任何模型，跳过更新");
                return;
            }

            var updates = new Dictionary<string, List<ModelItemConfig>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (profile, remoteIds) in remoteModels)
            {
                if (!settings.Vendor.TryGetValue(profile, out var profileSettings)) continue;
                var merged = ModelListMerger.Merge(profileSettings.Models, remoteIds);
                updates[profile] = merged;
            }

            if (updates.Count > 0)
                await _writer.WriteAsync(settings, updates, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("[ModelFetchStartupService] 模型列表拉取完成，更新了 {Count} 个供应商", updates.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ModelFetchStartupService] 模型列表拉取失败，不影响启动");
        }
    }
}
