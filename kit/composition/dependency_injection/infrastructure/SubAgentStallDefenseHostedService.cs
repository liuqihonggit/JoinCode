namespace Core.DependencyInjection;

/// <summary>
/// 子代理卡死防护启动托管服务 — 强制激活 SubAgentStallDefenseCoordinator（ADR 0106）
/// <para>
/// 协调器注册为 Singleton 但无消费方注入，DI 懒创建导致 Start() 永不调用。
/// 此托管服务在应用启动时解析协调器，触发 DI 工厂执行 coordinator.Start()。
/// </para>
/// </summary>
public sealed partial class SubAgentStallDefenseHostedService : ServiceEntity, IHostedService {
    private readonly SubAgentStallDefenseCoordinator _coordinator;
    private readonly ILogger<SubAgentStallDefenseHostedService>? _logger;

    /// <summary>
    /// 初始化 <see cref="SubAgentStallDefenseHostedService"/> 实例。
    /// </summary>
    public SubAgentStallDefenseHostedService(
        SubAgentStallDefenseCoordinator coordinator,
        ILogger<SubAgentStallDefenseHostedService>? logger = null) {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _logger = logger;
    }

    /// <summary>
    /// 应用启动时触发 — 协调器已在 DI 工厂中 Start()，此处仅记录日志确认激活。
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken) {
        _logger?.LogInformation("[SubAgentStallDefense] 托管服务已激活，纵深防御体系运行中");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 应用停止时触发 — 释放协调器资源。
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken) {
        _logger?.LogInformation("[SubAgentStallDefense] 托管服务停止，释放协调器资源");
        await _coordinator.DisposeAsync().ConfigureAwait(false);
    }
}