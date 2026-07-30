namespace Core.Security.Sandbox.Providers;

using JoinCode.Abstractions.Security.Sandbox;

[Register]
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
            var stack = new Stack<string>();
            var current = path;
            while (current is not null)
            {
                stack.Push(Path.GetFileName(current) ?? current);
                var parent = Path.GetDirectoryName(current);
                if (parent is null) break;
                current = parent;
            }

            if (stack.Count == 0) return null;

            var resolved = stack.Pop();
            var hadSymlink = false;
            while (stack.Count > 0)
            {
                var candidate = Path.Combine(resolved, stack.Pop());
                var dirInfo = new DirectoryInfo(candidate);
                if (dirInfo.Exists && dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var target = dirInfo.ResolveLinkTarget(true);
                    logger?.LogDebug("[Sandbox:Soft] 符号链接检测: '{Candidate}' -> '{Target}'", candidate, target?.FullName);
                    if (target is not null)
                    {
                        resolved = target.FullName;
                        hadSymlink = true;
                        continue;
                    }
                }
                var fileInfo = new FileInfo(candidate);
                if (fileInfo.Exists && fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var target = fileInfo.ResolveLinkTarget(true);
                    if (target is not null)
                    {
                        resolved = target.FullName;
                        hadSymlink = true;
                        continue;
                    }
                }
                resolved = candidate;
            }

            return hadSymlink ? Path.GetFullPath(resolved) : null;
        }
        catch
        {
            return null;
        }
    }
}
