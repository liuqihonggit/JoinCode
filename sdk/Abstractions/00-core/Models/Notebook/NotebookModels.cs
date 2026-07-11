
namespace JoinCode.Abstractions.Models.Notebook;

/// <summary>
/// Notebook单元格类型
/// </summary>
public enum NotebookCellType
{
    [EnumValue("code")] Code,
    [EnumValue("markdown")] Markdown,
    [EnumValue("raw")] Raw
}

/// <summary>
/// Notebook单元格类型扩展方法 — 委托给源码生成器自动生成的 NotebookCellTypeExtensions
/// </summary>
public static class NotebookCellTypeHelper
{
    /// <summary>
    /// 获取单元格类型字符串
    /// </summary>
    public static string ToCellTypeString(this NotebookCellType cellType)
    {
        return NotebookCellTypeExtensions.ToValue(cellType) ?? cellType.ToString().ToLowerInvariant();
    }
}

/// <summary>
/// Notebook单元格输出类型
/// </summary>
public enum OutputType
{
    ExecuteResult,
    DisplayData,
    Stream,
    Error
}

/// <summary>
/// Notebook 编辑模式枚举 — 替代 NotebookToolHandlers 中的 "replace"/"insert"/"delete" 硬编码字符串
/// </summary>
public enum NotebookEditMode
{
    [EnumValue("replace")] Replace = 0,
    [EnumValue("insert")] Insert = 1,
    [EnumValue("delete")] Delete = 2
}

/// <summary>
/// Notebook单元格输出
/// </summary>
public sealed record NotebookOutput
{
    [JsonPropertyName("output_type")]
    public string OutputType { get; init; } = "stream";

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Text { get; init; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? Data { get; init; }

    [JsonPropertyName("execution_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExecutionCount { get; init; }

    [JsonPropertyName("ename")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorName { get; init; }

    [JsonPropertyName("evalue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorValue { get; init; }

    [JsonPropertyName("traceback")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Traceback { get; init; }
}

/// <summary>
/// Notebook单元格
/// </summary>
public sealed record NotebookCell
{
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonPropertyName("cell_type")]
    public string CellType { get; init; } = "code";

    [JsonPropertyName("source")]
    public List<string> Source { get; init; } = new();

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();

    [JsonPropertyName("outputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<NotebookOutput>? Outputs { get; init; }

    [JsonPropertyName("execution_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExecutionCount { get; init; }

    [JsonIgnore]
    public string SourceText => string.Join("", Source);

    [JsonIgnore]
    public NotebookCellType Type => CellType.ToLowerInvariant() switch
    {
        "markdown" => NotebookCellType.Markdown,
        "raw" => NotebookCellType.Raw,
        _ => NotebookCellType.Code
    };
}

/// <summary>
/// Notebook元数据
/// </summary>
public sealed record NotebookMetadata
{
    [JsonPropertyName("kernelspec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KernelSpec? KernelSpec { get; init; }

    [JsonPropertyName("language_info")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LanguageInfo? LanguageInfo { get; init; }

    [JsonPropertyName("additional_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonElement>? AdditionalData { get; init; }
}

/// <summary>
/// 内核规范
/// </summary>
public sealed record KernelSpec
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("language")]
    public string Language { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>
/// 语言信息
/// </summary>
public sealed record LanguageInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    [JsonPropertyName("mimetype")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    [JsonPropertyName("file_extension")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileExtension { get; init; }
}

/// <summary>
/// Notebook文档 (.ipynb 格式)
/// </summary>
public sealed record NotebookDocument
{
    [JsonPropertyName("nbformat")]
    public int NbFormat { get; init; } = 4;

    [JsonPropertyName("nbformat_minor")]
    public int NbFormatMinor { get; init; } = 5;

    [JsonPropertyName("metadata")]
    public NotebookMetadata Metadata { get; init; } = new();

    [JsonPropertyName("cells")]
    public List<NotebookCell> Cells { get; init; } = new();

    [JsonIgnore]
    public int CellCount => Cells.Count;

    [JsonIgnore]
    public int CodeCellCount => Cells.Count(c => c.Type == NotebookCellType.Code);

    [JsonIgnore]
    public int MarkdownCellCount => Cells.Count(c => c.Type == NotebookCellType.Markdown);
}

/// <summary>
/// Notebook编辑结果
/// </summary>
public sealed record NotebookEditResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public NotebookDocument? Notebook { get; init; }
    public int? AffectedCellIndex { get; init; }
}
