
namespace JoinCode.Dream;

/// <summary>
/// Dream 插件入口 - 记忆整合功能插件
/// </summary>
[Register(typeof(IWorkflowPlugin))]
[Register(typeof(ICommandRegistrationHook))]
public sealed partial class DreamPlugin : IWorkflowPlugin, ICommandRegistrationHook, IDisposable
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
        registry.Register(new DreamCommand(dreamFeature));
        _registeredCommandNames.Add(nameof(DreamCommand));

        registry.Register(new DreamTasksCommand(dreamFeature));
        _registeredCommandNames.Add(nameof(DreamTasksCommand));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
