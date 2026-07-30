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
            var fileName = Path.GetFileName(fullPath);
            var fallbackPath = Path.GetFullPath(Path.Combine(sandboxRoot, "redirected", fileName));
            Logger?.LogWarning("[Sandbox:Soft] 路径遍历攻击检测，降级重定向: '{Path}' → '{Fallback}'", path, fallbackPath);
            return fallbackPath;
        }

        return resolvedPath;
    }
}
