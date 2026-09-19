
namespace Core.Memdir;

/// <summary>
/// 助手日志 Actor 命令类型 — 每个命令对应一个 IAssistantDailyLogService 写操作，由 DailyLogActor Consumer 串行处理。
/// <para>TASK001: AsyncLock+文件 I/O 迁移到 Actor 邮箱管道，消除显式锁。</para>
/// <para>读操作（GetDailyLogAsync/GetDailyLogForDateAsync/BuildDailyLogPromptAsync）不经 Actor，直接读文件（无共享可变状态）。</para>
/// </summary>
public abstract record AssistantDailyLogCommand;

/// <summary>追加日志条目 — 对应 AppendEntryAsync</summary>
public sealed record AppendEntryCmd(
    string Content,
    DailyLogCategory Category,
    string? RelatedMemoryId,
    TaskCompletionSource<DailyLogEntry> Reply) : AssistantDailyLogCommand;