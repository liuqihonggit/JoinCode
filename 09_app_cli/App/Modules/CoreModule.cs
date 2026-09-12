namespace JoinCode.App.Modules;

/// <summary>
/// 核心模块 — 注册 AI 工作流服务（AddAiWorkflowServices 包含 AddAutoRegisteredServices + 全部子系统）
/// </summary>
[AppModule(Order = 30)]
public sealed class CoreModule : IAppModule
{
    public int Order => 30;

    public void ConfigureServices(IServiceCollection services, AppModuleContext context)
    {
        services.AddAiWorkflowServices(context.Config);

        // JoinCode 项目的 [Register] 类型（如 OnboardingFlowController、ExecutionSettingsProvider 等）
        // 不在 Sync 程序集的扫描范围内，需补调 JoinCode 版本的注册方法
        // 源码生成器在 Exe 项目仅扫描自身程序集类型，方法名按程序集名生成
        // JoinCode 程序集名 "jcc" → 清理为 "Jcc"
        services.AddJccAutoRegisteredServices();
        services.AddJccAutoRegisteredOptions();

        // 中间件已通过 [Register] + AddAutoRegisteredServices 注册到 DI
        // 此处从 DI 解析中间件并按 Order 排序后构建管道
        // 替代源码生成器的 AddAutoRegisteredPipelines（解决跨程序集管道覆盖问题）
        services.AddAllPipelines();
    }

    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}
