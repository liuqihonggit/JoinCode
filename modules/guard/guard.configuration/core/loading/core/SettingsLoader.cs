namespace Core.Configuration;

/// <summary>
/// 多源配置加载器 — 对齐 TS 版 settings.ts 的 loadSettingsFromDisk
/// 5 层配置来源（优先级从低到高）:
///   UserSettings → ProjectSettings → LocalSettings → FlagSettings → PolicySettings
/// 合并策略: 从低优先级到高优先级依次 Merge，后者覆盖前者
/// </summary>
public static class SettingsLoader {
    /// <summary>
    /// 从所有来源加载并合并配置
    /// </summary>
    public static async Task<SettingsJson> LoadAllSourcesAsync(
        IFileSystem fs,
        string? projectDir = null,
        string? flagSettingsPath = null,
        CancellationToken cancellationToken = default,
        ILogger? logger = null) {
        SettingsJson? merged = null;

        // 按优先级从低到高依次加载并合并
        var sources = new (SettingSource Source, Func<Task<SettingsJson?>> Loader)[]
        {
            (SettingSource.UserSettings, () => LoadUserSettingsAsync(fs, cancellationToken, logger)),
            (SettingSource.ProjectSettings, () => LoadProjectSettingsAsync(fs, projectDir, cancellationToken, logger)),
            (SettingSource.LocalSettings, () => LoadLocalSettingsAsync(fs, projectDir, cancellationToken, logger)),
            (SettingSource.FlagSettings, () => LoadFlagSettingsAsync(fs, flagSettingsPath, cancellationToken, logger)),
            (SettingSource.PolicySettings, () => LoadPolicySettingsAsync(fs, cancellationToken, logger)),
        };

        // 并行加载所有源（独立文件 I/O 并行），然后按优先级顺序合并（单线程，保证覆盖语义正确）
        var loadTasks = sources.Select(async s => {
            SettingsJson? result = null;
            try {
                result = await s.Loader().ConfigureAwait(false);
            } catch (Exception ex) {
                // 单个来源加载失败不影响其他来源
                logger?.LogWarning(ex, "Failed to load settings from source");
            }
            return (s.Source, result);
        }).ToArray();

        await Task.WhenAll(loadTasks).ConfigureAwait(false);

        foreach (var (_, settings) in loadTasks.Select(t => t.Result)) {
            if (settings is not null) {
                merged = SettingsMapper.Merge(merged, settings);
            }
        }

        return merged ?? new SettingsJson();
    }

    /// <summary>
    /// 构建极简 settings.json 骨架 — 不包含供应商列表，供应商配置由 Hub 服务端管理
    /// </summary>
    public static string BuildDefaultSettingsJson() {
        return """
        {
          "autoFetchModels": true,
          "current": {
            "profile": "test-provider",
            "actor": {
              "buildQueue": {
                "mode": "serial",
                "workerCount": 2
              },
              "backpressure": {
                "codingAgentTask": { "capacity": 2000, "sendTimeoutSeconds": 30 },
                "llmGateway": { "capacity": 200, "sendTimeoutSeconds": 60 },
                "router": { "capacity": 1000, "sendTimeoutSeconds": 10 },
                "build": { "capacity": 100, "sendTimeoutSeconds": 60 }
              }
            },
            "subAgentLiveness": {
              "agentTimeoutSeconds": 300,
              "idleThresholdSeconds": 30,
              "completionCheckThreshold": 0.8,
              "confirmationWindowSeconds": 5,
              "scanIntervalSeconds": 10,
              "chainStallThreshold": 3,
              "poolMaxSize": 8,
              "poolIdleTimeoutSeconds": 300,
              "preemptMinWindowRatio": 0.2,
              "activationRecoverySeconds": 10
            }
          }
        }
        """;
    }

    /// <summary>
    /// 从指定目录加载 settings.json + 合并 providers_llm/current 子文件 — P8 roundtrip 测试用。
    /// <para>读取 {baseDir}/settings.json，然后合并 {baseDir}/providers_llm/ 和 {baseDir}/current/ 子文件。</para>
    /// </summary>
    public static async Task<SettingsJson?> LoadUserSettingsFromDirAsync(
        IFileSystem fs, string baseDir, CancellationToken cancellationToken = default) {
        var settingsPath = Path.Combine(baseDir, "settings.json");
        if (!fs.FileExists(settingsPath)) return null;
        var json = await fs.ReadAllTextAsync(settingsPath, cancellationToken).ConfigureAwait(false);
        var settings = RelaxedJsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        if (settings is null) return null;
        return await MergeAllSubConfigsAsync(fs, settings, baseDir, cancellationToken, null).ConfigureAwait(false);
    }

    /// <summary>
    /// 生成默认配置树骨架到指定目录 — Hub 模式下极简骨架。
    /// <para>生成结构:</para>
    /// <list type="bullet">
    /// <item>settings.json — 极简主入口（current.profile 指针 + autoFetchModels）</item>
    /// <item>current/ — 子文件（subagent.json 等 UI 偏好配置）</item>
    /// </list>
    /// <para>供应商配置由 Hub 服务端管理，不在本地生成 providers_llm/ 和 providers_search/ 目录。</para>
    /// </summary>
    /// <param name="fs">文件系统</param>
    /// <param name="baseDir">jcc 基础目录（如 ~/.jcc/ 或 {projectDir}/.jcc/）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task BuildDefaultConfigTreeAsync(
        IFileSystem fs, string baseDir, CancellationToken cancellationToken = default) {
        DirectoryHelper.EnsureDirectoryExists(fs, baseDir);

        var fullJson = BuildDefaultSettingsJson();
        var fullSettings = RelaxedJsonSerializer.Deserialize(fullJson, ConfigJsonContext.Default.SettingsJson)
            ?? new SettingsJson();

        await WriteMinimalSettingsJsonAsync(fs, baseDir, fullSettings, cancellationToken).ConfigureAwait(false);
        await WriteCurrentSubFilesAsync(fs, baseDir, fullSettings, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteMinimalSettingsJsonAsync(
        IFileSystem fs, string baseDir, SettingsJson fullSettings, CancellationToken ct) {
        var minimal = new MinimalSettingsJson {
            Current = new MinimalCurrentSettings { Profile = fullSettings.Current?.Profile },
            AutoFetchModels = fullSettings.AutoFetchModels,
            Update = fullSettings.Update,
        };
        var json = RelaxedJsonSerializer.SerializeIndented(minimal, ConfigIndentedJsonContext.Default);
        await fs.WriteAllTextAsync(Path.Combine(baseDir, "settings.json"), json, ct).ConfigureAwait(false);
    }

    private static async Task WriteCurrentSubFilesAsync(
        IFileSystem fs, string baseDir, SettingsJson fullSettings, CancellationToken ct) {
        var currentDir = Path.Combine(baseDir, "current");
        DirectoryHelper.EnsureDirectoryExists(fs, currentDir);

        foreach (var fileName in CurrentSubFiles) {
            var partial = ExtractCurrentPartial(fileName, fullSettings);
            var json = RelaxedJsonSerializer.SerializeIndented(partial, ConfigIndentedJsonContext.Default);
            await fs.WriteAllTextAsync(Path.Combine(currentDir, fileName), json, ct).ConfigureAwait(false);
        }
    }

    private static CurrentSettings ExtractCurrentPartial(string fileName, SettingsJson fullSettings) {
        var current = fullSettings.Current;
        return fileName switch {
            "subagent.json" => new CurrentSettings {
                Actor = current?.Actor,
                SubAgentLiveness = current?.SubAgentLiveness,
                SubAgentConcurrency = current?.SubAgentConcurrency,
            },
            _ => new CurrentSettings(),
        };
    }

    /// <summary>
    /// 加载用户全局设置: ~/.jcc/settings.json
    /// 文件不存在或解析失败时返回 null（Hub 模式下配置由 Hub 服务端管理）
    /// </summary>
    public static async Task<SettingsJson?> LoadUserSettingsAsync(IFileSystem fs, CancellationToken cancellationToken = default, ILogger? logger = null) {
        var path = GetUserSettingsPath();
        var result = await LoadSettingsFileAsync(fs, path, cancellationToken, logger).ConfigureAwait(false);
        if (result is not null)
            return await MergeAllSubConfigsAsync(fs, result, AppDataConstants.Paths.JccDirectory, cancellationToken, logger).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// 异步加载用户全局设置 — IFileSystem 异步化后原同步入口改为 async
    /// 文件不存在或解析失败时返回 null（Hub 模式下配置由 Hub 服务端管理）
    /// </summary>
    public static async Task<SettingsJson?> LoadUserSettings(IFileSystem fs) {
        var path = GetUserSettingsPath();
        var result = await LoadSettingsFileSync(fs, path).ConfigureAwait(false);
        if (result is not null)
            return await MergeAllSubConfigsAsync(fs, result, AppDataConstants.Paths.JccDirectory, CancellationToken.None, null).ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// 加载项目共享设置: .jcc/settings.json
    /// </summary>
    public static async Task<SettingsJson?> LoadProjectSettingsAsync(IFileSystem fs, string? projectDir, CancellationToken cancellationToken = default, ILogger? logger = null) {
        if (string.IsNullOrEmpty(projectDir)) return null;
        var path = GetProjectSettingsPath(projectDir);
        return await LoadSettingsFileAsync(fs, path, cancellationToken, logger).ConfigureAwait(false);
    }

    /// <summary>
    /// 加载项目本地设置: .jcc/settings.local.json
    /// </summary>
    public static async Task<SettingsJson?> LoadLocalSettingsAsync(IFileSystem fs, string? projectDir, CancellationToken cancellationToken = default, ILogger? logger = null) {
        if (string.IsNullOrEmpty(projectDir)) return null;
        var path = GetLocalSettingsPath(projectDir);
        return await LoadSettingsFileAsync(fs, path, cancellationToken, logger).ConfigureAwait(false);
    }

    /// <summary>
    /// 加载 CLI 标志设置: --settings 参数指定的路径
    /// </summary>
    public static async Task<SettingsJson?> LoadFlagSettingsAsync(IFileSystem fs, string? flagSettingsPath, CancellationToken cancellationToken = default, ILogger? logger = null) {
        if (string.IsNullOrEmpty(flagSettingsPath)) return null;
        return await LoadSettingsFileAsync(fs, flagSettingsPath, cancellationToken, logger).ConfigureAwait(false);
    }

    /// <summary>
    /// 加载策略设置: managed-settings.json（管理员强制）
    /// 对齐 TS 版: policySettings 内部 "first source wins" 策略
    /// </summary>
    public static async Task<SettingsJson?> LoadPolicySettingsAsync(IFileSystem fs, CancellationToken cancellationToken = default, ILogger? logger = null) {
        var path = GetManagedSettingsPath();
        return await LoadSettingsFileAsync(fs, path, cancellationToken, logger).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 {baseDir}/providers_llm/ 子文件夹加载供应商配置 — TASK054 P6 物理拆分。
    /// <para>每个供应商 {name}/config.json 含 ProfileSettings（除 models），{name}/models.json 含模型列表。</para>
    /// <para>子文件不存在时跳过（约定大于配置，渐进式迁移）。</para>
    /// </summary>
    /// <param name="fs">文件系统</param>
    /// <param name="baseDir">jcc 基础目录（如 ~/.jcc/）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="logger">可选日志</param>
    /// <returns>供应商名 → ProfileSettings 映射（空字典表示无子文件）</returns>
    public static async Task<Dictionary<string, ProfileSettings>> LoadVendorSubConfigsAsync(
        IFileSystem fs, string baseDir, CancellationToken cancellationToken = default, ILogger? logger = null) {
        var result = new Dictionary<string, ProfileSettings>(StringComparer.OrdinalIgnoreCase);
        var vendorsDir = Path.Combine(baseDir, "providers_llm");
        if (!fs.DirectoryExists(vendorsDir))
            return result;

        foreach (var vendorDir in fs.EnumerateDirectories(vendorsDir, "*", SearchOption.TopDirectoryOnly)) {
            var vendorName = Path.GetFileName(vendorDir);
            var configPath = Path.Combine(vendorDir, "config.json");
            var modelsPath = Path.Combine(vendorDir, "models.json");

            ProfileSettings? profile = null;
            if (fs.FileExists(configPath)) {
                try {
                    var configJson = await fs.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
                    profile = RelaxedJsonSerializer.Deserialize(configJson, ConfigJsonContext.Default.ProfileSettings);
                } catch (Exception ex) {
                    logger?.LogWarning(ex, "Failed to load vendor config: {Path}", configPath);
                }
            }

            if (profile is null)
                continue;

            if (profile.Models is null)
                profile = WithModels(profile, []);

            if (fs.FileExists(modelsPath)) {
                try {
                    var modelsJson = await fs.ReadAllTextAsync(modelsPath, cancellationToken).ConfigureAwait(false);
                    var modelsFile = RelaxedJsonSerializer.Deserialize(modelsJson, ConfigJsonContext.Default.VendorModelsFile);
                    if (modelsFile is not null)
                        profile = WithModels(profile, modelsFile.Models);
                } catch (Exception ex) {
                    logger?.LogWarning(ex, "Failed to load vendor models: {Path}", modelsPath);
                }
            }

            result[vendorName] = profile;
        }
        return result;
    }

    private static ProfileSettings WithModels(ProfileSettings profile, List<ModelItemConfig> models)
        => new() {
            Provider = profile.Provider,
            Protocol = profile.Protocol,
            Model = profile.Model,
            Endpoint = profile.Endpoint,
            ApiKeyEnvVar = profile.ApiKeyEnvVar,
            Models = models,
            ModelsEndpoint = profile.ModelsEndpoint,
        };

    /// <summary>
    /// 合并 providers_llm/ 子文件夹配置到 settings.json 内联配置 — 子文件覆盖内联（更高优先级）。
    /// </summary>
    private static async Task<SettingsJson?> MergeVendorSubConfigsAsync(
        IFileSystem fs, SettingsJson? settings, string baseDir,
        CancellationToken cancellationToken, ILogger? logger) {
        if (settings is null) return null;
        var subConfigs = await LoadVendorSubConfigsAsync(fs, baseDir, cancellationToken, logger).ConfigureAwait(false);
        if (subConfigs.Count == 0) return settings;
        return SettingsJson.Merge(settings, new SettingsJson { Vendor = subConfigs });
    }

    /// <summary>
    /// current/ 子文件名清单 — CurrentSettings ~45 字段按职责拆成 10 个子文件（TASK054 P7 物理拆分）。
    /// </summary>
    private static readonly string[] CurrentSubFiles = [
        "ui.json", "inference.json", "behavior.json", "security.json",
        "mcp.json", "plugins.json", "subagent.json", "tools.json",
        "worktree.json", "runtime.json"
    ];

    /// <summary>
    /// 从 {baseDir}/current/*.json 加载 CurrentSettings 子文件 — TASK054 P7 物理拆分。
    /// <para>每个子文件含 CurrentSettings 的部分字段，用 CurrentSettings.Merge 逐个合并成完整 DTO。</para>
    /// <para>子文件不存在时跳过（约定大于配置，渐进式迁移）。</para>
    /// </summary>
    /// <param name="fs">文件系统</param>
    /// <param name="baseDir">jcc 基础目录（如 ~/.jcc/）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="logger">可选日志</param>
    /// <returns>合并后的 CurrentSettings（null 表示无子文件）</returns>
    public static async Task<CurrentSettings?> LoadCurrentSubConfigsAsync(
        IFileSystem fs, string baseDir, CancellationToken cancellationToken = default, ILogger? logger = null) {
        var currentDir = Path.Combine(baseDir, "current");
        if (!fs.DirectoryExists(currentDir))
            return null;

        CurrentSettings? merged = null;
        foreach (var fileName in CurrentSubFiles) {
            var path = Path.Combine(currentDir, fileName);
            if (!fs.FileExists(path)) continue;
            try {
                var json = await fs.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                var partial = RelaxedJsonSerializer.Deserialize(json, ConfigJsonContext.Default.CurrentSettings);
                if (partial is not null)
                    merged = CurrentSettings.Merge(merged, partial);
            } catch (Exception ex) {
                logger?.LogWarning(ex, "Failed to load current sub-config: {Path}", path);
            }
        }
        return merged;
    }

    /// <summary>
    /// 合并 current/ 子文件配置到 settings.json 内联配置 — 子文件覆盖内联（更高优先级）。
    /// </summary>
    private static async Task<SettingsJson?> MergeCurrentSubConfigsAsync(
        IFileSystem fs, SettingsJson? settings, string baseDir,
        CancellationToken cancellationToken, ILogger? logger) {
        if (settings is null) return null;
        var subCurrent = await LoadCurrentSubConfigsAsync(fs, baseDir, cancellationToken, logger).ConfigureAwait(false);
        if (subCurrent is null) return settings;
        var mergedCurrent = CurrentSettings.Merge(settings.Current, subCurrent);
        return new SettingsJson {
            Vendor = settings.Vendor,
            Current = mergedCurrent,
            AutoFetchModels = settings.AutoFetchModels,
            Update = settings.Update,
            SearchProviders = settings.SearchProviders,
        };
    }

    /// <summary>
    /// 合并 providers_llm/ + current/ + providers_search/ 子文件配置到 settings.json 内联 — 统一入口。
    /// </summary>
    private static async Task<SettingsJson?> MergeAllSubConfigsAsync(
        IFileSystem fs, SettingsJson? settings, string baseDir,
        CancellationToken cancellationToken, ILogger? logger) {
        settings = await MergeVendorSubConfigsAsync(fs, settings, baseDir, cancellationToken, logger).ConfigureAwait(false);
        settings = await MergeCurrentSubConfigsAsync(fs, settings, baseDir, cancellationToken, logger).ConfigureAwait(false);
        settings = await MergeSearchProviderSubConfigsAsync(fs, settings, baseDir, cancellationToken, logger).ConfigureAwait(false);
        return settings;
    }

    /// <summary>
    /// 从 {baseDir}/providers_search/ 子文件夹加载搜索供应商配置 — TASK054 P8 物理拆分。
    /// <para>每个供应商 {name}/config.json 含 SearchProviderProfileSettings。</para>
    /// <para>子文件不存在时跳过（约定大于配置，渐进式迁移）。</para>
    /// </summary>
    public static async Task<Dictionary<string, SearchProviderProfileSettings>> LoadSearchProviderSubConfigsAsync(
        IFileSystem fs, string baseDir, CancellationToken cancellationToken = default, ILogger? logger = null) {
        var result = new Dictionary<string, SearchProviderProfileSettings>(StringComparer.OrdinalIgnoreCase);
        var searchDir = Path.Combine(baseDir, "providers_search");
        if (!fs.DirectoryExists(searchDir))
            return result;

        foreach (var providerDir in fs.EnumerateDirectories(searchDir, "*", SearchOption.TopDirectoryOnly)) {
            var providerName = Path.GetFileName(providerDir);
            var configPath = Path.Combine(providerDir, "config.json");
            if (!fs.FileExists(configPath)) continue;
            try {
                var configJson = await fs.ReadAllTextAsync(configPath, cancellationToken).ConfigureAwait(false);
                var sp = RelaxedJsonSerializer.Deserialize(configJson, ConfigJsonContext.Default.SearchProviderProfileSettings);
                if (sp is not null)
                    result[providerName] = sp;
            } catch (Exception ex) {
                logger?.LogWarning(ex, "Failed to load search provider config: {Path}", configPath);
            }
        }
        return result;
    }

    /// <summary>
    /// 合并 providers_search/ 子文件夹配置到 settings.json 内联配置 — 子文件覆盖内联（更高优先级）。
    /// </summary>
    private static async Task<SettingsJson?> MergeSearchProviderSubConfigsAsync(
        IFileSystem fs, SettingsJson? settings, string baseDir,
        CancellationToken cancellationToken, ILogger? logger) {
        if (settings is null) return null;
        var subConfigs = await LoadSearchProviderSubConfigsAsync(fs, baseDir, cancellationToken, logger).ConfigureAwait(false);
        if (subConfigs.Count == 0) return settings;
        return SettingsJson.Merge(settings, new SettingsJson { SearchProviders = subConfigs });
    }

    /// <summary>
    /// 保存设置到指定来源
    /// </summary>
    public static async Task SaveSettingsAsync(
        IFileSystem fs,
        SettingSource source,
        SettingsJson settings,
        string? projectDir = null,
        CancellationToken cancellationToken = default) {
        var path = source switch {
            SettingSource.UserSettings => GetUserSettingsPath(),
            SettingSource.ProjectSettings => GetProjectSettingsPath(projectDir ?? fs.GetCurrentDirectory()),
            SettingSource.LocalSettings => GetLocalSettingsPath(projectDir ?? fs.GetCurrentDirectory()),
            _ => throw new ArgumentException($"[GRD007] 不支持保存到来源: {source}"),
        };

        var directory = Path.GetDirectoryName(path);
        DirectoryHelper.EnsureDirectoryExists(fs, directory);

        var json = RelaxedJsonSerializer.SerializeIndented(settings, ConfigIndentedJsonContext.Default);
        await fs.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 写回单个供应商的 <c>providers_llm/{vendor}/config.json</c> — 树状布局下单点落盘入口
    /// （模型列表由 SettingsJsonModelWriter 单独写 models.json）。
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="baseDir">jcc 基础目录（<c>~/.jcc/</c>）</param>
    /// <param name="vendor">供应商键（providers_llm 子目录名）</param>
    /// <param name="config">供应商连接配置（models 由 models.json 承载）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task SaveVendorConfigAsync(
        IFileSystem fs,
        string baseDir,
        string vendor,
        VendorConfigFile config,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendor);
        var vendorDir = Path.Combine(baseDir, "providers_llm", vendor);
        DirectoryHelper.EnsureDirectoryExists(fs, vendorDir);
        var json = RelaxedJsonSerializer.SerializeIndented(config, ConfigIndentedJsonContext.Default);
        await fs.WriteAllTextAsync(Path.Combine(vendorDir, "config.json"), json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 写回单个供应商的 <c>providers_llm/{vendor}/models.json</c> — 与 <see cref="SaveVendorConfigAsync"/>
    /// 配对使用（配置文件只含连接字段，模型列表单独落盘）。
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="baseDir">jcc 基础目录（<c>~/.jcc/</c>）</param>
    /// <param name="vendor">供应商键（providers_llm 子目录名）</param>
    /// <param name="models">模型列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task SaveVendorModelsAsync(
        IFileSystem fs,
        string baseDir,
        string vendor,
        IReadOnlyList<ModelItemConfig> models,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendor);
        var vendorDir = Path.Combine(baseDir, "providers_llm", vendor);
        DirectoryHelper.EnsureDirectoryExists(fs, vendorDir);
        var json = RelaxedJsonSerializer.SerializeIndented(
            new VendorModelsFile { Models = [.. models] },
            ConfigIndentedJsonContext.Default);
        await fs.WriteAllTextAsync(Path.Combine(vendorDir, "models.json"), json, cancellationToken).ConfigureAwait(false);
    }

    #region 路径解析

    /// <summary>
    /// 获取用户全局设置路径
    /// 优先使用 JCC_APP_DATA_FOLDER 环境变量覆盖(测试隔离场景);
    /// AppDataFolder 为绝对路径时直接使用;
    /// 否则拼接 {UserProfile}/{AppDataFolder}/{SettingsFileName}
    /// </summary>
    public static string GetUserSettingsPath() {
        return Path.Combine(AppDataConstants.Paths.JccDirectory, AppDataConstants.SettingsFileName);
    }

    /// <summary>
    /// 获取项目共享设置路径: {projectDir}/.jcc/{SettingsFileName}
    /// 项目级目录始终使用相对目录名，当 AppDataFolder 为绝对路径（测试隔离）时回退到 .jcc
    /// 文件名与用户级同源（JCC_SETTINGS_FILE_NAME 三层统一生效）
    /// </summary>
    public static string GetProjectSettingsPath(string projectDir) {
        var folderName = Path.IsPathRooted(AppDataConstants.AppDataFolder) ? ".jcc" : AppDataConstants.AppDataFolder;
        return Path.Combine(projectDir, folderName, AppDataConstants.SettingsFileName);
    }

    /// <summary>
    /// 获取项目本地设置路径: {projectDir}/.jcc/{LocalSettingsFileName}
    /// 本地文件名由 SettingsFileName 派生（settings.json → settings.local.json）
    /// </summary>
    public static string GetLocalSettingsPath(string projectDir) {
        var folderName = Path.IsPathRooted(AppDataConstants.AppDataFolder) ? ".jcc" : AppDataConstants.AppDataFolder;
        return Path.Combine(projectDir, folderName, AppDataConstants.Paths.LocalSettingsFileName);
    }

    /// <summary>
    /// 获取策略设置路径（管理员强制）— 对齐 TS managedPath.ts 分平台安全目录：
    /// Windows C:\Program Files\jcc\（写需管理员，防普通用户进程伪造策略）、
    /// macOS /Library/Application Support/jcc/、Linux /etc/jcc/。
    /// AppDataFolder 为绝对路径时透传（测试隔离约定，与三层 settings 解析一致）。
    /// </summary>
    public static string GetManagedSettingsPath() {
        if (Path.IsPathRooted(AppDataConstants.AppDataFolder))
            return Path.Combine(AppDataConstants.AppDataFolder, "managed-settings.json");

        var managedDir = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), BrandConstants.CliCommandName)
            : OperatingSystem.IsMacOS()
                ? $"/Library/Application Support/{BrandConstants.CliCommandName}"
                : $"/etc/{BrandConstants.CliCommandName}";
        return Path.Combine(managedDir, "managed-settings.json");
    }

    #endregion

    #region 内部方法

    private static async Task<SettingsJson?> LoadSettingsFileAsync(IFileSystem fs, string path, CancellationToken cancellationToken, ILogger? logger = null) {
        if (!fs.FileExists(path)) {
            logger?.LogDebug("SL FileExists=False: {Path}", path);
            return null;
        }

        var result = await DirtyReadRetry.ReadWithRetryAsync(
            () => fs.ReadAllTextAsync(path, cancellationToken),
            json => RelaxedJsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson),
            path,
            logger,
            cancellationToken).ConfigureAwait(false);
        logger?.LogDebug("SL OK: {Path} profile={Profile}", path, result?.Current?.Profile);
        return result;
    }

    /// <summary>
    /// 异步加载设置文件 — IFileSystem 异步化后原同步入口改为 async
    /// </summary>
    private static async Task<SettingsJson?> LoadSettingsFileSync(IFileSystem fs, string path, ILogger? logger = null) {
        if (!fs.FileExists(path))
            return null;

        return await DirtyReadRetry.ReadWithRetryAsync(
            () => fs.ReadAllText(path).AsTask(),
            json => RelaxedJsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson),
            path,
            logger).ConfigureAwait(false);
    }

    #endregion
}