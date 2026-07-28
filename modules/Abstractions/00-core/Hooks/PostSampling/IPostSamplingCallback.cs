
namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// Post-sampling 回调上下文 — 每轮 LLM 采样完成后传递给回调
/// </summary>
public sealed class PostSamplingContext
{
    /// <summary>
    /// 当前 token 估算数
    /// </summary>
    public int EstimatedTokenCount { get; init; }

    /// <summary>
    /// 自上次提取以来的工具调用次数
    /// </summary>
    public int ToolCallsSinceLastExtraction { get; init; }

    /// <summary>
    /// 查询来源标识
    /// </summary>
    public string? QuerySource { get; init; }

    /// <summary>
    /// 当前会话 ID
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Post-sampling 回调接口 — 在每轮 LLM 采样完成后触发
/// </summary>
public interface IPostSamplingCallback
{
    /// <summary>
    /// 执行回调
    /// </summary>
    Task OnPostSamplingAsync(PostSamplingContext context);
}
