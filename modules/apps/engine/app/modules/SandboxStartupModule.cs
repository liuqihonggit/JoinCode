namespace JoinCode.App.Modules;

/// <summary>
/// 沙箱启动校验模块 — 落实 <c>sandbox.failIfUnavailable</c>：
/// 启用沙箱但当前环境跑不起来（平台不支持 / 无真隔离提供器）时，
/// <c>failIfUnavailable=true</c> 拒绝启动，否则显式警告（不静默忽略安全配置）。
/// </summary>
[AppModule]
public sealed class SandboxStartupModule : IAppModule {
    /// <summary>
    /// 服务注册 — 本模块只用启动后校验，不注册新服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="context">应用模块上下文。</param>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
    }

    /// <summary>
    /// 启动后校验沙箱可用性 — 对齐基准 <c>sandbox-adapter.ts:562-592</c>。
    /// </summary>
    /// <param name="services">服务提供者。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>已完成的任务；不可用且要求硬失败时抛出。</returns>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct) {
        var config = services.GetService<WorkflowConfig>();
        if (config is null || !config.Sandbox.Enabled) {
            return Task.CompletedTask;
        }

        var sandboxManager = services.GetService<ISandboxManager>();
        var decision = SandboxStartupGate.Evaluate(
            config.Sandbox,
            platformSupported: SandboxGate.IsPlatformSupported(SandboxGate.CurrentPlatform, config.Sandbox.EnabledPlatforms),
            realIsolationAvailable: sandboxManager.HasRealIsolationProvider());

        switch (decision.Outcome) {
            case SandboxStartupOutcome.Fail:
                throw new InvalidOperationException(decision.Message);
            case SandboxStartupOutcome.Warn:
                Diag.WriteLine($"[SANDBOX] startup warning: {decision.Message}");
                services.GetService<ILogger<SandboxStartupModule>>()?.LogWarning("{Message}", decision.Message);
                break;
            default:
                Diag.WriteLine("[SANDBOX] startup check ok");
                break;
        }

        return Task.CompletedTask;
    }
}
