using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.SlashCommands;

/// <summary>
/// 文件补全提供器 — 扫描当前工作目录的文件和文件夹，按前缀过滤。
/// 支持子目录递归：输入 @src/ 自动列出 src 目录内容，输入 @src/F 过滤 F 前缀。
/// @ 触发符调用，限制候选数量避免大目录卡顿。
/// </summary>
public static class FileCompletionProvider
{
    private const int MaxResults = 50;

    /// <summary>获取文件补全候选（扫描当前工作目录）</summary>
    public static IReadOnlyList<SlashCommandItem> GetFiles(string prefix)
    {
        try
        {
            var baseDir = Environment.CurrentDirectory;
            return GetFiles(prefix, baseDir);
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>获取文件补全候选（扫描指定目录，支持子目录路径前缀）</summary>
    public static IReadOnlyList<SlashCommandItem> GetFiles(string prefix, string baseDir)
    {
        try
        {
            var (scanDir, filePrefix, pathPrefix) = ResolveScanTarget(prefix, baseDir);
            var dir = new DirectoryInfo(scanDir);
            if (!dir.Exists)
                return [];

            var items = new List<SlashCommandItem>(MaxResults);
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                if (items.Count >= MaxResults)
                    break;
                var name = entry.Name;
                if (!string.IsNullOrEmpty(filePrefix) &&
                    !name.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var isDir = (entry.Attributes & FileAttributes.Directory) != 0;
                var displayPath = pathPrefix + name;
                items.Add(new SlashCommandItem
                {
                    Name = displayPath,
                    Description = isDir ? "文件夹" : "文件"
                });
            }
            return items;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>解析前缀为扫描目录、文件前缀、路径前缀（用于回填相对路径）</summary>
    private static (string scanDir, string filePrefix, string pathPrefix) ResolveScanTarget(string prefix, string baseDir)
    {
        var sepIdx = Math.Max(prefix.LastIndexOf('/'), prefix.LastIndexOf('\\'));
        if (sepIdx < 0)
            return (baseDir, prefix, string.Empty);

        var subDir = prefix[..sepIdx];
        var filePrefix = prefix[(sepIdx + 1)..];
        var scanDir = Path.Combine(baseDir, subDir);
        var pathPrefix = prefix[..(sepIdx + 1)];
        return (scanDir, filePrefix, pathPrefix);
    }
}
