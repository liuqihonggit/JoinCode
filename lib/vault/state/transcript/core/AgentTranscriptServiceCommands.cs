namespace State;

/// <summary>
/// Agent 转录服务 Actor 命令类型 — 每个命令对应一个 IAgentTranscriptService 写操作，由 AgentTranscriptActor Consumer 串行处理。
/// <para>AsyncLock+文件 I/O 迁移到 Actor 邮箱管道，消除显式锁。</para>
/// <para>读操作（LoadMetadataAsync/ListMetadataAsync/LoadTranscriptAsync）不经 Actor，直接读文件（无竞态）。</para>
/// <para>追加条目操作（AppendEntryAsync/AppendEntriesAsync）不经 Actor，由 TranscriptFileWriter 内部 AsyncLock 串行化。</para>
/// </summary>
public abstract record AgentTranscriptCommand;

/// <summary>保存 Agent 元数据 — 对应 SaveMetadataAsync</summary>
public sealed record SaveMetadataCmd(
    string SessionId,
    JoinCode.Abstractions.Interfaces.AgentMetadata Metadata,
    TaskCompletionSource Reply) : AgentTranscriptCommand;
