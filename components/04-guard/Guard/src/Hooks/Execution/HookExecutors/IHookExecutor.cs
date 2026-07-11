
namespace Core.Hooks.Execution;

/// <summary>
/// 钩子执行器接口
/// </summary>
public interface IHookExecutor
{
    /// <summary>
    /// 支持的钩子类型
    /// </summary>
    string SupportedType { get; }

    /// <summary>
    /// 执行钩子
    /// </summary>
    /// <param name="hook">钩子命令</param>
    /// <param name="input">钩子输入</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>钩子结果</returns>
    Task<HookResult> ExecuteAsync(
        HookCommand hook,
        HookInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 钩子执行器接口（泛型）
/// </summary>
public interface IHookExecutor<in THook> : IHookExecutor where THook : HookCommand
{
    /// <summary>
    /// 执行特定类型的钩子
    /// </summary>
    Task<HookResult> ExecuteTypedAsync(
        THook hook,
        HookInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 钩子执行上下文
/// </summary>
public sealed record HookExecutionContext
{
    /// <summary>
    /// 钩子输入
    /// </summary>
    public required HookInput Input { get; init; }

    /// <summary>
    /// 钩子配置
    /// </summary>
    public required HookCommand Command { get; init; }

    /// <summary>
    /// 钩子来源
    /// </summary>
    public HookSource? Source { get; init; }

    /// <summary>
    /// 插件ID
    /// </summary>
    public string? PluginId { get; init; }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 超时时间
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// 获取已执行时长
    /// </summary>
    public TimeSpan Elapsed => DateTimeOffset.UtcNow - StartTime;

    /// <summary>
    /// 检查是否已超时
    /// </summary>
    public bool IsTimedOut => Timeout.HasValue && Elapsed > Timeout.Value;
}

/// <summary>
/// 钩子执行异常
/// </summary>
public class HookExecutionException : Exception
{
    public HookExecutionException(string message) : base(message) { }
    public HookExecutionException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// 钩子名称
    /// </summary>
    public string? HookName { get; init; }

    /// <summary>
    /// 钩子类型
    /// </summary>
    public string? HookType { get; init; }

    /// <summary>
    /// 退出码（如果有）
    /// </summary>
    public int? ExitCode { get; init; }
}

/// <summary>
/// 钩子超时异常
/// </summary>
public class HookTimeoutException : HookExecutionException
{
    public HookTimeoutException(string hookName, TimeSpan timeout)
        : base($"Hook '{hookName}' timed out after {timeout.TotalSeconds}s") { }
}


