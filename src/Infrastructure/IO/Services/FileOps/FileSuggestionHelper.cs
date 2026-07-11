namespace Infrastructure.IO.Services.FileOps;

/// <summary>
/// 文件未找到时的建议工具。
/// 对齐 TS: findSimilarFile + suggestPathUnderCwd — file.ts
/// </summary>
public static class FileSuggestionHelper
{
    /// <summary>
    /// 在同目录下查找同名但不同扩展名的文件。
    /// 对齐 TS: findSimilarFile — file.ts L178-207
    /// 算法：取请求路径的目录+基本名，过滤同名不同扩展名的文件，返回第一个匹配。
    /// </summary>
    /// <param name="filePath">请求的文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <returns>相似文件名；未找到返回 null</returns>
    public static string? FindSimilarFile(string filePath, IFileSystem fs)
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || !fs.DirectoryExists(dir))
                return null;

            var fileBaseName = Path.GetFileNameWithoutExtension(filePath);
            var fileExtension = Path.GetExtension(filePath);

            foreach (var existingFile in fs.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                var existingBaseName = Path.GetFileNameWithoutExtension(existingFile);
                var existingExtension = Path.GetExtension(existingFile);

                // 同名但不同扩展名
                if (string.Equals(existingBaseName, fileBaseName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(existingExtension, fileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFileName(existingFile);
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 检测遗漏仓库目录的路径，建议修正路径。
    /// 对齐 TS: suggestPathUnderCwd — file.ts L228-267
    /// 算法：请求路径在 CWD 父目录下但不在 CWD 下 → 拼接到 CWD 下检查是否存在。
    /// </summary>
    /// <param name="requestedPath">请求的文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <returns>修正后的路径；无法修正返回 null</returns>
    public static string? SuggestPathUnderCwd(string requestedPath, IFileSystem fs)
    {
        try
        {
            var cwd = fs.GetCurrentDirectory();
            var cwdParent = Path.GetDirectoryName(cwd);
            if (string.IsNullOrEmpty(cwdParent))
                return null;

            // 解析请求路径为绝对路径
            var resolvedPath = Path.GetFullPath(requestedPath);

            // 请求路径必须在 CWD 父目录下，但不在 CWD 下
            var cwdParentPrefix = cwdParent.EndsWith(Path.DirectorySeparatorChar)
                ? cwdParent
                : cwdParent + Path.DirectorySeparatorChar;
            var cwdPrefix = cwd.EndsWith(Path.DirectorySeparatorChar)
                ? cwd
                : cwd + Path.DirectorySeparatorChar;

            if (!resolvedPath.StartsWith(cwdParentPrefix, StringComparison.OrdinalIgnoreCase) ||
                resolvedPath.StartsWith(cwdPrefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resolvedPath, cwd, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // 计算从 CWD 父目录到请求路径的相对路径，拼接到 CWD 下
            var relFromParent = Path.GetRelativePath(cwdParent, resolvedPath);
            var correctedPath = Path.Combine(cwd, relFromParent);

            return fs.FileExists(correctedPath) ? correctedPath : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 构建文件未找到的错误消息，包含相似文件建议。
    /// 对齐 TS: FileReadTool catch ENOENT — FileReadTool.ts L639-646
    /// 优先级：suggestPathUnderCwd > findSimilarFile
    /// </summary>
    /// <param name="filePath">请求的文件路径</param>
    /// <param name="fs">文件系统抽象</param>
    /// <returns>带建议的错误消息</returns>
    public static string BuildFileNotFoundMessage(string filePath, IFileSystem fs)
    {
        var message = $"File does not exist. Note: your current working directory is {fs.GetCurrentDirectory()}.";

        var cwdSuggestion = SuggestPathUnderCwd(filePath, fs);
        if (cwdSuggestion is not null)
        {
            message += $" Did you mean {cwdSuggestion}?";
        }
        else
        {
            var similarFile = FindSimilarFile(filePath, fs);
            if (similarFile is not null)
            {
                message += $" Did you mean {similarFile}?";
            }
        }

        return message;
    }
}
