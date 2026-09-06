
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 文件类型到扩展名的映射表 — 对齐 ripgrep --type 内置映射
/// <para>统一 RgEngine/SearchService 两处重复定义</para>
/// <para>扩展名格式：带点不带星号（.js），文件名项不带点（Dockerfile）</para>
/// </summary>
public static class FileTypeExtensionMap
{
    private static readonly FrozenDictionary<string, string[]> Extensions = FrozenDictionary.ToFrozenDictionary(
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["js"] = [".js", ".jsx", ".mjs", ".cjs"],
            ["ts"] = [".ts", ".tsx", ".mts", ".cts"],
            ["py"] = [".py", ".pyi"],
            ["rust"] = [".rs"],
            ["go"] = [".go"],
            ["java"] = [".java"],
            ["c"] = [".c", ".h"],
            ["cpp"] = [".cpp", ".cc", ".cxx", ".hpp", ".hh", ".hxx"],
            ["csharp"] = [".cs"],
            ["cs"] = [".cs"],
            ["ruby"] = [".rb", ".erb"],
            ["swift"] = [".swift"],
            ["kotlin"] = [".kt", ".kts"],
            ["scala"] = [".scala"],
            ["html"] = [".html", ".htm"],
            ["css"] = [".css", ".scss", ".sass", ".less"],
            ["json"] = [".json"],
            ["yaml"] = [".yaml", ".yml"],
            ["xml"] = [".xml", ".xsl", ".xsd"],
            ["markdown"] = [".md", ".mdx"],
            ["md"] = [".md", ".mdx"],
            ["sh"] = [".sh", ".bash", ".zsh"],
            ["powershell"] = [".ps1", ".psm1"],
            ["sql"] = [".sql"],
            ["toml"] = [".toml"],
            ["ini"] = [".ini", ".cfg", ".conf"],
            ["dockerfile"] = ["Dockerfile", ".dockerfile"],
        },
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 文件类型到扩展名/文件名的映射
    /// </summary>
    public static FrozenDictionary<string, string[]> Map => Extensions;

    /// <summary>
    /// 尝试获取文件类型对应的扩展名列表
    /// </summary>
    public static bool TryGetValue(string fileType, out string[] extensions)
    {
        return Extensions.TryGetValue(fileType, out extensions!);
    }

    /// <summary>
    /// 判断文件是否匹配指定类型
    /// </summary>
    public static bool MatchesFileType(string file, string? fileType)
    {
        if (string.IsNullOrEmpty(fileType))
            return true;

        if (Extensions.TryGetValue(fileType, out var exts))
        {
            var ext = Path.GetExtension(file);
            var fileName = Path.GetFileName(file);
            foreach (var e in exts)
            {
                if (e.StartsWith('.') && string.Equals(ext, e, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!e.StartsWith('.') && string.Equals(fileName, e, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        return string.Equals(Path.GetExtension(file), $".{fileType}", StringComparison.OrdinalIgnoreCase);
    }
}
