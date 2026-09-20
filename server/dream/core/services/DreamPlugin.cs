
namespace JoinCode.Dream;

/// <summary>
/// Dream 插件入口 - 记忆整合功能插件
/// </summary>
[Register(typeof(IWorkflowPlugin), ServiceLifetime.Singleton)]
[Register(typeof(ICommandRegistrationHook), ServiceLifetime.Singleton)]
public sealed partial class DreamPlugin : WorkflowPluginBase, ICommandRegistrationHook {
    private readonly List<string> _registeredCommandNames = new();

    /// <summary>
    /// 构造 DreamPlugin
    /// </summary>
    public DreamPlugin() : base("Dream") { }

    /// <summary>插件名称</summary>
    public override string Name => "Dream";
    /// <summary>插件版本</summary>
    public override string Version => "1.0.0";
    /// <summary>插件描述</summary>
    public override string Description => "JoinCode 记忆整合插件";

    /// <summary>
    /// 加载插件 — 注册 Dream 插件服务
    /// </summary>
    /// <param name="ctx">插件上下文</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default) {
        ctx.ConfigureServices(static s => s.AddDreamPluginServices());
        return Task.FromResult(OperationResult.Ok());
    }

    /// <summary>
    /// 初始化插件 — 加载持久化的活跃任务
    /// </summary>
    /// <param name="serviceProvider">服务提供者</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    public override async Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default) {
        var registry = serviceProvider.GetService<IDreamTaskRegistry>();
        if (registry is Persistence.PersistentDreamTaskRegistry persistentRegistry) {
            await persistentRegistry.LoadActiveTasksAsync(cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.Ok();
    }

    /// <summary>
    /// 注册命令 — 注册 dream 和 dream-tasks 命令到命令注册表
    /// </summary>
    /// <param name="registry">命令注册表</param>
    /// <param name="serviceProvider">服务提供者</param>
    public void RegisterCommands(ICommandRegistry registry, IServiceProvider serviceProvider) {
        var dreamFeature = serviceProvider.GetRequiredService<IDreamFeature>();

        registry.Register(RegisterResource(new DreamCommand(Name, dreamFeature)));
        _registeredCommandNames.Add("dream");
        UiResources.Register("menu.dream", new UiResourceEntry("menu.dream", UiResourceKind.MenuItem, "dream", "dream"));

        registry.Register(RegisterResource(new DreamTasksCommand(Name, dreamFeature)));
        _registeredCommandNames.Add("dream-tasks");
        UiResources.Register("menu.dream-tasks", new UiResourceEntry("menu.dream-tasks", UiResourceKind.MenuItem, "dream-tasks", "dream-tasks"));
    }

    /// <summary>撤销命令注册 — 可逆效应,使用 _registeredCommandNames 精确撤销</summary>
    /// <param name="registry">命令注册表</param>
    public void UnregisterCommands(ICommandRegistry registry) {
        foreach (var commandName in _registeredCommandNames) {
            registry.UnregisterCommand(commandName);
        }
        _registeredCommandNames.Clear();
    }

    /// <summary>
    /// 插件卸载时撤销命令注册
    /// </summary>
    protected override void OnUnload() {
        UnregisterCommandsIfRegistered();
    }

    private void UnregisterCommandsIfRegistered() {
        _registeredCommandNames.Clear();
    }
}