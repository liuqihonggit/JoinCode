namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// IFileOperationService 扩展方法 — 路径建议等跨工具复用逻辑
/// </summary>
public static class FileOperationServiceExtensions
{
    /// <summary>
    /// 当请求路径不存在时，建议 cwd 下的同名相对路径
    /// 对齐 TS suggestPathUnderCwd: 检测"遗漏仓库目录"模式
    /// 例如: cwd=/home/user/myrepo, 请求=/home/user/src/file.cs
    ///       → 建议=/home/user/myrepo/src/file.cs (如果存在)
    /// </summary>
    public static string? SuggestPathUnderCwd(this IFileOperationService fileOps, string requestedPath)
    {
        try
        {
            var cwd = fileOps.GetCurrentDirectory();
            var cwdParent = Path.GetDirectoryName(cwd);
            if (string.IsNullOrEmpty(cwdParent))
                return null;

            // 只检查路径在 cwd 的父目录下但不在 cwd 下的情况
            var cwdParentPrefix = cwdParent.EndsWith(Path.DirectorySeparatorChar)
                ? cwdParent
                : cwdParent + Path.DirectorySeparatorChar;

            if (!requestedPath.StartsWith(cwdParentPrefix, StringComparison.OrdinalIgnoreCase) ||
                requestedPath.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(requestedPath, cwd, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 从父目录获取相对路径
            var relFromParent = Path.GetRelativePath(cwdParent, requestedPath);

            // 检查 cwd 下是否存在同名相对路径（文件或目录）
            var correctedPath = fileOps.GetFullPath(Path.Combine(cwd, relFromParent));
            if (fileOps.FileExists(correctedPath) || fileOps.DirectoryExists(correctedPath))
            {
                return correctedPath;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
