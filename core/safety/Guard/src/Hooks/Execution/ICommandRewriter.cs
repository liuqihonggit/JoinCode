namespace Core.Hooks.Execution;

/// <summary>
/// 命令改写器接口 — 在命令执行前进行改写/路由
/// <para>
/// 核心价值：
/// 1. 自动修正命令参数（如为 gh pr create 添加 --body）
/// 2. 自动添加超时参数
/// 3. VPN 路由识别和代理切换
/// </para>
/// </summary>
public interface ICommandRewriter
{
    /// <summary>
    /// 判断是否可以改写该命令
    /// </summary>
    bool CanRewrite(string command);

    /// <summary>
    /// 改写命令
    /// </summary>
    string Rewrite(string command, IReadOnlyDictionary<string, object> context);

    /// <summary>
    /// 改写器优先级（数值越大优先级越高）
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 改写器名称（用于日志和调试）
    /// </summary>
    string Name { get; }
}

/// <summary>
/// 命令改写结果
/// </summary>
public sealed class CommandRewriteResult
{
    public required string OriginalCommand { get; init; }
    public required string RewrittenCommand { get; init; }
    public required bool WasRewritten { get; init; }
    public string? RewriterName { get; init; }
    public string? Reason { get; init; }
}
