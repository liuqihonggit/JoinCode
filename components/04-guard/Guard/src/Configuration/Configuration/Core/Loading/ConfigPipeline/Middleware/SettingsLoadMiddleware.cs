namespace Core.Configuration.ConfigPipeline;

/// <summary>
/// Step 1: 并行加载多源配置 + 规则文件
/// </summary>
[Register(typeof(IConfigLoadMiddleware))]
public sealed partial class SettingsLoadMiddleware : IConfigLoadMiddleware
{
    [Inject] private readonly IFileSystem _fs;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(ConfigLoadContext context, MiddlewareDelegate<ConfigLoadContext> next, CancellationToken ct)
    {
        var projectDir = context.ProjectDirectory ?? _fs.GetCurrentDirectory();

        var settingsTask = SettingsLoader.LoadAllSourcesAsync(_fs, projectDir: projectDir, cancellationToken: ct);
        var rulesLoader = new ProjectRulesLoader(_fs);
        var projectRulesTask = rulesLoader.LoadRulesAsync(projectDir, ct);
        var externalRulesLoader = new ExternalRulesLoader(_fs);
        var externalRulesTask = externalRulesLoader.LoadProjectRulesAsync(projectDir, ct);

        await Task.WhenAll(settingsTask, projectRulesTask, externalRulesTask).ConfigureAwait(false);

        context.Settings = await settingsTask.ConfigureAwait(false);
        context.ProjectRules = await projectRulesTask.ConfigureAwait(false);
        context.ExternalRules = await externalRulesTask.ConfigureAwait(false);

        await next(context, ct).ConfigureAwait(false);
    }
}
