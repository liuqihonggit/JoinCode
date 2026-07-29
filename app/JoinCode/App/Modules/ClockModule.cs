namespace JoinCode.App.Modules;

/// <summary>
/// 时钟模块 — 注册定时任务相关服务
/// </summary>
[AppModule(Order = 40)]
public sealed class ClockModule : IAppModule
{
    public int Order => 40;

    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.AddClockServices();
    }

    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}
