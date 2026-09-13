namespace Core.Hooks.Execution;

/// <summary>
/// 钩子决策 — 描述钩子执行后对主流程的判定结果(放行/阻塞/继续/停止等)
/// </summary>
public sealed class HookDecision
{
    /// <summary>
    /// 决策类型(如 block / allow)
    /// </summary>
    [JsonPropertyName("decision")]
    public string? Decision { get; init; }

    /// <summary>
    /// 决策原因,用于日志与用户提示
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    /// <summary>
    /// 面向用户的提示消息
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// 是否继续主流程,true 表示继续,false 表示终止
    /// </summary>
    [JsonPropertyName("continue")]
    public bool? Continue { get; init; }

    /// <summary>
    /// 决策置信度,取值范围 0.0 ~ 1.0
    /// </summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    /// <summary>
    /// 停止原因,当 Continue 为 false 时填充
    /// </summary>
    [JsonPropertyName("stopReason")]
    public string? StopReason { get; init; }
}
