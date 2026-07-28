namespace JoinCode.Abstractions.Interfaces.Lsp;

/// <summary>
/// LSP 文件同步接口
/// 处理文件打开/关闭/变更通知，对齐 TS LSPServerManager 的 changeFile/saveFile/openFile/closeFile
/// </summary>
public interface ILspFileSync
{
    /// <summary>
    /// 打开文档（发送 textDocument/didOpen）
    /// </summary>
    Task OpenDocumentAsync(string filePath, string languageId, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 关闭文档（发送 textDocument/didClose）
    /// </summary>
    Task CloseDocumentAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 文档内容变更（发送 textDocument/didChange）
    /// 若文件未打开，自动回退为 OpenDocumentAsync（对齐 TS changeFile 的 didOpen 回退逻辑）
    /// </summary>
    Task ChangeDocumentAsync(string filePath, IEnumerable<TextDocumentContentChangeEvent> changes, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存文档（发送 textDocument/didSave）
    /// </summary>
    Task SaveDocumentAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取已打开的文档
    /// </summary>
    IReadOnlyDictionary<string, OpenDocumentInfo> GetOpenDocuments();

    /// <summary>
    /// 文档是否已打开
    /// </summary>
    bool IsDocumentOpen(string filePath);

    /// <summary>
    /// 文档变更后触发（didChange / didSave / didOpen 后均触发）
    /// </summary>
    event EventHandler<DocumentChangedEventArgs>? DocumentChanged;
}

/// <summary>
/// 文档变更事件参数
/// </summary>
public sealed class DocumentChangedEventArgs : EventArgs
{
    /// <summary>
    /// 变更的文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 变更类型
    /// </summary>
    public required DocumentChangeKind ChangeKind { get; init; }
}

/// <summary>
/// 文档变更类型
/// </summary>
public enum DocumentChangeKind
{
    /// <summary>
    /// 文档已打开
    /// </summary>
    Opened,

    /// <summary>
    /// 文档内容已变更
    /// </summary>
    Changed,

    /// <summary>
    /// 文档已保存
    /// </summary>
    Saved,

    /// <summary>
    /// 文档已关闭
    /// </summary>
    Closed
}

/// <summary>
/// 打开的文档信息
/// </summary>
public sealed record OpenDocumentInfo
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 语言 ID
    /// </summary>
    public required string LanguageId { get; init; }

    /// <summary>
    /// 当前版本
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// 当前内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 打开时间
    /// </summary>
    public DateTime OpenedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 文档内容变更事件
/// </summary>
public sealed record TextDocumentContentChangeEvent
{
    /// <summary>
    /// 变更范围（null 表示全文替换）
    /// </summary>
    public LspRange? Range { get; init; }

    /// <summary>
    /// 替换的文本
    /// </summary>
    public required string Text { get; init; }
}

/// <summary>
/// 范围 — LSP JSON-RPC 与领域模型共用规范类型
/// </summary>
public sealed record LspRange
{
    /// <summary>
    /// 起始位置
    /// </summary>
    [JsonPropertyName("start")]
    public LspPosition Start { get; init; } = new();

    /// <summary>
    /// 结束位置
    /// </summary>
    [JsonPropertyName("end")]
    public LspPosition End { get; init; } = new();
}

/// <summary>
/// 位置 — LSP JSON-RPC 与领域模型共用规范类型
/// </summary>
public sealed record LspPosition
{
    /// <summary>
    /// 行号（从 0 开始）
    /// </summary>
    [JsonPropertyName("line")]
    public int Line { get; init; }

    /// <summary>
    /// 字符位置（从 0 开始）
    /// </summary>
    [JsonPropertyName("character")]
    public int Character { get; init; }
}
