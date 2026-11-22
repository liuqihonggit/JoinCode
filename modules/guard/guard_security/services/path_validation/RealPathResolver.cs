namespace Core.Security.Services;

/// <summary>
/// 真实路径解析器 — 返回文件系统真实大小写路径,用于大小写不敏感文件系统(Windows)的路径大小写守卫
/// </summary>
public interface IRealPathResolver {
    /// <summary>
    /// 尝试获取指定路径在文件系统中的真实大小写路径
    /// </summary>
    /// <param name="path">待解析的路径(相对或绝对)</param>
    /// <param name="realPath">真实大小写路径</param>
    /// <returns>路径存在返回 true</returns>
    bool TryGetRealPath(string path, [NotNullWhen(true)] out string? realPath);
}

/// <summary>
/// 文件系统真实路径解析器 — 枚举父目录获取条目真实大小写
/// </summary>
[Register(typeof(IRealPathResolver), ServiceLifetime.Singleton)]
public sealed class FileSystemRealPathResolver : IRealPathResolver {
    private readonly IFileSystem _fs;

    /// <summary>
    /// 创建 FileSystemRealPathResolver
    /// </summary>
    public FileSystemRealPathResolver(IFileSystem fs) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
    }

    /// <inheritdoc />
    public bool TryGetRealPath(string path, [NotNullWhen(true)] out string? realPath) {
        realPath = null;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var trimmed = PathNormalizer.TrimTrailingSeparators(path);
        if (string.IsNullOrEmpty(trimmed))
            return false;

        string fullPath;
        try {
            fullPath = _fs.GetFullPath(trimmed);
        } catch (Exception) {
            return false;
        }

        var parent = Path.GetDirectoryName(fullPath);
        var name = Path.GetFileName(fullPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
            return false;

        if (!_fs.DirectoryExists(parent))
            return false;

        try {
            foreach (var entry in _fs.EnumerateFiles(parent, "*", SearchOption.TopDirectoryOnly)) {
                if (string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase)) {
                    realPath = entry;
                    return true;
                }
            }

            foreach (var entry in _fs.EnumerateDirectories(parent, "*", SearchOption.TopDirectoryOnly)) {
                if (string.Equals(Path.GetFileName(entry), name, StringComparison.OrdinalIgnoreCase)) {
                    realPath = entry;
                    return true;
                }
            }
        } catch (Exception) {
            return false;
        }

        return false;
    }
}