namespace JoinCode.App.Modules;

/// <summary>
/// 浏览器模块 — 注册 PuppeteerSharp 浏览器自动化服务
/// </summary>
[AppModule(Order = 60)]
public sealed class BrowserModule : IAppModule
{
    public int Order => 60;

    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.AddBrowserAutomation();
    }

    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}
