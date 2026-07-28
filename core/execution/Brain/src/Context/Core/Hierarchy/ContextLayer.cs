namespace Core.Context;

/// <summary>
/// 上下文层实现类，支持三种层类型的不同行为
/// </summary>
public sealed partial class ContextLayer : IContextLayer
{
    private const int SummaryTargetRatio = 4;
    private const int IndexTargetRatio = 10;
    private const int DefaultSummaryLength = 200;
    private const int DefaultIndexLength = 50;

    /// <summary>
    /// 获取上下文层类型
    /// </summary>
    public ContextLayerType LayerType { get; }

    /// <summary>
    /// 获取层元数据
    /// </summary>
    public LayerMetadata Metadata { get; private set; }

    /// <summary>
    /// 获取或设置层内容
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// 获取当前 Token 数量（按字符估算）
    /// </summary>
    public int TokenCount => EstimateTokenCount(Content);

    /// <summary>
    /// 获取是否已压缩
    /// </summary>
    public bool IsCompressed => Metadata.CompressedAt.HasValue;

    /// <summary>
    /// 获取原始内容（压缩前）
    /// </summary>
    [JsonIgnore]
    public string? OriginalContent { get; private set; }

    /// <summary>
    /// 创建新的上下文层
    /// </summary>
    /// <param name="layerType">层类型</param>
    /// <param name="content">层内容</param>
    /// <param name="layerName">层名称</param>
    public ContextLayer(ContextLayerType layerType, string content, string? layerName = null)
    {
        LayerType = layerType;
        Content = content ?? string.Empty;
        Metadata = new LayerMetadata(layerName ?? $"Layer_{layerType}_{Guid.NewGuid():N}")
        {
            OriginalTokenCount = TokenCount
        };
    }

    /// <summary>
    /// 用于 JSON 反序列化的构造函数
    /// </summary>
    [JsonConstructor]
    public ContextLayer(ContextLayerType layerType, LayerMetadata metadata, string content)
    {
        LayerType = layerType;
        Metadata = metadata;
        Content = content;
    }

    /// <summary>
    /// 压缩当前层内容
    /// </summary>
    /// <returns>压缩后的上下文层</returns>
    public IContextLayer Compress()
    {
        if (IsCompressed || LayerType == ContextLayerType.Index)
        {
            return this;
        }

        OriginalContent = Content;
        var compressedContent = CompressContent(Content, LayerType);
        var compressedTokenCount = EstimateTokenCount(compressedContent);

        Metadata = Metadata.WithCompression(compressedTokenCount);
        Content = compressedContent;

        return this;
    }

    /// <summary>
    /// 解压当前层内容
    /// </summary>
    /// <returns>解压后的上下文层</returns>
    public IContextLayer Decompress()
    {
        if (!IsCompressed || OriginalContent == null)
        {
            return this;
        }

        Content = OriginalContent;
        OriginalContent = null;
        Metadata = new LayerMetadata(Metadata.LayerName)
        {
            OriginalTokenCount = Metadata.OriginalTokenCount,
            CreatedAt = Metadata.CreatedAt
        };

        return this;
    }

    /// <summary>
    /// 获取层内容摘要
    /// </summary>
    /// <returns>摘要字符串</returns>
    public string GetSummary()
    {
        return LayerType switch
        {
            ContextLayerType.Detailed => $"[详细] {Metadata.LayerName}: {TokenCount} tokens",
            ContextLayerType.Summary => $"[摘要] {Metadata.LayerName}: {TokenCount} tokens (压缩比: {Metadata.CompressionRatio:P1})",
            ContextLayerType.Index => $"[索引] {Metadata.LayerName}: {TokenCount} tokens",
            _ => $"[未知] {Metadata.LayerName}"
        };
    }

    /// <summary>
    /// 将层序列化为 JSON 字符串
    /// </summary>
    /// <returns>JSON 字符串</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, ContextJsonContext.Default.ContextLayer);
    }

    /// <summary>
    /// 从 JSON 字符串反序列化层
    /// </summary>
    /// <param name="json">JSON 字符串</param>
    /// <returns>上下文层实例，如果 JSON 无效则返回 null</returns>
    public static ContextLayer? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ContextDefaultJsonContext.Default.ContextLayer);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 创建详细层
    /// </summary>
    /// <param name="content">详细内容</param>
    /// <param name="layerName">层名称</param>
    /// <returns>详细层实例</returns>
    public static ContextLayer CreateDetailed(string content, string? layerName = null)
    {
        return new ContextLayer(ContextLayerType.Detailed, content, layerName);
    }

    /// <summary>
    /// 创建摘要层
    /// </summary>
    /// <param name="content">摘要内容</param>
    /// <param name="layerName">层名称</param>
    /// <returns>摘要层实例</returns>
    public static ContextLayer CreateSummary(string content, string? layerName = null)
    {
        return new ContextLayer(ContextLayerType.Summary, content, layerName);
    }

    /// <summary>
    /// 创建索引层
    /// </summary>
    /// <param name="content">索引内容</param>
    /// <param name="layerName">层名称</param>
    /// <returns>索引层实例</returns>
    public static ContextLayer CreateIndex(string content, string? layerName = null)
    {
        return new ContextLayer(ContextLayerType.Index, content, layerName);
    }

    /// <summary>
    /// 估算 Token 数量（简单字符数除以 4 的估算方法）
    /// </summary>
    private static int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return text.Length / 4 + (text.Length % 4 > 0 ? 1 : 0);
    }

    /// <summary>
    /// 根据层类型压缩内容
    /// </summary>
    private static string CompressContent(string content, ContextLayerType targetType)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var targetLength = targetType switch
        {
            ContextLayerType.Detailed => DefaultSummaryLength * 2,
            ContextLayerType.Summary => DefaultSummaryLength,
            ContextLayerType.Index => DefaultIndexLength,
            _ => content.Length
        };

        if (content.Length <= targetLength)
        {
            return content;
        }

        var prefix = content[..(targetLength / 2)];
        var suffix = content[^(targetLength / 2)..];

        return $"{prefix}...[压缩内容 {content.Length - targetLength} 字符]...{suffix}";
    }
}
