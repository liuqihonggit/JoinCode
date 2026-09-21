namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

[RegisterOptions]
public class ContextHierarchyOptions {
    /// <summary>获取或设置触发压缩的 Token 阈值。</summary>
    [Range(100, 128000)]
    public int TokenThreshold { get; set; } = 4000;

    /// <summary>获取或设置是否启用自动压缩。</summary>
    public bool AutoCompressionEnabled { get; set; } = true;

    /// <summary>获取或设置最大层级数。</summary>
    [Range(1, 10)]
    public int MaxLayers { get; set; } = 3;

    /// <summary>获取或设置默认压缩比率。</summary>
    [Range(0.01, 0.99)]
    public double DefaultCompressionRatio { get; set; } = 0.5;

    /// <summary>
    /// 默认配置
    /// </summary>
    public static ContextHierarchyOptions Default => new();

    /// <summary>
    /// 禁用压缩的配置
    /// </summary>
    public static ContextHierarchyOptions Disabled => new() {
        AutoCompressionEnabled = false
    };
}

public sealed class ContextHierarchyOptionsBuilder {
    private int _tokenThreshold = 4000;
    private bool _autoCompressionEnabled = true;
    private int _maxLayers = 3;
    private double _defaultCompressionRatio = 0.5;

    private ContextHierarchyOptionsBuilder() {
    }

    /// <summary>创建新的构建器实例。</summary>
    public static ContextHierarchyOptionsBuilder Create() => new();

    /// <summary>基于默认配置创建构建器实例。</summary>
    public static ContextHierarchyOptionsBuilder CreateFromDefault() => Create();

    /// <summary>创建禁用自动压缩的构建器实例。</summary>
    public static ContextHierarchyOptionsBuilder CreateDisabled() => Create()
        .DisableAutoCompression();

    /// <summary>设置 Token 阈值。</summary>
    public ContextHierarchyOptionsBuilder WithTokenThreshold(int threshold) {
        _tokenThreshold = threshold;
        return this;
    }

    /// <summary>启用自动压缩。</summary>
    public ContextHierarchyOptionsBuilder EnableAutoCompression() {
        _autoCompressionEnabled = true;
        return this;
    }

    /// <summary>禁用自动压缩。</summary>
    public ContextHierarchyOptionsBuilder DisableAutoCompression() {
        _autoCompressionEnabled = false;
        return this;
    }

    /// <summary>设置是否启用自动压缩。</summary>
    public ContextHierarchyOptionsBuilder WithAutoCompression(bool enable) {
        _autoCompressionEnabled = enable;
        return this;
    }

    /// <summary>设置最大层级数。</summary>
    public ContextHierarchyOptionsBuilder WithMaxLayers(int layers) {
        _maxLayers = layers;
        return this;
    }

    /// <summary>设置压缩比率。</summary>
    public ContextHierarchyOptionsBuilder WithCompressionRatio(double ratio) {
        _defaultCompressionRatio = ratio;
        return this;
    }

    /// <summary>使用轻量模式配置。</summary>
    public ContextHierarchyOptionsBuilder UseLightweightMode() {
        _tokenThreshold = 2000;
        _maxLayers = 2;
        _defaultCompressionRatio = 0.7;
        return this;
    }

    /// <summary>使用标准模式配置。</summary>
    public ContextHierarchyOptionsBuilder UseStandardMode() {
        _tokenThreshold = 4000;
        _maxLayers = 3;
        _defaultCompressionRatio = 0.5;
        return this;
    }

    /// <summary>使用深度模式配置。</summary>
    public ContextHierarchyOptionsBuilder UseDeepMode() {
        _tokenThreshold = 8000;
        _maxLayers = 5;
        _defaultCompressionRatio = 0.3;
        return this;
    }

    /// <summary>使用保守模式配置。</summary>
    public ContextHierarchyOptionsBuilder UseConservativeMode() {
        _tokenThreshold = WorkflowConstants.ContextCompression.DefaultTokenThreshold;
        _defaultCompressionRatio = 0.8;
        _maxLayers = 2;
        return this;
    }

    /// <summary>构建 ContextHierarchyOptions 实例。</summary>
    public ContextHierarchyOptions Build() {
        return new ContextHierarchyOptions {
            TokenThreshold = _tokenThreshold,
            AutoCompressionEnabled = _autoCompressionEnabled,
            MaxLayers = _maxLayers,
            DefaultCompressionRatio = _defaultCompressionRatio
        };
    }
}
