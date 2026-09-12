namespace Core.Hooks.Execution;

/// <summary>
/// 工具修正 Hook 接口 — 当工具错误次数达到阈值时自动触发修正
/// <para>
/// 核心价值：
/// 1. 自动修正工具调用参数（如为 gh pr create 添加 --body）
/// 2. 自动修复 JSON 格式问题
/// 3. 自动添加超时参数
/// </para>
/// </summary>
public interface IToolFixHook
{
    /// <summary>
    /// 判断是否可以修正该工具的错误
    /// </summary>
    bool CanFix(string toolName, Exception error);

    /// <summary>
    /// 执行修正
    /// </summary>
    Task<ToolFixResult> FixAsync(string toolName, Exception error, CancellationToken ct = default);

    /// <summary>
    /// 修正器优先级（数值越大优先级越高）
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 修正器名称（用于日志和调试）
    /// </summary>
    string Name { get; }
}

/// <summary>
/// 工具修正结果
/// </summary>
public sealed class ToolFixResult
{
    public required bool Success { get; init; }
    public string? FixedCommand { get; init; }
    public string? Description { get; init; }
    public string? Error { get; init; }
}
