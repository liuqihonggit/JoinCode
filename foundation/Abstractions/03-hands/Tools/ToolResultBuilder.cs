namespace JoinCode.Abstractions.Tools;

public sealed class ToolResultBuilder
{
    private readonly List<ToolContent> _content = new();
    private bool _isError;
    private List<EntityMetadataEntry>? _entityMetadata;
    private ToolDiagnostic? _diagnostic;

    public static ToolResultBuilder Success() => new();

    public static ToolResultBuilder Error() => new() { _isError = true };

    /// <summary>
    /// 管道未产生结果的错误 — 统一3处重复的 "Pipeline did not produce a result" 消息
    /// </summary>
    public static ToolResult PipelineNoResult()
        => Error().WithText("Pipeline did not produce a result").Build();

    /// <summary>
    /// 安全执行工具操作 — 封装 try-catch-log-returnError 三段式，统一26+处重复模式
    /// </summary>
    public static ToolResult SafeExecute(
        Func<ToolResult> action,
        ILogger? logger,
        string operationName)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "{Operation} failed", operationName);
            return Error().WithText($"{operationName}失败: {ex.Message}").Build();
        }
    }

    /// <summary>
    /// 安全执行异步工具操作 — 封装 try-catch-log-returnError 三段式（异步版本）
    /// </summary>
    public static async Task<ToolResult> SafeExecuteAsync(
        Func<Task<ToolResult>> action,
        ILogger? logger,
        string operationName)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "{Operation} failed", operationName);
            return Error().WithText($"{operationName}失败: {ex.Message}").Build();
        }
    }

    public ToolResultBuilder WithText(string text)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Text, Text = text });
        return this;
    }

    public ToolResultBuilder WithImage(string base64Data, string mediaType)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Image, Data = base64Data, MimeType = mediaType });
        return this;
    }

    /// <summary>
    /// 添加 PDF 文档内容（base64编码，对齐 TS FileReadTool pdf 类型）
    /// </summary>
    public ToolResultBuilder WithPdf(string base64Data, long originalSize)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Document, Data = base64Data, MimeType = "application/pdf" });
        return this;
    }

    /// <summary>
    /// 添加二进制内容引用 — 对齐 TS persistBlobToTextBlock 写盘路径
    /// </summary>
    public ToolResultBuilder WithBinary(string base64Data, string mimeType)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Resource, Data = base64Data, MimeType = mimeType });
        return this;
    }

    public ToolResultBuilder WithError(string errorMessage)
    {
        _isError = true;
        _content.Clear();
        _content.Add(new ToolContent { Type = ToolContentType.Text, Text = errorMessage });
        return this;
    }

    /// <summary>
    /// 附加工具执行实体元数据 — 用于回填子类 Entity 特有字段（如 ExitCode, HttpStatusCode）
    /// </summary>
    public ToolResultBuilder WithEntityMetadata(EntityMetadataEntry entry)
    {
        _entityMetadata ??= new();
        _entityMetadata.Add(entry);
        return this;
    }

    /// <summary>
    /// 附加结构化诊断信息 — 工具失败时调用，GUI 可根据 Reason/Details/Suggestions 分区域渲染。
    /// </summary>
    public ToolResultBuilder WithDiagnostic(ToolDiagnostic diagnostic)
    {
        _diagnostic = diagnostic;
        return this;
    }

    public ToolResult Build()
    {
        return new ToolResult
        {
            Content = _content,
            IsError = _isError,
            EntityMetadata = _entityMetadata,
            Diagnostic = _diagnostic
        };
    }
}
