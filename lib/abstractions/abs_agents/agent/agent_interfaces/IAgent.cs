
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 通用 Agent 接口 — 协调者和执行者共用
/// 身份信息通过 ObjectId 获取（ObjectId.SequenceId / ObjectId.UniqueId / ObjectId.DisplayName）
/// 角色通过 Role 属性区分：Coordinator（协调者）或 Executor（执行者）
/// 只声明行为能力：执行/暂停/恢复/取消/重置
/// </summary>
public interface IAgent : IDisposable {
    /// <summary>获取对象标识。</summary>
    ObjectId ObjectId { get; }
    /// <summary>获取代理名称。</summary>
    string Name { get; }
    /// <summary>获取代理角色。</summary>
    AgentRole Role { get; }
    /// <summary>获取执行器变体。</summary>
    ExecutorVariant? Variant { get; }
    /// <summary>获取父对象标识。</summary>
    ObjectId? ParentObjectId { get; }

    /// <summary>获取任务描述。</summary>
    string Task { get; }
    /// <summary>获取或设置任务执行状态。</summary>
    TaskExecutionStatus Status { get; set; }
    /// <summary>获取聊天历史记录。</summary>
    MessageList ChatHistory { get; }
    /// <summary>获取系统提示词。</summary>
    string? SystemPrompt { get; }
    /// <summary>获取或设置指令文本。</summary>
    string? Instruction { get; set; }

    /// <summary>异步执行任务并返回结果。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<SubAgentResult> ExecuteAsync(CancellationToken cancellationToken = default);
    /// <summary>异步流式执行任务。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    IAsyncEnumerable<AgentStreamChunk> ExecuteStreamAsync(CancellationToken cancellationToken = default);
    /// <summary>暂停执行。</summary>
    void Pause();
    /// <summary>恢复执行。</summary>
    void Resume();
    /// <summary>取消执行。</summary>
    void Cancel();
    /// <summary>重置代理状态。</summary>
    void Reset();
}