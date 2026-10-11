namespace Core.Hosting;

/// <summary>
/// 工作流服务接口 - 所有后台服务的基础接口
/// </summary>
public interface IWorkflowService {
    /// <summary>
    /// 服务名称
    /// </summary>
    string ServiceName { get; }

    /// <summary>
    /// 服务状态
    /// </summary>
    ServiceStatus Status { get; }

    /// <summary>
    /// 启动服务
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止服务
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 服务状态
/// </summary>
public enum ServiceStatus {
    /// <summary>已停止</summary>
    [EnumValue("stopped")] Stopped,
    /// <summary>启动中</summary>
    [EnumValue("starting")] Starting,
    /// <summary>运行中</summary>
    [EnumValue("running")] Running,
    /// <summary>停止中</summary>
    [EnumValue("stopping")] Stopping,
    /// <summary>失败</summary>
    [EnumValue("failed")] Failed
}

/// <summary>
/// 服务状态扩展方法
/// </summary>
public static class ServiceStatusExtensions {
    /// <summary>
    /// 将服务状态转换为状态名称字符串
    /// </summary>
    /// <param name="status">服务状态</param>
    /// <returns>状态名称</returns>
    public static string ToStatusName(this ServiceStatus status) {
        return status.ToString();
    }
}

/// <summary>
/// 服务事件参数
/// </summary>
public sealed class ServiceEventArgs : EventArgs {
    /// <summary>服务名称</summary>
    public required string ServiceName { get; init; }
    /// <summary>旧状态</summary>
    public required ServiceStatus OldStatus { get; init; }
    /// <summary>新状态</summary>
    public required ServiceStatus NewStatus { get; init; }
    /// <summary>附加消息，可选</summary>
    public string? Message { get; init; }
    /// <summary>异常对象，可选</summary>
    public Exception? Exception { get; init; }
}