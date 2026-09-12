namespace Core.Security.Services;

/// <summary>
/// 真实路径解析器 — 返回文件系统真实大小写路径,用于大小写不敏感文件系统(Windows)的路径大小写守卫
/// </summary>
public interface IRealPathResolver
{
    /// <summary>
    /// 获取指定路径在文件系统中的真实大小写路径
    /// </summary>
    /// <param name="path">待解析的路径(相对或绝对)</param>
    /// <returns>真实大小写路径;路径不存在返回 null</returns>
    string? GetRealPath(string path);
}

/// <summary>
/// 文件系统真实路径解析器 — 枚举父目录获取条目真实大小写
/// </summary>
[Register(typeof(IRealPathResolver), ServiceLifetime.Singleton)]
public sealed class FileSystemRealPathResolver : IRealPathResolver
{
    private readonly IFileSystem _fs;

    /// <summary>
    /// 创建 FileSystemRealPathResolver
    /// </summary>
    public FileSystemRealPathResolver(IFileSystem fs)
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    /// <inheritdoc />
    public string? GetRealPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = PathNormalizer.TrimTrailingSeparators(path);
        if (string.IsNullOrEmpty(trimmed))
            return null;

        string fullPath;
        try
        {
            fullPath = _fs.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            return null;
        }

        var parent = Path.GetDirectoryName(fullPath);
        var name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            return null;

        if (!_fs.DirectoryExists(parent))
            return null;

        try
        {
            foreach (var entry in _fs.EnumerateFiles(parent, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            foreach (var entry in _fs.EnumerateDirectories(parent, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }
}
