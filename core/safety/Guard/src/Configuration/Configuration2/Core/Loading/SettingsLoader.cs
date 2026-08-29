namespace Core.Configuration;

/// <summary>
/// 多源配置加载器 — 对齐 TS 版 settings.ts 的 loadSettingsFromDisk
/// 5 层配置来源（优先级从低到高）:
///   UserSettings → ProjectSettings → LocalSettings → FlagSettings → PolicySettings
/// 合并策略: 从低优先级到高优先级依次 Merge，后者覆盖前者
/// </summary>
public static class SettingsLoader
{
    /// <summary>
    /// 从所有来源加载并合并配置
    /// </summary>
    public static async Task<SettingsJson> LoadAllSourcesAsync(
        IFileSystem fs,
        string? projectDir = null,
        string? flagSettingsPath = null,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        SettingsJson? merged = null;

        // 按优先级从低到高依次加载并合并
        var sources = new (SettingSource Source, Func<Task<SettingsJson?>> Loader)[]
        {
            (SettingSource.UserSettings, () => LoadUserSettingsAsync(fs, cancellationToken)),
            (SettingSource.ProjectSettings, () => LoadProjectSettingsAsync(fs, projectDir, cancellationToken)),
            (SettingSource.LocalSettings, () => LoadLocalSettingsAsync(fs, projectDir, cancellationToken)),
            (SettingSource.FlagSettings, () => LoadFlagSettingsAsync(fs, flagSettingsPath, cancellationToken)),
            (SettingSource.PolicySettings, () => LoadPolicySettingsAsync(fs, cancellationToken)),
        };

        // 并行加载所有源（独立文件 I/O 并行），然后按优先级顺序合并（单线程，保证覆盖语义正确）
        var loadTasks = sources.Select(async s =>
        {
            SettingsJson? result = null;
            try
            {
                result = await s.Loader().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 单个来源加载失败不影响其他来源
                logger?.LogWarning(ex, "Failed to load settings from source");
            }
            return (s.Source, result);
        }).ToArray();

        await Task.WhenAll(loadTasks).ConfigureAwait(false);

        foreach (var (_, settings) in loadTasks.Select(t => t.Result))
        {
            if (settings is not null)
            {
                merged = SettingsMapper.Merge(merged, settings);
            }
        }

        return merged ?? new SettingsJson();
    }

    /// <summary>
    /// 构建默认 settings.json 骨架 — 含所有5个供应商的预设入口点,models 数组留空
    /// 模型列表由启动时 AutoFetchModels 从 {endpoint}/{modelsEndpoint} 自动拉取填充
    /// 供应商端点来源: README 供应商表 + ModelListFetcher 测试数据
    /// </summary>
    public static string BuildDefaultSettingsJson()
    {
        return """
        {
          "vendor": {
            "sensenova": {
              "provider": "sensenova",
              "protocol": "openai-compatible",
              "model": "sensenova-6.8-flash-lite",
              "endpoint": "https://token.sensenova.cn/v1",
              "apiKeyEnvVar": "SENSENOVA_API_KEY",
              "models": [],
              "modelsEndpoint": "models"
            },
            "openai": {
              "provider": "openai",
              "protocol": "openai-compatible",
              "model": "gpt-5.6-sol",
              "endpoint": "https://api.openai.com/v1",
              "apiKeyEnvVar": "OPENAI_API_KEY",
              "models": [],
              "modelsEndpoint": "models"
            },
            "anthropic": {
              "provider": "anthropic",
              "protocol": "anthropic",
              "model": "claude-opus-5",
              "endpoint": "https://api.anthropic.com",
              "apiKeyEnvVar": "ANTHROPIC_API_KEY",
              "models": [],
              "modelsEndpoint": "models"
            },
            "deepseek": {
              "provider": "deepseek",
              "protocol": "openai-compatible",
              "model": "deepseek-v4-flash",
              "endpoint": "https://api.deepseek.com",
              "apiKeyEnvVar": "DEEPSEEK_API_KEY",
              "models": [],
              "modelsEndpoint": "models"
            },
            "deepseek-anthropic": {
              "provider": "deepseek",
              "protocol": "anthropic",
              "model": "deepseek-v4-flash",
              "endpoint": "https://api.deepseek.com/anthropic",
              "apiKeyEnvVar": "DEEPSEEK_API_KEY",
              "models": [],
              "modelsEndpoint": "models"
            },
            "agnes": {
              "provider": "agnes",
              "protocol": "openai-compatible",
              "model": "agnes-2.0-flash",
              "endpoint": "https://apihub.agnes-ai.com/v1",
              "apiKeyEnvVar": "AGNES_API_KEY",
              "models": [],
              "modelsEndpoint": "models"
            },
            "zhipu": {
              "provider": "zhipu",
              "protocol": "openai-compatible",
              "model": "glm-5.3",
              "endpoint": "https://open.bigmodel.cn/api/paas/v4",
              "apiKeyEnvVar": "ZHIPUAI_API_KEY",
              "models": [],
              "modelsEndpoint": "models"
            }
          },
          "autoFetchModels": true,
          "current": { "profile": "sensenova" }
        }
        """;
    }

    /// <summary>
    /// 加载用户全局设置: ~/.jcc/settings.json
    /// 文件不存在或为空(0字节)时自动创建默认骨架
    /// </summary>
    public static async Task<SettingsJson?> LoadUserSettingsAsync(IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var path = GetUserSettingsPath();
        var result = await LoadSettingsFileAsync(fs, path, cancellationToken).ConfigureAwait(false);
        if (result is not null)
            return result;

        // 文件不存在或为空 → 自动创建默认骨架
        if (fs.FileExists(path) && fs.GetFileLength(path) > 0)
            return null; // 文件存在但非空,解析失败,返回 null

        var skeletonJson = BuildDefaultSettingsJson();
        var skeleton = JsonSerializer.Deserialize(skeletonJson, ConfigJsonContext.Default.SettingsJson);
        if (skeleton is null)
            return null;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            DirectoryHelper.EnsureDirectoryExists(fs, directory);

        await fs.WriteAllTextAsync(path, skeletonJson, cancellationToken).ConfigureAwait(false);
        return skeleton;
    }

    /// <summary>
    /// 同步加载用户全局设置 — 用于 Configure 回调等不支持 async 的场景
    /// 文件不存在或为空(0字节)时自动创建默认骨架
    /// </summary>
    public static SettingsJson? LoadUserSettings(IFileSystem fs)
    {
        var path = GetUserSettingsPath();
        var result = LoadSettingsFileSync(fs, path);
        if (result is not null)
            return result;

        if (fs.FileExists(path) && fs.GetFileLength(path) > 0)
            return null;

        var skeletonJson = BuildDefaultSettingsJson();
        var skeleton = JsonSerializer.Deserialize(skeletonJson, ConfigJsonContext.Default.SettingsJson);
        if (skeleton is null)
            return null;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            DirectoryHelper.EnsureDirectoryExists(fs, directory);

        fs.WriteAllText(path, skeletonJson);
        return skeleton;
    }

    /// <summary>
    /// 加载项目共享设置: .jcc/settings.json
    /// </summary>
    public static async Task<SettingsJson?> LoadProjectSettingsAsync(IFileSystem fs, string? projectDir, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectDir)) return null;
        var path = GetProjectSettingsPath(projectDir);
        return await LoadSettingsFileAsync(fs, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 加载项目本地设置: .jcc/settings.local.json
    /// </summary>
    public static async Task<SettingsJson?> LoadLocalSettingsAsync(IFileSystem fs, string? projectDir, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(projectDir)) return null;
        var path = GetLocalSettingsPath(projectDir);
        return await LoadSettingsFileAsync(fs, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 加载 CLI 标志设置: --settings 参数指定的路径
    /// </summary>
    public static async Task<SettingsJson?> LoadFlagSettingsAsync(IFileSystem fs, string? flagSettingsPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(flagSettingsPath)) return null;
        return await LoadSettingsFileAsync(fs, flagSettingsPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 加载策略设置: managed-settings.json（管理员强制）
    /// 对齐 TS 版: policySettings 内部 "first source wins" 策略
    /// </summary>
    public static async Task<SettingsJson?> LoadPolicySettingsAsync(IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var path = GetManagedSettingsPath();
        return await LoadSettingsFileAsync(fs, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存设置到指定来源
    /// </summary>
    public static async Task SaveSettingsAsync(
        IFileSystem fs,
        SettingSource source,
        SettingsJson settings,
        string? projectDir = null,
        CancellationToken cancellationToken = default)
    {
        var path = source switch
        {
            SettingSource.UserSettings => GetUserSettingsPath(),
            SettingSource.ProjectSettings => GetProjectSettingsPath(projectDir ?? fs.GetCurrentDirectory()),
            SettingSource.LocalSettings => GetLocalSettingsPath(projectDir ?? fs.GetCurrentDirectory()),
            _ => throw new ArgumentException($"[GRD007] 不支持保存到来源: {source}"),
        };

        var directory = Path.GetDirectoryName(path);
        DirectoryHelper.EnsureDirectoryExists(fs, directory);

        var json = JsonSerializer.Serialize(settings, ConfigIndentedJsonContext.Default.SettingsJson);
        await fs.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    #region 路径解析

    /// <summary>
    /// 获取用户全局设置路径
    /// 优先使用 JCC_APP_DATA_FOLDER 环境变量覆盖(测试隔离场景);
    /// AppDataFolder 为绝对路径时直接使用;
    /// 否则拼接 {UserProfile}/{AppDataFolder}/{SettingsFileName}
    /// </summary>
    public static string GetUserSettingsPath()
    {
        return Path.Combine(WorkflowConstants.Paths.JccDirectory, AppDataConstants.SettingsFileName);
    }

    /// <summary>
    /// 获取项目共享设置路径: {projectDir}/.jcc/settings.json
    /// 项目级目录始终使用相对目录名，当 AppDataFolder 为绝对路径（测试隔离）时回退到 .jcc
    /// </summary>
    public static string GetProjectSettingsPath(string projectDir)
    {
        var folderName = Path.IsPathRooted(AppDataConstants.AppDataFolder) ? ".jcc" : AppDataConstants.AppDataFolder;
        return Path.Combine(projectDir, folderName, "settings.json");
    }

    /// <summary>
    /// 获取项目本地设置路径: {projectDir}/.jcc/settings.local.json
    /// </summary>
    public static string GetLocalSettingsPath(string projectDir)
    {
        var folderName = Path.IsPathRooted(AppDataConstants.AppDataFolder) ? ".jcc" : AppDataConstants.AppDataFolder;
        return Path.Combine(projectDir, folderName, "settings.local.json");
    }

    /// <summary>
    /// 获取策略设置路径（管理员强制）
    /// Windows: C:\ProgramData\jcc\managed-settings.json
    /// 对齐 TS 版: Windows 下 C:\Program Files\ClaudeCode\managed-settings.json
    /// </summary>
    public static string GetManagedSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppDataConstants.AppDataFolder,
            "managed-settings.json");
    }

    #endregion

    #region 内部方法

    private static async Task<SettingsJson?> LoadSettingsFileAsync(IFileSystem fs, string path, CancellationToken cancellationToken)
    {
        if (!fs.FileExists(path))
            return null;

        try
        {
            var json = await fs.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        }
        catch
        {
            // 文件损坏或格式错误，返回 null（使用默认值）
            return null;
        }
    }

    /// <summary>
    /// 同步加载设置文件 — 用于 Configure 回调等不支持 async 的场景
    /// </summary>
    private static SettingsJson? LoadSettingsFileSync(IFileSystem fs, string path)
    {
        if (!fs.FileExists(path))
            return null;

        try
        {
            var json = fs.ReadAllText(path);
            return JsonSerializer.Deserialize(json, ConfigJsonContext.Default.SettingsJson);
        }
        catch
        {
            return null;
        }
    }

    #endregion
}
