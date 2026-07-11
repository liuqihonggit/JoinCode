namespace JoinCode.App.Modules;

/// <summary>
/// 管道模块 — 注册命名管道通信服务
/// </summary>
[AppModule(Order = 70)]
public sealed class PipeModule : IAppModule
{
    public int Order => 70;

    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.RegisterPipeServices();
    }

    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}
