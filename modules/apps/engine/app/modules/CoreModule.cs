namespace JoinCode.App.Modules;

/// <summary>
/// 核心模块 — 注册 AI 工作流服务（AddAiWorkflowServices 包含 AddAutoRegisteredServices + 全部子系统）
/// + engine 程序集 [Register] 类型自动注册（ADR 0143 Step 3c 搬入 24 个引擎侧资产）
/// </summary>
[AppModule(Order = 30)]
public sealed class CoreModule : IAppModule {
    /// <summary>模块加载顺序，值为 30</summary>
    public int Order => 30;

    /// <summary>
    /// 配置核心服务 — 注册 AI 工作流服务、全部管道与 engine 程序集自动注册
    /// <para>engine 程序集 [Register] 类型（24 个：telemetry hooks/TrustFolderManager/user_experience 5 件）</para>
    /// <para>经生成器方法 AddJoinCodeEngineAutoRegisteredServices 注册（"JoinCode.Engine"→"JoinCodeEngine"）</para>
    /// <para>cli 程序集剩余 25 个宿主侧类型仍由 cli 宿主 CliRegistrationModule(31) 兜底（ADR 0143 Step 3b）</para>
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="context">应用模块上下文，提供配置信息</param>
    public void ConfigureServices(IServiceCollection services, AppModuleContext context) {
        services.AddAiWorkflowServices(context.Config);
        services.AddJoinCodeEngineAutoRegisteredServices();
        services.AddJoinCodeEngineAutoRegisteredOptions();

        // 中间件已通过 [Register] + AddAutoRegisteredServices 注册到 DI
        // 此处从 DI 解析中间件并按 Order 排序后构建管道
        // 替代源码生成器的 AddAutoRegisteredPipelines（解决跨程序集管道覆盖问题）
        services.AddAllPipelines();
    }

    /// <summary>
    /// 异步配置 — 核心模块无需异步初始化，直接返回完成
    /// </summary>
    /// <param name="services">服务提供者</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>已完成的任务</returns>
    public Task ConfigureAsync(IServiceProvider services, CancellationToken ct)
        => Task.CompletedTask;
}