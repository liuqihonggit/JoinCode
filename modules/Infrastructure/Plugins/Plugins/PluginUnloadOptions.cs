namespace Core.Plugins;

public sealed class PluginUnloadOptions {
    public TimeSpan CooperativeTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public bool ForceAlcUnloadOnTimeout { get; init; } = true;

    public static readonly PluginUnloadOptions Default = new();

    /// <summary>
    /// 快速卸载选项（1秒超时，强制卸载）
    /// </summary>
    public static readonly PluginUnloadOptions Fast = new()
    {
        CooperativeTimeout = TimeSpan.FromSeconds(1),
        ForceAlcUnloadOnTimeout = true
    };

    /// <summary>
    /// 优雅卸载选项（10秒超时，不强制卸载）
    /// </summary>
    public static readonly PluginUnloadOptions Graceful = new()
    {
        CooperativeTimeout = TimeSpan.FromSeconds(10),
        ForceAlcUnloadOnTimeout = false
    };

    /// <summary>
    /// 强制卸载选项（立即超时，强制卸载）
    /// </summary>
    public static readonly PluginUnloadOptions Force = new()
    {
        CooperativeTimeout = TimeSpan.Zero,
        ForceAlcUnloadOnTimeout = true
    };
}

/// <summary>
/// 插件卸载选项构建器 - 支持链式配置
/// </summary>
public sealed class PluginUnloadOptionsBuilder
{
    private TimeSpan _cooperativeTimeout = TimeSpan.FromSeconds(5);
    private bool _forceAlcUnloadOnTimeout = true;

    private PluginUnloadOptionsBuilder()
    {
    }

    /// <summary>
    /// 创建新的构建器
    /// </summary>
    public static PluginUnloadOptionsBuilder Create() => new();

    /// <summary>
    /// 从默认选项开始
    /// </summary>
    public static PluginUnloadOptionsBuilder CreateDefault() => Create();

    /// <summary>
    /// 从快速卸载选项开始
    /// </summary>
    public static PluginUnloadOptionsBuilder CreateFast() => Create()
        .WithTimeout(TimeSpan.FromSeconds(1))
        .WithForceUnload(true);

    /// <summary>
    /// 从优雅卸载选项开始
    /// </summary>
    public static PluginUnloadOptionsBuilder CreateGraceful() => Create()
        .WithTimeout(TimeSpan.FromSeconds(10))
        .WithForceUnload(false);

    /// <summary>
    /// 从强制卸载选项开始
    /// </summary>
    public static PluginUnloadOptionsBuilder CreateForce() => Create()
        .WithTimeout(TimeSpan.Zero)
        .WithForceUnload(true);

    /// <summary>
    /// 设置超时时间
    /// </summary>
    public PluginUnloadOptionsBuilder WithTimeout(TimeSpan timeout)
    {
        _cooperativeTimeout = timeout;
        return this;
    }

    /// <summary>
    /// 设置超时时间（秒）
    /// </summary>
    public PluginUnloadOptionsBuilder WithTimeoutSeconds(int seconds)
    {
        _cooperativeTimeout = TimeSpan.FromSeconds(seconds);
        return this;
    }

    /// <summary>
    /// 设置超时时间（毫秒）
    /// </summary>
    public PluginUnloadOptionsBuilder WithTimeoutMilliseconds(int milliseconds)
    {
        _cooperativeTimeout = TimeSpan.FromMilliseconds(milliseconds);
        return this;
    }

    /// <summary>
    /// 启用强制卸载
    /// </summary>
    public PluginUnloadOptionsBuilder WithForceUnload(bool force)
    {
        _forceAlcUnloadOnTimeout = force;
        return this;
    }

    /// <summary>
    /// 强制卸载
    /// </summary>
    public PluginUnloadOptionsBuilder ForceUnload()
    {
        _forceAlcUnloadOnTimeout = true;
        return this;
    }

    /// <summary>
    /// 不强制卸载
    /// </summary>
    public PluginUnloadOptionsBuilder NoForceUnload()
    {
        _forceAlcUnloadOnTimeout = false;
        return this;
    }

    /// <summary>
    /// 立即卸载（零超时）
    /// </summary>
    public PluginUnloadOptionsBuilder Immediate()
    {
        _cooperativeTimeout = TimeSpan.Zero;
        return this;
    }

    /// <summary>
    /// 构建插件卸载选项
    /// </summary>
    public PluginUnloadOptions Build()
    {
        return new PluginUnloadOptions
        {
            CooperativeTimeout = _cooperativeTimeout,
            ForceAlcUnloadOnTimeout = _forceAlcUnloadOnTimeout
        };
    }
}
