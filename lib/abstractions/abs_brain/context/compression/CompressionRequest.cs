namespace JoinCode.Abstractions.Brain.Context.Compression;

public sealed record CompressionRequest {
    /// <summary>获取或设置目标上下文层类型。</summary>
    public ContextLayerType TargetLayer { get; init; } = ContextLayerType.Summary;

    /// <summary>获取或设置压缩级别。</summary>
    public int CompressionLevel { get; init; } = 3;

    /// <summary>获取或设置保留关键词列表。</summary>
    public List<string> PreserveKeywords { get; init; } = new();

    /// <summary>获取或设置最大输出 Token 数。</summary>
    public int MaxOutputTokens { get; init; } = 4000;

    /// <summary>获取或设置内容类型。</summary>
    public ContentType ContentType { get; init; } = ContentType.Text;

    /// <summary>获取或设置是否保留关键决策。</summary>
    public bool PreserveKeyDecisions { get; init; } = true;

    /// <summary>获取或设置是否保留签名。</summary>
    public bool PreserveSignatures { get; init; } = true;

    /// <summary>获取或设置是否保留导入。</summary>
    public bool PreserveImports { get; init; } = true;

    /// <summary>获取或设置是否使用摘要。</summary>
    public bool UseSummarization { get; init; } = true;

    /// <summary>获取或设置 Token 阈值。</summary>
    public int TokenThreshold { get; init; } = 8000;

    /// <summary>获取或设置最小压缩阈值。</summary>
    public int MinCompressionThreshold { get; init; } = WorkflowConstants.ContextCompression.MinCompressionThreshold;

    /// <summary>获取请求标识。</summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>获取目标压缩比率。</summary>
    public double GetTargetCompressionRatio() => CompressionLevel switch {
        1 => 0.8,
        2 => 0.6,
        3 => 0.5,
        4 => 0.35,
        5 => 0.2,
        _ => 0.5
    };

    /// <summary>转换为压缩选项。</summary>
    public CompressionOptions ToCompressionOptions() {
        return new CompressionOptions {
            TargetCompressionRatio = GetTargetCompressionRatio(),
            MaxOutputTokens = MaxOutputTokens,
            PreserveSignatures = PreserveSignatures,
            PreserveImports = PreserveImports,
            UseSummarization = UseSummarization,
            PreserveKeyDecisions = PreserveKeyDecisions,
            MinCompressionThreshold = MinCompressionThreshold
        };
    }

    /// <summary>创建轻度压缩请求。</summary>
    public static CompressionRequest Light(ContextLayerType targetLayer = ContextLayerType.Summary) {
        return new CompressionRequest {
            TargetLayer = targetLayer,
            CompressionLevel = 1,
            MaxOutputTokens = 8000,
            UseSummarization = false
        };
    }

    /// <summary>创建标准压缩请求。</summary>
    public static CompressionRequest Standard(ContextLayerType targetLayer = ContextLayerType.Summary) {
        return new CompressionRequest {
            TargetLayer = targetLayer,
            CompressionLevel = 3,
            MaxOutputTokens = 4000,
            UseSummarization = true
        };
    }

    /// <summary>创建激进压缩请求。</summary>
    public static CompressionRequest Aggressive(ContextLayerType targetLayer = ContextLayerType.Index) {
        return new CompressionRequest {
            TargetLayer = targetLayer,
            CompressionLevel = 5,
            MaxOutputTokens = 2000,
            UseSummarization = true,
            PreserveSignatures = true,
            PreserveImports = false
        };
    }

    /// <summary>创建面向代码的压缩请求。</summary>
    public static CompressionRequest ForCode(ContextLayerType targetLayer = ContextLayerType.Summary) {
        return new CompressionRequest {
            TargetLayer = targetLayer,
            CompressionLevel = 3,
            ContentType = ContentType.Code,
            MaxOutputTokens = 4000,
            PreserveSignatures = true,
            PreserveImports = true,
            UseSummarization = false
        };
    }

    /// <summary>创建面向对话的压缩请求。</summary>
    public static CompressionRequest ForDialogue(ContextLayerType targetLayer = ContextLayerType.Summary) {
        return new CompressionRequest {
            TargetLayer = targetLayer,
            CompressionLevel = 3,
            ContentType = ContentType.Dialogue,
            MaxOutputTokens = 4000,
            UseSummarization = true,
            PreserveKeyDecisions = true
        };
    }
}