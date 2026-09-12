namespace Core.Telemetry;

/// <summary>
/// 遥测插件 — 根据配置启用控制台导出器
/// <para>万物皆插件(ADR 0098): 从 TelemetryService 构造时硬编码迁移为插件加载</para>
/// <para>卸载时禁用导出器,实现可逆效应</para>
/// </summary>
[Register(typeof(IWorkflowPlugin), ServiceLifetime.Singleton)]
public sealed partial class TelemetryPlugin : WorkflowPluginBase
{
    private TelemetryService? _telemetryService;

    public TelemetryPlugin() : base("Telemetry") { }

    /// <summary>插件名称</summary>
    public override string Name => "Telemetry";

    /// <summary>插件版本</summary>
    public override string Version => "1.0.0";

    /// <summary>插件描述</summary>
    public override string Description => "遥测控制台导出器插件";

    /// <summary>加载插件 — 无副作用,仅返回成功</summary>
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <summary>初始化插件 — 根据遥测配置启用控制台导出器</summary>
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        _telemetryService = serviceProvider.GetRequiredService<TelemetryService>();
        if (_telemetryService.Config.ExportFormat == TelemetryExportFormat.Console)
        {
            var logger = serviceProvider.GetService<ILogger<TelemetryService>>();
            _telemetryService.EnableConsoleExporter(logger);
        }
        return Task.FromResult(OperationResult.Ok());
    }

    /// <summary>插件特定清理 — 禁用控制台导出器</summary>
    protected override void OnUnload()
    {
        _telemetryService?.DisableConsoleExporter();
        _telemetryService = null;
    }
}
