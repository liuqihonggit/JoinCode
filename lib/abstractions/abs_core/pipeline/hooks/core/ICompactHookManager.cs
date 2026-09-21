namespace JoinCode.Abstractions.Hooks;

public interface ICompactHookManager : IHookManager {
    /// <summary>压缩前钩子。</summary>
    /// <param name="context">压缩上下文。</param>
    /// <param name="ct">取消令牌。</param>
    Task<CompactHookResult> OnPreCompactAsync(CompactHookContext context, CancellationToken ct = default);

    /// <summary>压缩后钩子。</summary>
    /// <param name="context">压缩上下文。</param>
    /// <param name="result">压缩结果数据。</param>
    /// <param name="ct">取消令牌。</param>
    Task OnPostCompactAsync(CompactHookContext context, PostCompactData result, CancellationToken ct = default);
}

public sealed partial class CompactHookContext {
    /// <summary>获取会话标识。</summary>
    public required string SessionId { get; init; }
    /// <summary>获取压缩触发原因。</summary>
    public required string Trigger { get; init; }
    /// <summary>获取当前 Token 数。</summary>
    public int CurrentTokenCount { get; init; }
    /// <summary>获取目标 Token 数。</summary>
    public int TargetTokenCount { get; init; }
    /// <summary>获取元数据字典。</summary>
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

public sealed partial class PostCompactData {
    /// <summary>获取是否已压缩。</summary>
    public bool Compacted { get; init; }
    /// <summary>获取压缩级别。</summary>
    public string? Level { get; init; }
    /// <summary>获取压缩触发原因。</summary>
    public string? Trigger { get; init; }
    /// <summary>获取压缩摘要。</summary>
    public string? Summary { get; init; }
    /// <summary>获取压缩前 Token 数。</summary>
    public int PreCompactTokenCount { get; init; }
    /// <summary>获取压缩后 Token 数。</summary>
    public int PostCompactTokenCount { get; init; }
    /// <summary>获取移除的消息数。</summary>
    public int MessagesRemoved { get; init; }
    /// <summary>获取保留的消息数。</summary>
    public int MessagesPreserved { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>获取元数据字典。</summary>
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
}

public sealed partial class CompactHookResult {
    /// <summary>获取是否应执行压缩。</summary>
    public bool ShouldCompact { get; init; } = true;
    /// <summary>获取提示消息。</summary>
    public string? Message { get; init; }
    /// <summary>获取压缩动作。</summary>
    public CompactHookAction Action { get; init; } = CompactHookAction.Proceed;
}

public enum CompactHookAction { [EnumValue("proceed")] Proceed, [EnumValue("skip")] Skip, [EnumValue("defer")] Defer, [EnumValue("custom")] Custom }
