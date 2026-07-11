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
        CancellationToken cancellationToken = default)
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

        foreach (var (source, loader) in sources)
        {
            try
            {
                var settings = await loader().ConfigureAwait(false);
                if (settings is not null)
                {
                    merged = SettingsMapper.Merge(merged, settings);
                }
            }
            catch (Exception ex)
            {
                // 单个来源加载失败不影响其他来源
                System.Diagnostics.Trace.WriteLine($"Failed to load settings from source: {ex.Message}");
            }
        }

        return merged ?? new SettingsJson();
    }

    /// <summary>
    /// 加载用户全局设置: ~/.jcc/settings.json
    /// </summary>
    public static async Task<SettingsJson?> LoadUserSettingsAsync(IFileSystem fs, CancellationToken cancellationToken = default)
    {
        var path = GetUserSettingsPath();
        return await LoadSettingsFileAsync(fs, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 同步加载用户全局设置 — 用于 Configure 回调等不支持 async 的场景
    /// </summary>
    public static SettingsJson? LoadUserSettings(IFileSystem fs)
    {
        var path = GetUserSettingsPath();
        return LoadSettingsFileSync(fs, path);
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
            _ => throw new ArgumentException($"不支持保存到来源: {source}"),
        };

        var directory = Path.GetDirectoryName(path);
        DirectoryHelper.EnsureDirectoryExists(fs, directory);

        var json = JsonSerializer.Serialize(settings, ConfigIndentedJsonContext.Default.SettingsJson);
        await fs.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    #region 路径解析

    /// <summary>
    /// 获取用户全局设置路径
    /// AppDataFolder 为绝对路径时直接使用（测试隔离场景）;
    /// 否则拼接 {UserProfile}/{AppDataFolder}/{SettingsFileName}
    /// </summary>
    public static string GetUserSettingsPath()
    {
        var appDataFolder = AppDataConstants.AppDataFolder;
        var settingsFileName = AppDataConstants.SettingsFileName;

        // 绝对路径: 直接使用（测试隔离时 AppDataFolder 被设为临时目录绝对路径）
        if (Path.IsPathRooted(appDataFolder))
            return Path.Combine(appDataFolder, settingsFileName);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            appDataFolder,
            settingsFileName);
    }

    /// <summary>
    /// 获取项目共享设置路径: {projectDir}/.jcc/settings.json
    /// 项目设置始终使用 .jcc 相对目录名，不受 AppDataFolder 绝对路径覆盖影响
    /// </summary>
    public static string GetProjectSettingsPath(string projectDir)
    {
        // 项目设置固定使用 .jcc 目录名，不跟随 AppDataFolder 覆盖
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
            "jcc",
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
