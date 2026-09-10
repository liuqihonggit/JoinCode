namespace Core.Plugins;

/// <summary>
/// 沙箱提供器插件 — 运行时注册 Soft/Process/Docker/Bubblewrap 四个沙箱提供器
/// <para>万物皆插件(ADR 0098): 从 ServiceRegistration 硬编码 DI 注册迁移为插件加载</para>
/// <para>卸载时从 SandboxManager 移除所有提供器,实现可逆效应</para>
/// </summary>
[Register(typeof(IWorkflowPlugin), ServiceLifetime.Singleton)]
public sealed partial class SandboxProvidersPlugin : WorkflowPluginBase
{
    private ISandboxManager? _sandboxManager;

    public SandboxProvidersPlugin() : base("SandboxProviders") { }

    /// <summary>插件名称</summary>
    public override string Name => "SandboxProviders";

    /// <summary>插件版本</summary>
    public override string Version => "1.0.0";

    /// <summary>插件描述</summary>
    public override string Description => "沙箱提供器插件(Soft/Process/Docker/Bubblewrap)";

    /// <summary>加载插件 — 无副作用,仅返回成功</summary>
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <summary>初始化插件 — 从 DI 获取依赖,注册四个沙箱提供器</summary>
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        _sandboxManager = serviceProvider.GetRequiredService<ISandboxManager>();
        var fs = serviceProvider.GetRequiredService<IFileSystem>();
        var processService = serviceProvider.GetRequiredService<IProcessService>();
        var logger = serviceProvider.GetService<ILogger<SandboxProvidersPlugin>>();
        var clock = serviceProvider.GetService<IClockService>();
        var telemetry = serviceProvider.GetService<ITelemetryService>();

        _sandboxManager.AddProvider(new SoftSandboxProvider(fs, logger is null ? null : logger as ILogger<SoftSandboxProvider>, clock, telemetry));
        _sandboxManager.AddProvider(new ProcessSandboxProvider(fs, processService, logger as ILogger<ProcessSandboxProvider>, clock, telemetry));
        _sandboxManager.AddProvider(new DockerSandboxProvider(fs, processService, logger as ILogger<DockerSandboxProvider>, clock, telemetry));
        _sandboxManager.AddProvider(new BubblewrapSandboxProvider(fs, processService, logger as ILogger<BubblewrapSandboxProvider>, clock, telemetry));
        return Task.FromResult(OperationResult.Ok());
    }

    /// <summary>插件特定清理 — 移除四个沙箱提供器</summary>
    protected override void OnUnload()
    {
        if (_sandboxManager is null) return;
        _sandboxManager.RemoveProvider(SandboxType.Soft);
        _sandboxManager.RemoveProvider(SandboxType.Process);
        _sandboxManager.RemoveProvider(SandboxType.Docker);
        _sandboxManager.RemoveProvider(SandboxType.Bubblewrap);
        _sandboxManager = null;
    }
}
