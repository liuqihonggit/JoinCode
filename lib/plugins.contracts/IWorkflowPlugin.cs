
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
    /// 加载插件 — 副作用唯一入口 PluginContext,对齐 Cordis ctx
    /// <para>通过 ctx.RegisterService/ConfigureServices/Effect 注册副作用,撤销链自动收集</para>
    /// </summary>
    Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default);

    /// <summary>
    /// 初始化插件 - 获取服务依赖
    /// </summary>
    Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);

    /// <summary>
    /// 卸载插件 - 释放资源
    /// </summary>
    PluginUnloadResult Unload();
}

/// <summary>
/// 插件卸载结果
/// </summary>
public sealed class PluginUnloadResult
{
    /// <summary>
    /// 卸载状态
    /// </summary>
    public PluginUnloadStatus Status { get; }

    /// <summary>
    /// 插件名称
    /// </summary>
    public string PluginName { get; }

    /// <summary>
    /// 卸载耗时
    /// </summary>
    public TimeSpan ElapsedTime { get; }

    /// <summary>
    /// 错误信息（失败或超时时填充）
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 是否卸载成功
    /// </summary>
    public bool IsSuccess => Status == PluginUnloadStatus.Success;

    private PluginUnloadResult(PluginUnloadStatus status, string pluginName, TimeSpan elapsedTime, string? errorMessage = null)
    {
        Status = status;
        PluginName = pluginName;
        ElapsedTime = elapsedTime;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 创建卸载成功结果
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="elapsedTime">卸载耗时</param>
    /// <returns>卸载成功结果实例</returns>
    public static PluginUnloadResult Success(string pluginName, TimeSpan elapsedTime) =>
        new(PluginUnloadStatus.Success, pluginName, elapsedTime);

    /// <summary>
    /// 创建卸载失败结果
    /// </summary>
    /// <param name="errorMessage">错误信息</param>
    /// <returns>卸载失败结果实例</returns>
    public static PluginUnloadResult Failure(string errorMessage) =>
        new(PluginUnloadStatus.AlcUnloadFailed, string.Empty, TimeSpan.Zero, errorMessage);

    /// <summary>
    /// 创建协作式卸载超时结果（已回退到 ALC 强制卸载）
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="elapsedTime">卸载耗时</param>
    /// <returns>协作式卸载超时结果实例</returns>
    public static PluginUnloadResult CooperativeTimeout(string pluginName, TimeSpan elapsedTime) =>
        new(PluginUnloadStatus.CooperativeTimeout, pluginName, elapsedTime, "协作式卸载超时，已回退到ALC强制卸载");

    /// <summary>
    /// 创建 ALC 卸载失败结果
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <param name="elapsedTime">卸载耗时</param>
    /// <param name="errorMessage">错误信息</param>
    /// <returns>ALC 卸载失败结果实例</returns>
    public static PluginUnloadResult AlcUnloadFailed(string pluginName, TimeSpan elapsedTime, string errorMessage) =>
        new(PluginUnloadStatus.AlcUnloadFailed, pluginName, elapsedTime, errorMessage);

    /// <summary>
    /// 创建插件已卸载结果（重复卸载场景）
    /// </summary>
    /// <param name="pluginName">插件名称</param>
    /// <returns>已卸载结果实例</returns>
    public static PluginUnloadResult AlreadyUnloaded(string pluginName) =>
        new(PluginUnloadStatus.AlreadyUnloaded, pluginName, TimeSpan.Zero);
}

/// <summary>
/// 插件卸载状态
/// </summary>
public enum PluginUnloadStatus
{
    /// <summary>
    /// 卸载成功
    /// </summary>
    Success,

    /// <summary>
    /// 协作式卸载超时（已回退到 ALC 强制卸载）
    /// </summary>
    CooperativeTimeout,

    /// <summary>
    /// ALC 卸载失败
    /// </summary>
    AlcUnloadFailed,

    /// <summary>
    /// 插件已卸载（重复卸载）
    /// </summary>
    AlreadyUnloaded
}
