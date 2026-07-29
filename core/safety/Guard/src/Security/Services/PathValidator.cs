using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 路径验证器实现
/// </summary>
[Register]
public sealed partial class PathValidator : IPathValidator
{
    // 路径逃逸模式字典
    private static readonly FrozenDictionary<string, string> PathEscapePatterns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".."] = "Parent directory traversal",
        ["~"] = "Home directory reference",
        ["/etc/"] = "System configuration directory",
        ["/usr/"] = "System user directory",
        ["/bin/"] = "System binary directory",
        ["/sbin/"] = "System superuser binary directory",
        ["/var/"] = "System variable directory",
        ["/root/"] = "Root user directory",
        [@"C:\Windows"] = "Windows system directory",
        [@"C:\Program Files"] = "Windows program directory",
        [@"C:\System32"] = "Windows system32 directory",
        [@"\\"] = "UNC network path",
        ["//"] = "Network path"
    }.ToFrozenDictionary();

    // 危险绝对路径前缀
    private static readonly FrozenSet<string> DangerousPathPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        @"C:\",
        @"D:\",
        @"E:\",
        "/home/",
        "/root/",
        "/tmp/",
        "/var/",
        "/etc/",
        "/usr/",
        "/bin/",
        "/sbin/",
        "/lib/",
        "/opt/",
        "/sys/",
        "/proc/",
        "/dev/"
    }.ToFrozenSet();

    public ValidationResult ValidatePaths(ShellCommand command, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return ValidationResult.Invalid("Working directory is not specified");
        }

        var normalizedWorkingDir = NormalizePath(workingDirectory);

        foreach (var path in command.ReferencedPaths)
        {
            // 检查路径逃逸模式
            foreach (var pattern in PathEscapePatterns)
            {
                if (path.Contains(pattern.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return ValidationResult.Invalid(
                        $"Path '{path}' contains escape pattern: {pattern.Value}");
                }
            }

            // 检查是否为危险绝对路径
            if (IsDangerousAbsolutePath(path))
            {
                return ValidationResult.Invalid(
                    $"Path '{path}' references a dangerous system location");
            }

            // 检查是否在工作区内
            if (!IsPathWithinWorkspace(path, normalizedWorkingDir))
            {
                return ValidationResult.Invalid(
                    $"Path '{path}' is outside the working directory '{workingDirectory}'");
            }
        }

        return ValidationResult.Valid();
    }

    public bool IsPathWithinWorkspace(string path, string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(workingDirectory))
        {
            return false;
        }

        var normalizedWorkingDir = NormalizePath(workingDirectory);

        // 如果是相对路径，先与工作目录组合，再解析
        string normalizedPath;
        if (!IsAbsolutePath(path))
        {
            // 使用 Path.Combine 将相对路径与工作目录组合
            var combinedPath = Path.Combine(normalizedWorkingDir, path);
            normalizedPath = NormalizePathWithBase(combinedPath);
        }
        else
        {
            normalizedPath = NormalizePath(path);
        }

        // 确保路径以工作目录开头
        return normalizedPath.StartsWith(normalizedWorkingDir, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDangerousAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return DangerousPathPrefixes.Any(prefix =>
            path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        // Windows 绝对路径
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return true;
        }

        // Unix 绝对路径
        if (path.StartsWith('/'))
        {
            return true;
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        // 统一使用系统路径分隔符
        var normalized = path.Replace('/', Path.DirectorySeparatorChar)
                             .Replace('\\', Path.DirectorySeparatorChar);

        // 移除末尾的路径分隔符（除了根路径）
        if (normalized.Length > 1 &&
            (normalized.EndsWith(Path.DirectorySeparatorChar) ||
             normalized.EndsWith('/')))
        {
            normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, '/');
        }

        // 解析 . 和 ..
        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch (Exception ex)
        {
            // 如果无法解析，返回原路径（如路径格式无效）
            System.Diagnostics.Trace.WriteLine($"Path normalization failed for '{normalized}': {ex.Message}");
        }

        return normalized;
    }

    /// <summary>
    /// 使用基础路径规范化路径（用于已经组合好的路径）
    /// </summary>
    private static string NormalizePathWithBase(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        // 统一使用系统路径分隔符
        var normalized = path.Replace('/', Path.DirectorySeparatorChar)
                             .Replace('\\', Path.DirectorySeparatorChar);

        // 移除末尾的路径分隔符（除了根路径）
        if (normalized.Length > 1 &&
            (normalized.EndsWith(Path.DirectorySeparatorChar) ||
             normalized.EndsWith('/')))
        {
            normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, '/');
        }

        // 解析 . 和 ..
        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch (Exception ex)
        {
            // 如果无法解析，返回原路径（如路径格式无效）
            System.Diagnostics.Trace.WriteLine($"Path normalization failed for '{normalized}': {ex.Message}");
        }

        return normalized;
    }
}
