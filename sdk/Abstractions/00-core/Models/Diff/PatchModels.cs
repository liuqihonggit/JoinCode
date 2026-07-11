namespace JoinCode.Abstractions.Models.Diff;

/// <summary>
/// Patch 行类型 — 对齐 TS diff 库的行前缀
/// </summary>
public enum PatchLineType : byte
{
    /// <summary>上下文行（前缀 ' '）</summary>
    [EnumValue("context")] Context,
    /// <summary>添加行（前缀 '+'）</summary>
    [EnumValue("added")] Added,
    /// <summary>删除行（前缀 '-'）</summary>
    [EnumValue("removed")] Removed
}

/// <summary>
/// Patch 行 — 对齐 TS StructuredPatchHunk.lines 中的单行
/// </summary>
public sealed record PatchLine
{
    /// <summary>行类型</summary>
    public required PatchLineType Type { get; init; }

    /// <summary>行内容（不含前缀）</summary>
    public required string Content { get; init; }

    /// <summary>旧文件行号（仅 Context/Removed 有值）</summary>
    public int? OldLineNumber { get; init; }

    /// <summary>新文件行号（仅 Context/Added 有值）</summary>
    public int? NewLineNumber { get; init; }
}

/// <summary>
/// 结构化 Patch Hunk — 对齐 TS StructuredPatchHunk (npm diff 库)
/// </summary>
public sealed record StructuredPatchHunk
{
    /// <summary>旧文件起始行号</summary>
    public required int OldStart { get; init; }

    /// <summary>旧文件行数</summary>
    public required int OldLines { get; init; }

    /// <summary>新文件起始行号</summary>
    public required int NewStart { get; init; }

    /// <summary>新文件行数</summary>
    public required int NewLines { get; init; }

    /// <summary>Hunk 头（如 "@@ -1,3 +1,4 @@"）</summary>
    public string Header { get; init; } = string.Empty;

    /// <summary>Diff 行数组</summary>
    public PatchLine[] Lines { get; init; } = [];
}
