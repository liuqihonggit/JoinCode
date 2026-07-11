
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 工作流插件接口 - 支持反射加载和生命周期管理
/// </summary>
public interface IWorkflowPlugin
{
    /// <summary>
    /// 插件名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 插件版本
    /// </summary>
    string Version { get; }

    /// <summary>
    /// 插件描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 加载插件 - 注册服务
    /// </summary>
    PluginLoadResult Load(IServiceCollection services);

    /// <summary>
    /// 初始化插件 - 获取服务依赖
    /// </summary>
    Task<PluginInitResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);

    /// <summary>
    /// 卸载插件 - 释放资源
    /// </summary>
    PluginUnloadResult Unload();
}

/// <summary>
/// 插件加载结果
/// </summary>
public sealed class PluginLoadResult
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }

    private PluginLoadResult(bool isSuccess, string? errorMessage = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public static PluginLoadResult Success() => new(true);
    public static PluginLoadResult Failure(string errorMessage) => new(false, errorMessage);
}

/// <summary>
/// 插件初始化结果
/// </summary>
public sealed class PluginInitResult
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }

    private PluginInitResult(bool isSuccess, string? errorMessage = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public static PluginInitResult Success() => new(true);
    public static PluginInitResult Failure(string errorMessage) => new(false, errorMessage);
}

/// <summary>
/// 插件卸载结果
/// </summary>
public sealed class PluginUnloadResult
{
    public PluginUnloadStatus Status { get; }
    public string PluginName { get; }
    public TimeSpan ElapsedTime { get; }
    public string? ErrorMessage { get; }
    public bool IsSuccess => Status == PluginUnloadStatus.Success;

    private PluginUnloadResult(PluginUnloadStatus status, string pluginName, TimeSpan elapsedTime, string? errorMessage = null)
    {
        Status = status;
        PluginName = pluginName;
        ElapsedTime = elapsedTime;
        ErrorMessage = errorMessage;
    }

    public static PluginUnloadResult Success(string pluginName, TimeSpan elapsedTime) =>
        new(PluginUnloadStatus.Success, pluginName, elapsedTime);

    public static PluginUnloadResult Failure(string errorMessage) =>
        new(PluginUnloadStatus.AlcUnloadFailed, string.Empty, TimeSpan.Zero, errorMessage);

    public static PluginUnloadResult CooperativeTimeout(string pluginName, TimeSpan elapsedTime) =>
        new(PluginUnloadStatus.CooperativeTimeout, pluginName, elapsedTime, "协作式卸载超时，已回退到ALC强制卸载");

    public static PluginUnloadResult AlcUnloadFailed(string pluginName, TimeSpan elapsedTime, string errorMessage) =>
        new(PluginUnloadStatus.AlcUnloadFailed, pluginName, elapsedTime, errorMessage);

    public static PluginUnloadResult AlreadyUnloaded(string pluginName) =>
        new(PluginUnloadStatus.AlreadyUnloaded, pluginName, TimeSpan.Zero);
}

/// <summary>
/// 插件卸载状态
/// </summary>
public enum PluginUnloadStatus
{
    Success,
    CooperativeTimeout,
    AlcUnloadFailed,
    AlreadyUnloaded
}
