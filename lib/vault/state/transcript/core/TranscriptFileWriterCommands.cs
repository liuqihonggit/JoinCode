namespace State;

/// <summary>
/// Transcript 文件写入器 Actor 命令类型 — TASK001
/// </summary>
public abstract record TranscriptFileWriterCommand;

/// <summary>追加单条记录 — 对应 AppendEntryAsync</summary>
public sealed record AppendEntryCmd(
    string FilePath,
    TranscriptEntry Entry,
    TaskCompletionSource<Unit> Reply) : TranscriptFileWriterCommand;

/// <summary>追加多条记录 — 对应 AppendEntriesAsync</summary>
public sealed record AppendEntriesCmd(
    string FilePath,
    IReadOnlyList<TranscriptEntry> Entries,
    TaskCompletionSource<Unit> Reply) : TranscriptFileWriterCommand;
