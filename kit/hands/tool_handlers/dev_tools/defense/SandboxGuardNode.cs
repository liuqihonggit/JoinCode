namespace Tools.Handlers;

/// <summary>
/// 沙箱路径解析 node — 独立公共对象，包装 <see cref="ISandboxManager"/>，提供沙箱内路径解析与越界检测。
/// 任意解析路径的工具可注入此 node 确保路径在沙箱范围内。
/// </summary>
[Register(typeof(SandboxGuardNode), ServiceLifetime.Singleton)]
public sealed class SandboxGuardNode {
    private readonly ISandboxManager? _sandboxManager;
    private readonly ILogger<SandboxGuardNode>? _logger;

    /// <summary>
    /// 构造沙箱路径解析 node
    /// </summary>
    /// <param name="sandboxManager">可选的沙箱管理器</param>
    /// <param name="logger">可选日志记录器</param>
    public SandboxGuardNode(
        ISandboxManager? sandboxManager = null,
        ILogger<SandboxGuardNode>? logger = null) {
        _sandboxManager = sandboxManager;
        _logger = logger;
    }

    /// <summary>
    /// 将原始路径解析为沙箱内绝对路径，越界抛 UnauthorizedAccessException。
    /// 沙箱未激活时直接返回原路径。
    /// </summary>
    /// <param name="path">原始路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>沙箱内绝对路径</returns>
    /// <exception cref="UnauthorizedAccessException">路径越出沙箱范围</exception>
    public async ValueTask<string> ResolvePathAsync(string path, CancellationToken ct) {
        if (_sandboxManager is null || !_sandboxManager.IsInSandbox) return path;
        var sandboxId = _sandboxManager.CurrentSandboxId;
        if (sandboxId is null) return path;

        var resolvedPath = _sandboxManager.ResolvePath(path, sandboxId);
        var isInSandbox = await _sandboxManager.ActiveProvider!.IsPathInSandboxAsync(resolvedPath, sandboxId, ct).ConfigureAwait(false);
        if (!isInSandbox)
            throw new UnauthorizedAccessException($"Path '{path}' is outside the sandbox scope");
        return resolvedPath;
    }
}