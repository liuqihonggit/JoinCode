
namespace JoinCode.Dream;

/// <summary>
/// Dream 插件入口 - 记忆整合功能插件
/// </summary>
[Register(typeof(IWorkflowPlugin))]
[Register(typeof(ICommandRegistrationHook))]
public sealed partial class DreamPlugin : ServiceEntity, IWorkflowPlugin, ICommandRegistrationHook, IDisposable
{
    private readonly List<string> _registeredCommandNames = new();
    private bool _disposed;

    public string Name => "Dream";
    public string Version => "1.0.0";
    public string Description => "JoinCode 记忆整合插件";

    public OperationResult Load(IServiceCollection services)
    {
        services.AddDreamPluginServices();
        return OperationResult.Ok();
    }

    public async Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var registry = serviceProvider.GetService<IDreamTaskRegistry>();
        if (registry is Persistence.PersistentDreamTaskRegistry persistentRegistry)
        {
            await persistentRegistry.LoadActiveTasksAsync(cancellationToken).ConfigureAwait(false);
        }

        return OperationResult.Ok();
    }

    public PluginUnloadResult Unload()
    {
        Dispose();
        return PluginUnloadResult.Success("Dream", TimeSpan.Zero);
    }

    public void RegisterCommands(ICommandRegistry registry, IServiceProvider serviceProvider)
    {
        var dreamFeature = serviceProvider.GetRequiredService<IDreamFeature>();
        var dreamCmd = new DreamCommand(dreamFeature);
        registry.Register(dreamCmd);
        _registeredCommandNames.Add(dreamCmd.Name);

        var dreamTasksCmd = new DreamTasksCommand(dreamFeature);
        registry.Register(dreamTasksCmd);
        _registeredCommandNames.Add(dreamTasksCmd.Name);
    }

    /// <summary>撤销命令注册 — 可逆效应,使用 _registeredCommandNames 精确撤销</summary>
    public void UnregisterCommands(ICommandRegistry registry)
    {
        foreach (var commandName in _registeredCommandNames)
        {
            registry.UnregisterCommand(commandName);
        }
        _registeredCommandNames.Clear();
    }

    protected override void OnDispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
