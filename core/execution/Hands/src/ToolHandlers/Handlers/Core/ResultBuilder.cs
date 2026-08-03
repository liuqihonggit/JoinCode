


namespace Tools.Handlers;

/// <summary>
/// 工具结果构建器
/// </summary>
public sealed class ResultBuilder
{
    private readonly List<ToolContent> _content = new();
    private bool _isError;
    private List<EntityMetadataEntry>? _entityMetadata;

    public static ResultBuilder Success() => new();

    public static ResultBuilder Error() => new() { _isError = true };

    public ResultBuilder WithText(string text)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Text, Text = text });
        return this;
    }

    /// <summary>
    /// 添加图像内容（base64编码，对齐 TS mapToolResultToToolResultBlockParam image 类型）
    /// </summary>
    public ResultBuilder WithImage(string base64Data, string mediaType)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Image, Data = base64Data, MimeType = mediaType });
        return this;
    }

    /// <summary>
    /// 添加 PDF 文档内容（base64编码，对齐 TS FileReadTool pdf 类型）
    /// </summary>
    public ResultBuilder WithPdf(string base64Data, long originalSize)
    {
        _content.Add(new ToolContent { Type = ToolContentType.Document, Data = base64Data, MimeType = "application/pdf" });
        return this;
    }

    public ResultBuilder WithError(string errorMessage)
    {
        _isError = true;
        _content.Clear();
        _content.Add(new ToolContent { Type = ToolContentType.Text, Text = errorMessage });
        return this;
    }

    /// <summary>
    /// 附加工具执行实体元数据 — 用于回填子类 Entity 特有字段（如 ExitCode, HttpStatusCode）
    /// </summary>
    public ResultBuilder WithEntityMetadata(EntityMetadataEntry entry)
    {
        _entityMetadata ??= new();
        _entityMetadata.Add(entry);
        return this;
    }

    public ToolResult Build()
    {
        return new ToolResult
        {
            Content = _content,
            IsError = _isError,
            EntityMetadata = _entityMetadata
        };
    }
}
