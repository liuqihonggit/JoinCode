namespace JoinCode.Abstractions.Interfaces.Lsp;

/// <summary>
/// LSP 服务类型枚举
/// [EnumValue] 特性由 EnumMetadataGenerator 自动生成 LspServerTypeConstants + LspServerTypeExtensions
/// </summary>
public enum LspServerType
{
    /// <summary>C# 语言服务器</summary>
    [EnumValue("csharp")] CSharp,

    /// <summary>TypeScript 语言服务器</summary>
    [EnumValue("typescript")] TypeScript,

    /// <summary>Python 语言服务器</summary>
    [EnumValue("python")] Python,

    /// <summary>Rust 语言服务器</summary>
    [EnumValue("rust")] Rust,

    /// <summary>Go 语言服务器</summary>
    [EnumValue("go")] Go,

    /// <summary>Java 语言服务器</summary>
    [EnumValue("java")] Java,

    /// <summary>C++ 语言服务器</summary>
    [EnumValue("cpp")] Cpp,

    /// <summary>通用语言服务器</summary>
    [EnumValue("generic")] Generic
}

/// <summary>
/// LSP 服务配置
/// </summary>
public sealed record LspServiceConfig
{
    /// <summary>
    /// 服务器类型
    /// </summary>
    public required LspServerType ServerType { get; init; }

    /// <summary>
    /// 启动命令
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// 启动参数
    /// </summary>
    public List<string> Arguments { get; init; } = new();

    /// <summary>
    /// 关联的文件扩展名
    /// </summary>
    public List<string> FileExtensions { get; init; } = new();
}

/// <summary>
/// LSP 服务接口 — 提供语言服务器查询能力
/// </summary>
public interface ILspService : IAsyncDisposable
{
    /// <summary>
    /// 跳转到定义
    /// </summary>
    Task<List<LspLocation>> GotoDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找引用
    /// </summary>
    Task<List<LspLocation>> FindReferencesAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 悬停提示
    /// </summary>
    Task<LspHoverResult?> HoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 代码补全
    /// </summary>
    Task<List<LspCompletionItem>> GetCompletionsAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 文档符号
    /// </summary>
    Task<List<LspDocumentSymbol>> GetDocumentSymbolsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 工作区符号搜索
    /// </summary>
    Task<List<LspSymbolInformation>> SearchWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 跳转到实现
    /// </summary>
    Task<List<LspLocation>> GotoImplementationAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 准备调用层次
    /// </summary>
    Task<List<LspCallHierarchyItem>> PrepareCallHierarchyAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// 传入调用
    /// </summary>
    Task<List<LspCallHierarchyIncomingCall>> CallHierarchyIncomingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// 传出调用
    /// </summary>
    Task<List<LspCallHierarchyOutgoingCall>> CallHierarchyOutgoingCallsAsync(LspCallHierarchyItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// 关闭指定文件的客户端
    /// </summary>
    Task CloseClientAsync(string filePath, CancellationToken cancellationToken = default);
}

/// <summary>
/// LSP 位置信息
/// </summary>
public sealed record LspLocation
{
    /// <summary>
    /// 文档 URI
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>
    /// 范围
    /// </summary>
    [JsonPropertyName("range")]
    public LspRange Range { get; init; } = new();
}

/// <summary>
/// LSP 文档符号
/// </summary>
public sealed record LspDocumentSymbol
{
    /// <summary>
    /// 符号名称
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 详细信息
    /// </summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>
    /// 符号类型
    /// </summary>
    [JsonPropertyName("kind")]
    public int Kind { get; init; }

    /// <summary>
    /// 符号范围
    /// </summary>
    [JsonPropertyName("range")]
    public LspRange Range { get; init; } = new();

    /// <summary>
    /// 选中范围
    /// </summary>
    [JsonPropertyName("selectionRange")]
    public LspRange SelectionRange { get; init; } = new();

    /// <summary>
    /// 子符号
    /// </summary>
    [JsonPropertyName("children")]
    public List<LspDocumentSymbol>? Children { get; init; }
}

/// <summary>
/// LSP 悬停结果
/// </summary>
public sealed record LspHoverResult
{
    /// <summary>
    /// 内容
    /// </summary>
    [JsonPropertyName("contents")]
    public JsonElement? Contents { get; init; }

    /// <summary>
    /// 范围
    /// </summary>
    [JsonPropertyName("range")]
    public LspRange? Range { get; init; }
}

/// <summary>
/// LSP 补全项
/// </summary>
public sealed record LspCompletionItem
{
    /// <summary>
    /// 标签
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// 补全类型
    /// </summary>
    [JsonPropertyName("kind")]
    public int? Kind { get; init; }

    /// <summary>
    /// 详细信息
    /// </summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>
    /// 文档
    /// </summary>
    [JsonPropertyName("documentation")]
    public JsonElement? Documentation { get; init; }

    /// <summary>
    /// 插入文本
    /// </summary>
    [JsonPropertyName("insertText")]
    public string? InsertText { get; init; }
}

/// <summary>
/// LSP 符号信息
/// </summary>
public sealed record LspSymbolInformation
{
    /// <summary>
    /// 符号名称
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 符号类型
    /// </summary>
    [JsonPropertyName("kind")]
    public int Kind { get; init; }

    /// <summary>
    /// 位置
    /// </summary>
    [JsonPropertyName("location")]
    public LspLocation Location { get; init; } = new();

    /// <summary>
    /// 容器名称
    /// </summary>
    [JsonPropertyName("containerName")]
    public string? ContainerName { get; init; }
}

/// <summary>
/// LSP 调用层次项
/// </summary>
public sealed record LspCallHierarchyItem
{
    /// <summary>
    /// 符号名称
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// 符号类型
    /// </summary>
    [JsonPropertyName("kind")]
    public int Kind { get; init; }

    /// <summary>
    /// 文档 URI
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>
    /// 符号范围
    /// </summary>
    [JsonPropertyName("range")]
    public LspRange Range { get; init; } = new();

    /// <summary>
    /// 选中范围
    /// </summary>
    [JsonPropertyName("selectionRange")]
    public LspRange SelectionRange { get; init; } = new();

    /// <summary>
    /// 详细信息
    /// </summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}

/// <summary>
/// LSP 传入调用
/// </summary>
public sealed record LspCallHierarchyIncomingCall
{
    /// <summary>
    /// 调用方
    /// </summary>
    [JsonPropertyName("from")]
    public LspCallHierarchyItem From { get; init; } = new();

    /// <summary>
    /// 调用范围列表
    /// </summary>
    [JsonPropertyName("fromRanges")]
    public List<LspRange> FromRanges { get; init; } = new();
}

/// <summary>
/// LSP 传出调用
/// </summary>
public sealed record LspCallHierarchyOutgoingCall
{
    /// <summary>
    /// 被调用方
    /// </summary>
    [JsonPropertyName("to")]
    public LspCallHierarchyItem To { get; init; } = new();

    /// <summary>
    /// 调用范围列表
    /// </summary>
    [JsonPropertyName("fromRanges")]
    public List<LspRange> FromRanges { get; init; } = new();
}
