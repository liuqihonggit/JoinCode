
namespace JoinCode.Abstractions.Models.Notebook;

/// <summary>
/// Notebook单元格类型
/// </summary>
public enum NotebookCellType {
    [EnumValue("code")] Code,
    [EnumValue("markdown")] Markdown,
    [EnumValue("raw")] Raw
}

/// <summary>
/// Notebook单元格类型扩展方法 — 委托给源码生成器自动生成的 NotebookCellTypeExtensions
/// </summary>
public static class NotebookCellTypeHelper {
    /// <summary>
    /// 获取单元格类型字符串
    /// </summary>
    public static string ToCellTypeString(this NotebookCellType cellType) {
        return NotebookCellTypeExtensions.ToValue(cellType) ?? cellType.ToString().ToLowerInvariant();
    }
}

/// <summary>
/// Notebook单元格输出类型
/// </summary>
public enum OutputType {
    [EnumValue("execute_result")] ExecuteResult,
    [EnumValue("display_data")] DisplayData,
    [EnumValue("stream")] Stream,
    [EnumValue("error")] Error
}

/// <summary>
/// Notebook 编辑模式枚举 — 替代 NotebookToolHandlers 中的 "replace"/"insert"/"delete" 硬编码字符串
/// </summary>
public enum NotebookEditMode {
    [EnumValue("replace")] Replace = 0,
    [EnumValue("insert")] Insert = 1,
    [EnumValue("delete")] Delete = 2
}

/// <summary>
/// Notebook单元格输出
/// </summary>
public sealed record NotebookOutput {
    /// <summary>获取输出类型。</summary>
    [JsonPropertyName("output_type")]
    public string OutputType { get; init; } = global::JoinCode.Abstractions.Models.Notebook.OutputType.Stream.ToValue();

    /// <summary>获取输出名称（stream 类型时为 stdout/stderr）。</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>获取输出文本行列表。</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Text { get; init; }

    /// <summary>获取输出数据（MIME 类型到内容的映射）。</summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Data { get; init; }

    /// <summary>获取执行计数。</summary>
    [JsonPropertyName("execution_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExecutionCount { get; init; }

    /// <summary>获取错误名称（error 类型时有效）。</summary>
    [JsonPropertyName("ename")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorName { get; init; }

    /// <summary>获取错误值（error 类型时有效）。</summary>
    [JsonPropertyName("evalue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorValue { get; init; }

    /// <summary>获取错误回溯信息行列表。</summary>
    [JsonPropertyName("traceback")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Traceback { get; init; }
}

/// <summary>
/// Notebook单元格
/// </summary>
public sealed record NotebookCell {
    /// <summary>获取单元格标识。</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    /// <summary>获取单元格类型（code/markdown/raw）。</summary>
    [JsonPropertyName("cell_type")]
    public string CellType { get; init; } = NotebookCellType.Code.ToValue();

    /// <summary>获取源代码行列表。</summary>
    [JsonPropertyName("source")]
    public List<string> Source { get; init; } = new();

    /// <summary>获取单元格元数据。</summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();

    /// <summary>获取输出列表（仅 code 单元格有效）。</summary>
    [JsonPropertyName("outputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NotebookOutput>? Outputs { get; init; }

    /// <summary>获取执行计数（仅 code 单元格有效）。</summary>
    [JsonPropertyName("execution_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExecutionCount { get; init; }

    /// <summary>获取源代码文本（合并所有行）。</summary>
    [JsonIgnore]
    public string SourceText => string.Join("", Source);

    /// <summary>获取单元格类型枚举。</summary>
    [JsonIgnore]
    public NotebookCellType Type {
        get {
            var lower = CellType.ToLowerInvariant();
            if (string.Equals(lower, NotebookCellType.Markdown.ToValue(), StringComparison.Ordinal))
                return NotebookCellType.Markdown;
            if (string.Equals(lower, NotebookCellType.Raw.ToValue(), StringComparison.Ordinal))
                return NotebookCellType.Raw;
            return NotebookCellType.Code;
        }
    }
}

/// <summary>
/// Notebook元数据
/// </summary>
public sealed record NotebookMetadata {
    /// <summary>获取内核规范。</summary>
    [JsonPropertyName("kernelspec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KernelSpec? KernelSpec { get; init; }

    /// <summary>获取语言信息。</summary>
    [JsonPropertyName("language_info")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LanguageInfo? LanguageInfo { get; init; }

    /// <summary>获取附加元数据。</summary>
    [JsonPropertyName("additional_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

/// <summary>
/// 内核规范
/// </summary>
public sealed record KernelSpec {
    /// <summary>获取内核显示名称。</summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "";

    /// <summary>获取内核语言。</summary>
    [JsonPropertyName("language")]
    public string Language { get; init; } = "";

    /// <summary>获取内核名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>
/// 语言信息
/// </summary>
public sealed record LanguageInfo {
    /// <summary>获取语言名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>获取语言版本。</summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    /// <summary>获取 MIME 类型。</summary>
    [JsonPropertyName("mimetype")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    /// <summary>获取文件扩展名。</summary>
    [JsonPropertyName("file_extension")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileExtension { get; init; }
}

/// <summary>
/// Notebook文档 (.ipynb 格式)
/// </summary>
public sealed record NotebookDocument {
    /// <summary>获取 nbformat 主版本号。</summary>
    [JsonPropertyName("nbformat")]
    public int NbFormat { get; init; } = 4;

    /// <summary>获取 nbformat 次版本号。</summary>
    [JsonPropertyName("nbformat_minor")]
    public int NbFormatMinor { get; init; } = 5;

    /// <summary>获取 Notebook 元数据。</summary>
    [JsonPropertyName("metadata")]
    public NotebookMetadata Metadata { get; init; } = new();

    /// <summary>获取单元格列表。</summary>
    [JsonPropertyName("cells")]
    public List<NotebookCell> Cells { get; init; } = new();

    /// <summary>获取单元格总数。</summary>
    [JsonIgnore]
    public int CellCount => Cells.Count;

    /// <summary>获取代码单元格数量。</summary>
    [JsonIgnore]
    public int CodeCellCount => Cells.Count(c => c.Type == NotebookCellType.Code);

    /// <summary>获取 Markdown 单元格数量。</summary>
    [JsonIgnore]
    public int MarkdownCellCount => Cells.Count(c => c.Type == NotebookCellType.Markdown);
}

/// <summary>
/// Notebook编辑结果
/// </summary>
public sealed record NotebookEditResult {
    /// <summary>获取是否成功。</summary>
    public bool Success { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>获取 Notebook 文档。</summary>
    public NotebookDocument? Notebook { get; init; }
    /// <summary>获取受影响的单元格索引。</summary>
    public int? AffectedCellIndex { get; init; }

    /// <summary>获取 Notebook，操作失败时抛出异常</summary>
    public NotebookDocument GetNotebook() =>
        Notebook ?? throw new InvalidOperationException("Notebook is not available. Check Success before calling this method.");
}