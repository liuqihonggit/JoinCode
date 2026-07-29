namespace JoinCode.Cli;

/// <summary>
/// diff 行 — 对齐 TS diff 库的行前缀
/// </summary>
public sealed record DiffLine(PatchLineType Type, string Content, int? OldLineNumber, int? NewLineNumber);

/// <summary>
/// 结构化 hunk — 对齐 TS StructuredPatchHunk
/// </summary>
public sealed record StructuredHunk(
    int OldStart,
    int OldLines,
    int NewStart,
    int NewLines,
    string Header,
    DiffLine[] Lines);

/// <summary>
/// diff 统计汇总 — 对齐 TS DiffStats
/// </summary>
public sealed record DiffStats(
    int FilesCount,
    int LinesAdded,
    int LinesRemoved);

/// <summary>
/// 单个文件在某个 Turn 中的 diff — 对齐 TS TurnFileDiff
/// </summary>
public sealed record TurnFileDiff(
    string FilePath,
    StructuredHunk[] Hunks,
    bool IsNewFile,
    int LinesAdded,
    int LinesRemoved);

/// <summary>
/// 单个对话轮次的 diff — 对齐 TS TurnDiff
/// </summary>
public sealed record TurnDiff(
    int TurnIndex,
    string? UserPromptPreview,
    DateTimeOffset Timestamp,
    Dictionary<string, TurnFileDiff> Files,
    DiffStats Stats);

/// <summary>
/// diff 数据聚合 — 对齐 TS DiffData
/// </summary>
public sealed record DiffData(
    DiffStats? Stats,
    List<DiffFileStats> Files,
    Dictionary<string, StructuredHunk[]> Hunks,
    bool Loading);

/// <summary>
/// 单个文件的 diff 统计信息 — 对齐 TS DiffFile
/// </summary>
public sealed record DiffFileStats(
    string Path,
    int LinesAdded,
    int LinesRemoved,
    bool IsBinary = false,
    bool IsLargeFile = false,
    bool IsTruncated = false,
    bool IsNewFile = false,
    bool IsUntracked = false);
