namespace Core.Hosting;

/// <summary>
/// 工作流服务接口 - 所有后台服务的基础接口
/// </summary>
public interface IWorkflowService
{
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
public enum ServiceStatus
{
    [EnumValue("stopped")] Stopped,
    [EnumValue("starting")] Starting,
    [EnumValue("running")] Running,
    [EnumValue("stopping")] Stopping,
    [EnumValue("failed")] Failed
}

public static class ServiceStatusExtensions
{
    public static string ToStatusName(this ServiceStatus status)
    {
        return status.ToString();
    }
}

/// <summary>
/// 服务事件参数
/// </summary>
public sealed class ServiceEventArgs : EventArgs
{
    public required string ServiceName { get; init; }
    public required ServiceStatus OldStatus { get; init; }
    public required ServiceStatus NewStatus { get; init; }
    public string? Message { get; init; }
    public Exception? Exception { get; init; }
}
