namespace NativeTui.Core.Repl;

/// <summary>
/// jcc 用户级配置根目录解析（TUI 侧轻量镜像）— 语义对齐 modules/abstractions 的
/// AppDataPaths.JccDirectory：JCC_APP_DATA_FOLDER 环境变量覆盖（绝对路径直接用 / 相对名拼
/// UserProfile），默认 ~/.jcc/。NativeTui.Core 保持零外部项目依赖，故本地实现同一契约
/// （modules 侧 AppDataPaths 是唯一数据源，此处仅消费同一环境变量，不引入第二套定义）。
/// 测试隔离场景（JCC_APP_DATA_FOLDER 指向临时目录）TUI 的 keybindings/command-history
/// 读写随之隔离，不再污染真实用户目录。
/// </summary>
public static class JccHome
{
    /// <summary>覆盖目录的环境变量名 — 与 AppDataPaths/JccEnvVar.AppDataFolder 同一契约。</summary>
    public const string AppDataFolderEnvVar = "JCC_APP_DATA_FOLDER";

    /// <summary>默认目录名。</summary>
    public const string DefaultFolderName = ".jcc";

    /// <summary>用户级配置根目录（~/.jcc/ 或 JCC_APP_DATA_FOLDER 覆盖值）。</summary>
    public static string RootDirectory => Resolve();

    /// <summary>拼用户级配置文件完整路径。</summary>
    public static string Combine(string fileName) => Path.Combine(RootDirectory, fileName);

    private static string Resolve()
    {
        var folder = Environment.GetEnvironmentVariable(AppDataFolderEnvVar);
        if (string.IsNullOrEmpty(folder))
        {
            folder = DefaultFolderName;
        }

        if (Path.IsPathRooted(folder))
        {
            return folder;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder);
    }
}
