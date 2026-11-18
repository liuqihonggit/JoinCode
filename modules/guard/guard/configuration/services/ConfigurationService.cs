namespace Core.Configuration;

/// <summary>
/// 配置服务实现 - 内存缓存 + 磁盘持久化 + 变更通知
/// 对齐 TS 版 ConfigTool: 双存储源(global/settings) + appStateKey 热更新同步
/// </summary>
[Register(typeof(IConfigurationService), ServiceLifetime.Singleton)]
public sealed partial class ConfigurationService : ServiceEntity, IConfigurationService {
    private ImmutableHamT<string, string> _configurations = ImmutableHamT<string, string>.Empty;
    private readonly IFileSystem _fs;
    private readonly IRemoteSettingsService? _remoteSettingsService;
    private readonly IConfigChangeNotifier? _configChangeNotifier;
    private readonly ILogger<ConfigurationService>? _logger;


    /// <summary>
    /// 设置变更事件 — 当设置被添加、更新或删除时触发
    /// </summary>
    public event EventHandler<SettingChangeEventArgs>? SettingChanged;

    /// <summary>
    /// 构造配置服务
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="remoteSettingsService">远程设置服务(可选),用于读取与合并远程设置</param>
    /// <param name="configChangeNotifier">配置变更通知器(可选),用于标记内部写以避免回声</param>
    /// <param name="logger">日志记录器(可选)</param>
    public ConfigurationService(IFileSystem fs, IRemoteSettingsService? remoteSettingsService = null, IConfigChangeNotifier? configChangeNotifier = null, ILogger<ConfigurationService>? logger = null) {
        _fs = fs;
        _remoteSettingsService = remoteSettingsService;
        _configChangeNotifier = configChangeNotifier;
        _logger = logger;
    }

    /// <summary>
    /// 读取配置项（存储源由 <see cref="ConfigKeyRegistry"/> 决定）。
    /// <para><b>为什么不在这里固定 UserSettings</b>：曾经「无 source 重载」硬编码 settings.json，
    /// 于是 <c>/theme</c>、GUI 读 settings 而 <c>config_get</c> 读 global，同一个键两处答案（TASK089）。
    /// 决策只在服务内部做一次，任何调用方用无 source 重载都不会读错文件。</para>
    /// </summary>
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => GetAsync(key, ConfigKeyRegistry.SourceOf(key), cancellationToken);

    /// <summary>迁移闸门（普通 object 即可，不用可释放类型，避免单例被迫实现 IDisposable）</summary>
    private readonly object _migrationLock = new();

    /// <summary>迁移任务 — 首次访问时创建，之后所有调用方共享同一个结果</summary>
    private Task _migrationTask = Task.CompletedTask;

    /// <summary>迁移是否已启动</summary>
    private bool _migrationStarted;

    /// <summary>
    /// 首次读写前把历史误写在 settings.json 的 global 键搬到 global.json（TASK089 / 方案 B）。
    /// <para>放在服务内部而非各启动入口：CLI、GUI、<c>jcc tool call</c> 三个宿主都会解析同一个单例，
    /// 挂在入口就得写三遍，漏一处就有宿主读到旧位置。</para>
    /// </summary>
    private Task EnsureMigratedAsync() {
        lock (_migrationLock) {
            if (!_migrationStarted) {
                _migrationStarted = true;
                _migrationTask = RunMigrationOnceAsync();
            }
            return _migrationTask;
        }
    }

    /// <summary>
    /// 真正的迁移动作 —— 不接收调用方取消令牌，避免一次提前取消把半成品状态留给下一位调用方。
    /// <para>逐版本执行由 <see cref="ConfigMigrationRunner"/> 负责：读 <c>config-migrations.json</c>
    /// 的已应用版本，只跑落后的那几步，每步成功就落盘。服务只负责「加锁只跑一次」与「标记内部写」。</para>
    /// </summary>
    private async Task RunMigrationOnceAsync() {
        // 迁移自己改文件，先标记内部写，避免 FileSystemWatcher 把它当成外部修改而回灌
        _configChangeNotifier?.MarkInternalWrite(AppDataConstants.Paths.SettingsFilePath);
        _configChangeNotifier?.MarkInternalWrite(AppDataConstants.Paths.GlobalConfigFilePath);

        await ConfigMigrationRunner.RunAsync(_fs, _logger, CancellationToken.None).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> GetAsync(string key, SettingSource source, CancellationToken cancellationToken = default) {
        await EnsureMigratedAsync().ConfigureAwait(false);

        // 1. 先查内存缓存（缓存键含存储源 —— 同一键在 global 与 settings 是两个不同的值，
        //    缓存不区分源会让「先读 A 源再读 B 源」拿到 A 的值，掩盖存储源分叉）
        if (_configurations.TryGetValue(CacheKey(key, source), out var value))
            return value;

        // 2. 按存储源分流读取 — 对齐 TS: global → ~/.claude.json, settings → ~/.claude/settings.json
        try {
            var diskValue = source == SettingSource.GlobalConfig
                ? await ConfigLoader.LoadSettingFromGlobalConfigAsync(key, _fs, cancellationToken, _logger).ConfigureAwait(false)
                : await ConfigLoader.LoadSettingFromSettingsJsonAsync(key, _fs, cancellationToken, _logger).ConfigureAwait(false);

            if (diskValue is not null) {
                while (true) {
                    var current = _configurations;
                    var updated = current.SetItem(CacheKey(key, source), diskValue);
                    if (Interlocked.CompareExchange(ref _configurations, updated, current) == current) break;
                }
                return diskValue;
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "从 {Source} 读取设置失败: {Key}", source.ToValue(), key);
        }

        // 3. 最后查远程设置（仅 settings 源）
        if (source == SettingSource.UserSettings && _remoteSettingsService is not null) {
            try {
                value = await _remoteSettingsService.GetSettingAsync(key, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "获取远程设置失败: {Key}", key);
            }
        }

        return value;
    }

    /// <summary>缓存键 = 存储键 + 存储源 —— 同一键在不同源下是不同的值，不能共用一条缓存。</summary>
    private static string CacheKey(string key, SettingSource source) => $"{source.ToValue()}:{key}";

    /// <inheritdoc/>
    public Task<bool> SetAsync(string key, string value, CancellationToken cancellationToken = default)
        => SetAsync(key, value, ConfigKeyRegistry.SourceOf(key), null, cancellationToken);

    /// <inheritdoc/>
    public Task<bool> SetAsync(string key, string value, SettingSource source, CancellationToken cancellationToken = default)
        => SetAsync(key, value, source, null, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> SetAsync(string key, string value, SettingSource source, string? appStateKey, CancellationToken cancellationToken = default) {
        await EnsureMigratedAsync().ConfigureAwait(false);

        // 获取旧值用于变更通知
        var cacheKey = CacheKey(key, source);
        var oldValue = _configurations.TryGetValue(cacheKey, out var existing) ? existing : null;

        while (true) {
            var current = _configurations;
            var updated = current.SetItem(cacheKey, value);
            if (Interlocked.CompareExchange(ref _configurations, updated, current) == current) break;
        }

        // 按存储源分流持久化 — 对齐 TS: global → saveGlobalConfig, settings → updateSettingsForSource
        try {
            // 对齐 TS markInternalWrite — 写入前标记内部写，防止 FileSystemWatcher 回声
            // 路径委托 AppDataPaths 唯一数据源（JCC_APP_DATA_FOLDER 覆盖行为与主链路一致）
            var targetPath = source == SettingSource.GlobalConfig
                ? AppDataConstants.Paths.GlobalConfigFilePath
                : AppDataConstants.Paths.SettingsFilePath;
            _configChangeNotifier?.MarkInternalWrite(targetPath);

            if (source == SettingSource.GlobalConfig) {
                await ConfigLoader.SaveSettingToGlobalConfigAsync(key, value, _fs, cancellationToken, _logger).ConfigureAwait(false);
            } else {
                await ConfigLoader.SaveSettingToSettingsJsonAsync(key, value, _fs, cancellationToken, _logger).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "持久化设置到 {Source} 失败: {Key}", source.ToValue(), key);
            Diag.WriteLine($"[CONFIG] 持久化失败 {source.ToValue()}/{key}: {ex.GetType().Name}: {ex.Message}");
            // 原先无论如何都返回 true —— 磁盘写失败（含「路径无法解析，什么都没写」）也报成功，
            // 用户以为设置生效、实际下次启动还是旧值（TASK090 的 model 就是这样被吞掉的）
            return false;
        }

        // 触发变更通知（对齐 TS 版 appStateKey 热更新同步）
        OnSettingChanged(key, oldValue, value, source, appStateKey);

        return true;
    }

    /// <inheritdoc/>
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
        => RemoveAsync(key, ConfigKeyRegistry.SourceOf(key), cancellationToken);

    /// <summary>
    /// 删除配置项（指定存储源）— 对齐 TS「写到哪个源就删哪个源」。
    /// 不放进接口：对外只需要「删这个键」，源由唯一表决定，多一个入口就多一次判错的机会。
    /// </summary>
    public async Task<bool> RemoveAsync(string key, SettingSource source, CancellationToken cancellationToken = default) {
        await EnsureMigratedAsync().ConfigureAwait(false);

        var cacheKey = CacheKey(key, source);
        var oldValue = _configurations.TryGetValue(cacheKey, out var existing) ? existing : null;

        while (true) {
            var current = _configurations;
            if (!current.ContainsKey(cacheKey)) break;
            var updated = current.Remove(cacheKey);
            if (Interlocked.CompareExchange(ref _configurations, updated, current) == current) break;
        }

        try {
            // 对齐 TS markInternalWrite — 写入前标记内部写（路径委托 AppDataPaths 唯一数据源）
            // 删除必须落在该键真正所在的文件 —— 旧实现硬编码 settings.json，
            // 于是 /config remove <global 键> 会删错文件（TASK089）
            var targetPath = source == SettingSource.GlobalConfig
                ? AppDataConstants.Paths.GlobalConfigFilePath
                : AppDataConstants.Paths.SettingsFilePath;
            _configChangeNotifier?.MarkInternalWrite(targetPath);

            if (source == SettingSource.GlobalConfig) {
                await ConfigLoader.SaveSettingToGlobalConfigAsync(key, null, _fs, cancellationToken, _logger).ConfigureAwait(false);
            } else {
                await ConfigLoader.SaveSettingToSettingsJsonAsync(key, null, _fs, cancellationToken, _logger).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "从 {Source} 删除设置失败: {Key}", source.ToValue(), key);
        }

        // 触发变更通知
        OnSettingChanged(key, oldValue, null, source);

        return true;
    }

    /// <summary>
    /// 列出「已声明且有值」的配置项。
    /// <para><b>为什么逐键读盘而不是回内存缓存</b>：缓存只装本进程读过的键，新启动一个进程
    /// <c>/config list</c> 永远是空的（磁盘上明明有值）。旧实现只回缓存，用户看到「当前没有配置项」
    /// 会以为配置丢了。逐键按 <see cref="ConfigKeyRegistry"/> 声明的源去读，与
    /// <c>config_get</c>/<c>/config get</c> 走同一条路径，三处答案自然一致。</para>
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default) {
        await EnsureMigratedAsync().ConfigureAwait(false);

        var localConfigurations = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, source) in ConfigKeyRegistry.All.Select(kv => (kv.Key, kv.Value.Source))) {
            var value = await GetAsync(key, source, cancellationToken).ConfigureAwait(false);
            if (value is not null)
                localConfigurations[key] = value;
        }

        if (_remoteSettingsService != null) {
            try {
                var merged = await _remoteSettingsService.GetMergedSettingsAsync(localConfigurations, cancellationToken).ConfigureAwait(false);
                foreach (var kvp in merged) {
                    if (!localConfigurations.ContainsKey(kvp.Key)) {
                        localConfigurations[kvp.Key] = kvp.Value;
                    }
                }
            } catch (Exception ex) {
                _logger?.LogWarning(ex, "合并远程设置失败");
            }
        }

        return localConfigurations;
    }

    private void OnSettingChanged(string key, string? oldValue, string? newValue, SettingSource source = SettingSource.UserSettings, string? appStateKey = null) {
        try {
            SettingChanged?.Invoke(this, new SettingChangeEventArgs {
                Key = key,
                OldValue = oldValue,
                NewValue = newValue,
                Source = source,
                AppStateKey = appStateKey
            });
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "触发设置变更通知失败: {Key}", key);
        }
    }
}