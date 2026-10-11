namespace JoinCode.Dream.Pipeline;


/// <summary>
/// Dream 管道上下文 — 中间件间共享的可变状态
/// </summary>
public sealed class DreamContext : PipelineContextBase {
    /// <summary>Dream 请求</summary>
    public required DreamRequest Request { get; init; }
    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>会话 ID 集合</summary>
    public IEnumerable<string> SessionIds { get; set; } = [];
    /// <summary>任务 ID</summary>
    public string? TaskId { get; set; }
    /// <summary>系统提示词</summary>
    public string? SystemPrompt { get; set; }
    /// <summary>用户提示词</summary>
    public string? UserPrompt { get; set; }
    /// <summary>合并结果</summary>
    public string? ConsolidationResult { get; set; }
    /// <summary>Dream 结果</summary>
    public DreamResult? Result { get; set; }

    /// <summary>是否已通过门控检查</summary>
    public bool GateChecked { get; set; }
    /// <summary>是否已扫描会话</summary>
    public bool SessionsScanned { get; set; }
    /// <summary>是否已注册任务</summary>
    public bool TaskRegistered { get; set; }
    /// <summary>是否已构建提示词</summary>
    public bool PromptBuilt { get; set; }
    /// <summary>LLM 是否已完成</summary>
    public bool LlmCompleted { get; set; }
    /// <summary>是否已记录回合</summary>
    public bool TurnRecorded { get; set; }
    /// <summary>任务是否已完成</summary>
    public bool TaskCompleted { get; set; }
}