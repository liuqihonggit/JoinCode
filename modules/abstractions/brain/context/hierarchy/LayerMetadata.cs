namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

public record LayerMetadata {
    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>获取压缩时间。</summary>
    public DateTime? CompressedAt { get; init; }

    /// <summary>获取原始令牌数。</summary>
    public int OriginalTokenCount { get; init; }

    /// <summary>获取压缩后令牌数。</summary>
    public int CompressedTokenCount { get; init; }

    /// <summary>获取压缩比率。</summary>
    public double CompressionRatio => OriginalTokenCount > 0
        ? (CompressedTokenCount > 0 ? (double)CompressedTokenCount / OriginalTokenCount : 1.0)
        : 1.0;

    /// <summary>获取层级名称。</summary>
    public string LayerName { get; init; } = string.Empty;

    /// <summary>构造层级元数据默认实例。</summary>
    public LayerMetadata() { }

    /// <summary>构造层级元数据。</summary>
    public LayerMetadata(string layerName) {
        LayerName = layerName;
    }

    /// <summary>返回应用压缩后的新实例。</summary>
    public LayerMetadata WithCompression(int compressedTokenCount) {
        return this with {
            CompressedAt = DateTime.UtcNow,
            CompressedTokenCount = compressedTokenCount
        };
    }
}