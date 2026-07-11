
namespace Core.Configuration;

/// <summary>
/// 上下文压缩配置 - 用于 Agent 基类的上下文压缩行为配置
/// </summary>
[RegisterOptions]
public sealed class ContextCompressionConfig
{
    /// <summary>
    /// 是否启用自动压缩
    /// 默认值: true
    /// </summary>
    public bool EnableAutoCompression { get; set; } = true;

    /// <summary>
    /// Token 阈值，超过此值触发自动压缩
    /// 默认值: 4000
    /// </summary>
    [Range(100, 128000)]
    public int TokenThreshold { get; set; } = 4000;

    /// <summary>
    /// 默认压缩比例 (0.0 - 1.0)
    /// 默认值: 0.5 (保留50%的内容)
    /// </summary>
    [Range(0.01, 0.99)]
    public double DefaultCompressionRatio { get; set; } = 0.5;

    /// <summary>
    /// 最大上下文层数
    /// 默认值: 3 (Detailed, Summary, Index)
    /// </summary>
    [Range(1, 10)]
    public int MaxContextLayers { get; set; } = 3;

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public static ContextCompressionConfig Default => new();

    /// <summary>
    /// 创建禁用压缩的配置
    /// </summary>
    public static ContextCompressionConfig Disabled => new()
    {
        EnableAutoCompression = false
    };

    /// <summary>
    /// 转换为 ContextHierarchyOptions
    /// </summary>
    public ContextHierarchyOptions ToHierarchyOptions()
    {
        return new ContextHierarchyOptions
        {
            TokenThreshold = TokenThreshold,
            AutoCompressionEnabled = EnableAutoCompression,
            MaxLayers = MaxContextLayers,
            DefaultCompressionRatio = DefaultCompressionRatio
        };
    }
}

/// <summary>
/// 上下文压缩配置构建器 - 支持链式配置
/// </summary>
public sealed class ContextCompressionConfigBuilder
{
    private bool _enableAutoCompression = true;
    private int _tokenThreshold = 4000;
    private double _defaultCompressionRatio = 0.5;
    private int _maxContextLayers = 3;

    private ContextCompressionConfigBuilder()
    {
    }

    /// <summary>
    /// 创建新的构建器
    /// </summary>
    public static ContextCompressionConfigBuilder Create() => new();

    /// <summary>
    /// 从默认配置开始
    /// </summary>
    public static ContextCompressionConfigBuilder CreateFromDefault() => Create();

    /// <summary>
    /// 创建禁用压缩的配置
    /// </summary>
    public static ContextCompressionConfigBuilder CreateDisabled() => Create()
        .DisableAutoCompression();

    /// <summary>
    /// 启用自动压缩
    /// </summary>
    public ContextCompressionConfigBuilder EnableAutoCompression()
    {
        _enableAutoCompression = true;
        return this;
    }

    /// <summary>
    /// 禁用自动压缩
    /// </summary>
    public ContextCompressionConfigBuilder DisableAutoCompression()
    {
        _enableAutoCompression = false;
        return this;
    }

    /// <summary>
    /// 设置是否启用自动压缩
    /// </summary>
    public ContextCompressionConfigBuilder WithAutoCompression(bool enable)
    {
        _enableAutoCompression = enable;
        return this;
    }

    /// <summary>
    /// 设置 Token 阈值
    /// </summary>
    public ContextCompressionConfigBuilder WithTokenThreshold(int threshold)
    {
        _tokenThreshold = threshold;
        return this;
    }

    /// <summary>
    /// 设置默认压缩比例
    /// </summary>
    public ContextCompressionConfigBuilder WithCompressionRatio(double ratio)
    {
        _defaultCompressionRatio = ratio;
        return this;
    }

    /// <summary>
    /// 设置最大上下文层数
    /// </summary>
    public ContextCompressionConfigBuilder WithMaxContextLayers(int layers)
    {
        _maxContextLayers = layers;
        return this;
    }

    /// <summary>
    /// 使用轻量级压缩配置
    /// </summary>
    public ContextCompressionConfigBuilder UseLightweightConfig()
    {
        _tokenThreshold = 2000;
        _defaultCompressionRatio = 0.7;
        _maxContextLayers = 2;
        return this;
    }

    /// <summary>
    /// 使用标准压缩配置
    /// </summary>
    public ContextCompressionConfigBuilder UseStandardConfig()
    {
        _tokenThreshold = 4000;
        _defaultCompressionRatio = 0.5;
        _maxContextLayers = 3;
        return this;
    }

    /// <summary>
    /// 使用激进压缩配置
    /// </summary>
    public ContextCompressionConfigBuilder UseAggressiveConfig()
    {
        _tokenThreshold = 8000;
        _defaultCompressionRatio = 0.3;
        _maxContextLayers = 5;
        return this;
    }

    /// <summary>
    /// 使用保守压缩配置（高阈值，低压缩）
    /// </summary>
    public ContextCompressionConfigBuilder UseConservativeConfig()
    {
        _tokenThreshold = WorkflowConstants.ContextCompression.DefaultTokenThreshold;
        _defaultCompressionRatio = 0.8;
        _maxContextLayers = 2;
        return this;
    }

    /// <summary>
    /// 构建上下文压缩配置
    /// </summary>
    public ContextCompressionConfig Build()
    {
        return new ContextCompressionConfig
        {
            EnableAutoCompression = _enableAutoCompression,
            TokenThreshold = _tokenThreshold,
            DefaultCompressionRatio = _defaultCompressionRatio,
            MaxContextLayers = _maxContextLayers
        };
    }
}
