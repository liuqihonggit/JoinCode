
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 二进制文件检测器 — 按扩展名判断是否为二进制文件
/// <para>统一 RgEngine/SearchService/FileToolHandlers 三处重复的 BinaryExtensions 定义</para>
/// <para>扩展名集取超集（80个），覆盖可执行文件/图片/音视频/压缩包/文档/字体/字节码/数据库/设计文件</para>
/// </summary>
public static class BinaryFileDetector
{
    private static readonly FrozenSet<string> BinaryExtensions = FrozenSet.ToFrozenSet(
        [
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff", ".tif",
            ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".flv", ".m4v", ".mpeg", ".mpg",
            ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma", ".aiff", ".opus",
            ".zip", ".tar", ".gz", ".bz2", ".7z", ".rar", ".xz", ".z", ".tgz", ".iso",
            ".exe", ".dll", ".so", ".dylib", ".bin", ".o", ".a", ".obj", ".lib", ".app", ".msi", ".deb", ".rpm",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp",
            ".ttf", ".otf", ".woff", ".woff2", ".eot",
            ".pyc", ".pyo", ".class", ".jar", ".war", ".ear", ".node", ".wasm", ".rlib",
            ".sqlite", ".sqlite3", ".db", ".mdb", ".idx",
            ".psd", ".ai", ".eps", ".sketch", ".fig", ".xd", ".blend", ".3ds", ".max",
            ".swf", ".fla",
            ".lockb", ".dat", ".data",
            ".pyd", ".pdb", ".mdb", ".nupkg", ".snupkg", ".dex", ".apk", ".ipa",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 按文件扩展名判断是否为二进制文件
    /// </summary>
    public static bool IsBinaryByExtension(string file)
    {
        var ext = Path.GetExtension(file);
        return !string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext);
    }

    /// <summary>
    /// 按扩展名字符串判断是否为二进制文件
    /// </summary>
    public static bool IsBinaryExtension(string ext)
    {
        return !string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext);
    }
}
