using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.SlashCommands;

/// <summary>
/// 文件补全提供器 — 扫描当前工作目录的文件和文件夹，按前缀过滤。
/// @ 触发符调用，限制候选数量避免大目录卡顿。
/// </summary>
public static class FileCompletionProvider
{
    private const int MaxResults = 50;

    /// <summary>获取文件补全候选（扫描当前工作目录第一层）</summary>
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

    /// <summary>获取文件补全候选（扫描指定目录第一层）</summary>
    public static IReadOnlyList<SlashCommandItem> GetFiles(string prefix, string baseDir)
    {
        try
        {
            var dir = new DirectoryInfo(baseDir);
            if (!dir.Exists)
                return [];

            var items = new List<SlashCommandItem>(MaxResults);
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                if (items.Count >= MaxResults)
                    break;
                var name = entry.Name;
                if (!string.IsNullOrEmpty(prefix) &&
                    !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var isDir = (entry.Attributes & FileAttributes.Directory) != 0;
                items.Add(new SlashCommandItem
                {
                    Name = name,
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
}
