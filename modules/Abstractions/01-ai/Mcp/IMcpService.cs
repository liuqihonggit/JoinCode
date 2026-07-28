namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// MCP 服务接口
/// </summary>
public interface IMcpService
{
    /// <summary>
    /// 是否正在运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 初始化 MCP 服务，注册所有工具处理器
    /// </summary>
    Task InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);
}
