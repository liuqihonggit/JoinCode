namespace Core.Configuration.ModelFetch;

/// <summary>
/// 模型拉取启动服务 — 编排"拉取 → 合并 → 写回"流程
/// 在 EngineSessionFactory 中非阻塞并行调用，失败不影响启动
/// </summary>
public sealed class ModelFetchStartupService
{
    private readonly IModelListFetcher _fetcher;
    private readonly SettingsJsonModelWriter _writer;
    private readonly ISettingsChangeApplier? _settingsChangeApplier;
    private readonly ILogger<ModelFetchStartupService>? _logger;

    public ModelFetchStartupService(
        IModelListFetcher fetcher,
        SettingsJsonModelWriter writer,
        ISettingsChangeApplier? settingsChangeApplier = null,
        ILogger<ModelFetchStartupService>? logger = null)
    {
        _fetcher = fetcher;
        _writer = writer;
        _settingsChangeApplier = settingsChangeApplier;
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
                await WriteWithRetryAsync(settings, updates, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("[ModelFetchStartupService] 模型列表拉取完成，更新了 {Count} 个供应商", updates.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ModelFetchStartupService] 模型列表拉取失败，不影响启动");
        }
    }

    /// <summary>
    /// 写回 settings.json — 对文件锁异常重试一次，提供简洁错误信息
    /// </summary>
    private async Task WriteWithRetryAsync(
        SettingsJson settings,
        IReadOnlyDictionary<string, List<ModelItemConfig>> updates,
        CancellationToken cancellationToken)
    {
        try
        {
            await _writer.WriteAsync(settings, updates, cancellationToken).ConfigureAwait(false);
            await RefreshInMemoryConfigAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            _logger?.LogWarning("[ModelFetchStartupService] settings.json 写入被拒（文件锁），500ms 后重试一次");
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            try
            {
                await _writer.WriteAsync(settings, updates, cancellationToken).ConfigureAwait(false);
                await RefreshInMemoryConfigAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                var path = SettingsLoader.GetUserSettingsPath();
                _logger?.LogWarning("[ModelFetchStartupService] settings.json 写入仍被拒，跳过本次更新 | 路径: {Path} | 不影响启动", path);
            }
        }
    }

    /// <summary>
    /// 刷新内存配置 — MarkInternalWrite 抑制了文件监听事件，需显式触发 SettingsChangeApplier 重载
    /// 否则 ModelConfigLoader 内存缓存不更新，导致模态校验误报
    /// </summary>
    private async Task RefreshInMemoryConfigAsync(CancellationToken cancellationToken)
    {
        if (_settingsChangeApplier is null) return;
        try
        {
            await _settingsChangeApplier.ApplySettingsChangeAsync(cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("[ModelFetchStartupService] 内存配置已刷新");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[ModelFetchStartupService] 内存配置刷新失败，不影响启动");
        }
    }
}
