namespace Core.Security.Sandbox.Providers;

using JoinCode.Abstractions.Security.Sandbox;

[Register(typeof(SandboxProviderBase), ServiceLifetime.Singleton)]
public sealed partial class SoftSandboxProvider : SandboxProviderBase
{
    public override SandboxType SandboxType => SandboxType.Soft;
    public override SandboxCapabilities Capabilities => SandboxCapabilities.PathRedirection | SandboxCapabilities.FileSystemIsolation;

    public SoftSandboxProvider(IFileSystem fs, ILogger<SoftSandboxProvider>? logger = null, IClockService? clock = null, ITelemetryService? telemetryService = null)
        : base(fs, logger, clock ?? SystemClockService.Instance, telemetryService)
    {
    }

    private protected override string OnResolvePath(string path, SandboxInfo info)
    {
        var fullPath = Path.GetFullPath(path);
        var sandboxRoot = Path.GetFullPath(info.RootPath);

        if (fullPath.StartsWith(sandboxRoot, StringComparison.OrdinalIgnoreCase))
        {
            var realPath = ResolveSymlinkTarget(fullPath, Logger);
            if (realPath is not null && !realPath.StartsWith(sandboxRoot, StringComparison.OrdinalIgnoreCase))
            {
                Logger?.LogWarning("[Sandbox:Soft] 符号链接逃逸检测: '{Path}' 解析到沙箱外 '{Real}'，降级重定向", fullPath, realPath);
                return FallbackRedirect(fullPath, sandboxRoot);
            }
            return fullPath;
        }

        if (info.AllowedPaths is not null)
        {
            foreach (var allowed in info.AllowedPaths)
            {
                var fullAllowed = Path.GetFullPath(allowed);
                if (fullPath.StartsWith(fullAllowed, StringComparison.OrdinalIgnoreCase))
                {
                    return fullPath;
                }
            }
        }

        var sanitizedPath = path.Replace('\\', '/').TrimStart('/');
        var segments = sanitizedPath.Split('/');
        var safeSegments = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment is ".." or ".")
            {
                continue;
            }
            if (!string.IsNullOrEmpty(segment))
            {
                safeSegments.Add(segment);
            }
        }

        var safeRelativePath = string.Join(Path.DirectorySeparatorChar, safeSegments);
        var resolvedPath = Path.GetFullPath(Path.Combine(sandboxRoot, safeRelativePath));

        if (!resolvedPath.StartsWith(sandboxRoot, StringComparison.OrdinalIgnoreCase))
        {
            return FallbackRedirect(fullPath, sandboxRoot);
        }

        return resolvedPath;
    }

    private static string FallbackRedirect(string fullPath, string sandboxRoot)
    {
        var fileName = Path.GetFileName(fullPath);
        return Path.GetFullPath(Path.Combine(sandboxRoot, "redirected", fileName));
    }

    private static string? ResolveSymlinkTarget(string path, ILogger? logger)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
                return null;

            var relativePart = fullPath[root.Length..];
            var segments = relativePart.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

            var resolved = root;
            var hadSymlink = false;
            foreach (var segment in segments)
            {
                var candidate = Path.Combine(resolved, segment);

                // 防御层1: candidate 必须是绝对路径，否则分解逻辑有误
                if (!Path.IsPathRooted(candidate))
                {
                    logger?.LogWarning("[Sandbox:Soft] ResolveSymlinkTarget 内部错误: candidate='{Candidate}' 不是绝对路径，中断解析", candidate);
                    return null;
                }

                if (TryResolveSymlink(candidate, isDirectory: true, logger, out var target) ||
                    TryResolveSymlink(candidate, isDirectory: false, logger, out target))
                {
                    resolved = target;
                    hadSymlink = true;
                    continue;
                }

                resolved = candidate;
            }

            return hadSymlink ? Path.GetFullPath(resolved) : null;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "[Sandbox:Soft] ResolveSymlinkTarget 异常，路径: '{Path}'", path);
            return null;
        }
    }

    private static bool TryResolveSymlink(string candidate, bool isDirectory, ILogger? logger, [NotNullWhen(true)] out string? target)
    {
        target = null;
        try
        {
            FileSystemInfo fsInfo = isDirectory ? new DirectoryInfo(candidate) : new FileInfo(candidate);

            // 防御层2: 不存在的条目跳过（Attributes=-1 时 HasFlag 误判为 true）
            if (!fsInfo.Exists)
                return false;

            var attrs = fsInfo.Attributes;

            // 防御层3: Attributes 值合理性校验（-1 表示获取失败）
            if ((int)attrs == -1)
                return false;

            if (!attrs.HasFlag(FileAttributes.ReparsePoint))
                return false;

            var linkTarget = fsInfo.ResolveLinkTarget(true);
            if (linkTarget is not null)
            {
                logger?.LogDebug("[Sandbox:Soft] 符号链接检测({Kind}): '{Candidate}' -> '{Target}'", isDirectory ? "dir" : "file", candidate, linkTarget.FullName);
                target = linkTarget.FullName;
                return true;
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "[Sandbox:Soft] 符号链接检测异常({Kind}): '{Candidate}'", isDirectory ? "dir" : "file", candidate);
        }

        return false;
    }
}
