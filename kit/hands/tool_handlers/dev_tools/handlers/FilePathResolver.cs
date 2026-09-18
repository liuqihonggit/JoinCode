namespace Tools.Handlers;

/// <summary>
/// 文件路径解析器 — 封装沙箱路径解析与设备路径黑名单检查
/// 从 FileToolHandlers 提取,统一管理路径安全验证
/// </summary>
internal sealed class FilePathResolver
{
    private static readonly FrozenSet<string> BlockedDevicePaths = CreateBlockedDevicePathSet();
    private readonly ISandboxManager? _sandboxManager;

    /// <summary>构造 FilePathResolver</summary>
    public FilePathResolver(ISandboxManager? sandboxManager = null) => _sandboxManager = sandboxManager;

    /// <summary>是否为阻塞的设备路径</summary>
    public static bool IsBlockedDevicePath(string filePath)
    {
        if (BlockedDevicePaths.Contains(filePath))
            return true;

        if (filePath.StartsWith("/proc/", StringComparison.OrdinalIgnoreCase)
            && (filePath.EndsWith("/fd/0", StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith("/fd/1", StringComparison.OrdinalIgnoreCase)
                || filePath.EndsWith("/fd/2", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    /// <summary>解析沙箱路径 — 非沙箱环境直接返回原路径</summary>
    public async Task<string> ResolveSandboxPathAsync(string path, CancellationToken cancellationToken)
    {
        if (_sandboxManager == null || !_sandboxManager.IsInSandbox)
        {
            return path;
        }

        var sandboxId = _sandboxManager.CurrentSandboxId;
        if (sandboxId is null)
        {
            return path;
        }

        var resolvedPath = _sandboxManager.ResolvePath(path, sandboxId);
        var isInSandbox = await _sandboxManager.ActiveProvider!.IsPathInSandboxAsync(resolvedPath, sandboxId, cancellationToken).ConfigureAwait(false);
        if (!isInSandbox)
        {
            throw new UnauthorizedAccessException($"Path '{path}' is outside the sandbox scope");
        }

        return resolvedPath;
    }

    private static FrozenSet<string> CreateBlockedDevicePathSet()
    {
        return FrozenSet.ToFrozenSet(
        [
            "/dev/zero", "/dev/random", "/dev/urandom", "/dev/full",
            "/dev/stdin", "/dev/tty", "/dev/console",
            "/dev/stdout", "/dev/stderr",
            "/dev/fd/0", "/dev/fd/1", "/dev/fd/2"
        ], StringComparer.OrdinalIgnoreCase);
    }
}
