namespace JoinCode.Abstractions.LLM.Chat;

public sealed class PersistedToolResult {
    /// <summary>获取文件路径。</summary>
    public required string Filepath { get; init; }
    /// <summary>获取原始大小。</summary>
    public required int OriginalSize { get; init; }
    /// <summary>获取是否为 JSON。</summary>
    public required bool IsJson { get; init; }
    /// <summary>获取预览内容。</summary>
    public required string Preview { get; init; }
    /// <summary>获取是否还有更多内容。</summary>
    public required bool HasMore { get; init; }
}