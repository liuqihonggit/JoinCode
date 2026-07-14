namespace JoinCode.Abstractions.Brain.Context.Hierarchy;

[RegisterOptions]
public class ContextHierarchyOptions
{
    [Range(100, 128000)]
    public int TokenThreshold { get; set; } = 4000;

    public bool AutoCompressionEnabled { get; set; } = true;

    [Range(1, 10)]
    public int MaxLayers { get; set; } = 3;

    [Range(0.01, 0.99)]
    public double DefaultCompressionRatio { get; set; } = 0.5;

    /// <summary>
    /// 默认配置
    /// </summary>
    public static ContextHierarchyOptions Default => new();

    /// <summary>
    /// 禁用压缩的配置
    /// </summary>
    public static ContextHierarchyOptions Disabled => new()
    {
        AutoCompressionEnabled = false
    };
}

public sealed class ContextHierarchyOptionsBuilder
{
    private int _tokenThreshold = 4000;
    private bool _autoCompressionEnabled = true;
    private int _maxLayers = 3;
    private double _defaultCompressionRatio = 0.5;

    private ContextHierarchyOptionsBuilder()
    {
    }

    public static ContextHierarchyOptionsBuilder Create() => new();

    public static ContextHierarchyOptionsBuilder CreateFromDefault() => Create();

    public static ContextHierarchyOptionsBuilder CreateDisabled() => Create()
        .DisableAutoCompression();

    public ContextHierarchyOptionsBuilder WithTokenThreshold(int threshold)
    {
        _tokenThreshold = threshold;
        return this;
    }

    public ContextHierarchyOptionsBuilder EnableAutoCompression()
    {
        _autoCompressionEnabled = true;
        return this;
    }

    public ContextHierarchyOptionsBuilder DisableAutoCompression()
    {
        _autoCompressionEnabled = false;
        return this;
    }

    public ContextHierarchyOptionsBuilder WithAutoCompression(bool enable)
    {
        _autoCompressionEnabled = enable;
        return this;
    }

    public ContextHierarchyOptionsBuilder WithMaxLayers(int layers)
    {
        _maxLayers = layers;
        return this;
    }

    public ContextHierarchyOptionsBuilder WithCompressionRatio(double ratio)
    {
        _defaultCompressionRatio = ratio;
        return this;
    }

    public ContextHierarchyOptionsBuilder UseLightweightMode()
    {
        _tokenThreshold = 2000;
        _maxLayers = 2;
        _defaultCompressionRatio = 0.7;
        return this;
    }

    public ContextHierarchyOptionsBuilder UseStandardMode()
    {
        _tokenThreshold = 4000;
        _maxLayers = 3;
        _defaultCompressionRatio = 0.5;
        return this;
    }

    public ContextHierarchyOptionsBuilder UseDeepMode()
    {
        _tokenThreshold = 8000;
        _maxLayers = 5;
        _defaultCompressionRatio = 0.3;
        return this;
    }

    public ContextHierarchyOptionsBuilder UseConservativeMode()
    {
        _tokenThreshold = WorkflowConstants.ContextCompression.DefaultTokenThreshold;
        _defaultCompressionRatio = 0.8;
        _maxLayers = 2;
        return this;
    }

    public ContextHierarchyOptions Build()
    {
        return new ContextHierarchyOptions
        {
            TokenThreshold = _tokenThreshold,
            AutoCompressionEnabled = _autoCompressionEnabled,
            MaxLayers = _maxLayers,
            DefaultCompressionRatio = _defaultCompressionRatio
        };
    }
}
