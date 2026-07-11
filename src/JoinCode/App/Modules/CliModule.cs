namespace JoinCode.App.Modules;

/// <summary>
/// CLI 模块 — 注册 CLI 专属服务（预览模式等条件注册）
/// </summary>
[AppModule(Order = 80)]
public sealed class CliModule : IAppModule
{
    public int Order => 80;

    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
    }

    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}
