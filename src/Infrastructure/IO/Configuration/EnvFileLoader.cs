
namespace Infrastructure.IO.Configuration;

/// <summary>
/// 轻量 .env 文件加载器 — 不依赖第三方包，兼容 NativeAOT
/// 格式: KEY=VALUE，支持 # 注释，忽略空行和引号包裹
/// </summary>
public static class EnvFileLoader
{
    /// <summary>
    /// 从指定 .env 文件加载环境变量到当前进程
    /// </summary>
    /// <param name="envFilePath">.env 文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="overwrite">是否覆盖已存在的环境变量（默认 false）</param>
    /// <returns>实际加载的变量数量</returns>
    public static int Load(string envFilePath, IFileSystem fs, bool overwrite = false)
    {
        if (!fs.FileExists(envFilePath))
            return 0;

        var count = 0;
        foreach (var line in fs.ReadAllLines(envFilePath))
        {
            var trimmed = line.AsSpan().Trim();

            // 跳过空行和注释
            if (trimmed.IsEmpty || trimmed[0] == '#')
                continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0)
                continue;

            var key = trimmed[..eqIndex].Trim().ToString();
            var value = trimmed[(eqIndex + 1)..].Trim();

            // 去除引号包裹
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (!overwrite && Environment.GetEnvironmentVariable(key) is not null)
                continue;

            Environment.SetEnvironmentVariable(key, value.ToString());
            count++;
        }

        return count;
    }

    /// <summary>
    /// 从当前目录及父目录逐级查找 .env 文件并加载
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="overwrite">是否覆盖已存在的环境变量</param>
    /// <param name="maxParentLevels">向上查找的最大父目录层级</param>
    /// <returns>实际加载的变量数量</returns>
    public static int LoadFromDirectory(IFileSystem fs, bool overwrite = false, int maxParentLevels = 5)
    {
        var dir = fs.GetCurrentDirectory();
        for (var i = 0; i <= maxParentLevels; i++)
        {
            // 检查 .env 文件（直接文件）
            var envPath = fs.CombinePath(dir, ".env");
            if (fs.FileExists(envPath))
                return Load(envPath, fs, overwrite);

            // 检查 .env/env.txt 格式（目录包含文件）
            var altPath = fs.CombinePath(dir, ".env", "env.txt");
            if (fs.FileExists(altPath))
                return Load(altPath, fs, overwrite);

            // 向上一级
            var parent = fs.GetParentPath(dir);
            if (parent is null)
                break;
            dir = parent;
        }

        return 0;
    }

    /// <summary>
    /// 获取环境变量值
    /// </summary>
    public static string? Get(string key, string? defaultValue = null)
    {
        return Environment.GetEnvironmentVariable(key) ?? defaultValue;
    }
}
