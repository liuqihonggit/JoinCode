namespace Core.Plugins;

/// <summary>
/// 工具修正钩子插件 — 注册 GhPrBody/Json/GhTimeout 三个默认修正器
/// <para>万物皆插件(ADR 0098): 从 ToolFixHookRegistry 构造时硬编码迁移为插件加载</para>
/// <para>卸载时撤销注册,实现可逆效应</para>
/// </summary>
[Register(typeof(IWorkflowPlugin), ServiceLifetime.Singleton)]
public sealed partial class FixHooksPlugin : WorkflowPluginBase
{
    private ToolFixHookRegistry? _registry;

    public FixHooksPlugin() : base("FixHooks") { }

    /// <summary>插件名称</summary>
    public override string Name => "FixHooks";

    /// <summary>插件版本</summary>
    public override string Version => "1.0.0";

    /// <summary>插件描述</summary>
    public override string Description => "工具修正钩子插件(GhPrBody/Json/GhTimeout)";

    /// <summary>加载插件 — 无副作用,仅返回成功</summary>
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult.Ok());

    /// <summary>初始化插件 — 从 DI 获取 ToolFixHookRegistry,注册默认修正器</summary>
    public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        _registry = serviceProvider.GetRequiredService<ToolFixHookRegistry>();
        _registry.RegisterDefaultFixHooks();
        return Task.FromResult(OperationResult.Ok());
    }

    /// <summary>插件特定清理 — 撤销三个默认修正器的注册</summary>
    protected override void OnUnload()
    {
        if (_registry is null) return;
        _registry.Unregister("GhPrBodyFixHook");
        _registry.Unregister("JsonFixHook");
        _registry.Unregister("GhTimeoutFixHook");
        _registry = null;
    }
}
