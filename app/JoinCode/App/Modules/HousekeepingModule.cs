namespace JoinCode.App.Modules;

/// <summary>
/// 家政清理模块 — 注册后台家政清理 HostedService
/// 对齐 TS startBackgroundHousekeeping: 延迟10分钟+24小时循环清理
/// </summary>
[AppModule(Order = 75)]
public sealed class HousekeepingModule : IAppModule
{
    public int Order => 75;

    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.AddHostedService<Infrastructure.Housekeeping.BackgroundHousekeepingService>(sp =>
        {
            var housekeeping = sp.GetRequiredService<IHousekeepingService>();
            var fs = sp.GetRequiredService<IFileSystem>();
            var clock = sp.GetRequiredService<IClockService>();
            var logger = sp.GetService<ILogger<Infrastructure.Housekeeping.BackgroundHousekeepingService>>();
            return new Infrastructure.Housekeeping.BackgroundHousekeepingService(housekeeping, fs, clock, logger);
        });
    }

    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}
