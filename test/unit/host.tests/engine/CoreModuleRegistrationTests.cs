namespace Host.Tests.Engine;

/// <summary>
/// CoreModule 引擎自动注册测试 — ADR 0143 Step 3c（49 类型分类搬移首批）。
/// 契约：24 个引擎侧 [Register] 类型（17 telemetry hooks + 伴生基类 + TrustFolderManager +
/// user_experience 5 件）搬入 engine 程序集后，由 CoreModule 经生成器方法
/// AddJoinCodeEngineAutoRegisteredServices 注册（挂核心模块使三宿主零改动获得引擎注册）；
/// cli 侧 CliRegistrationModule 只兜底剩余 25 个宿主类型。
/// 注：只断言注册描述符 — 实例解析需完整宿主 DI 图（构造依赖来自其他模块），不在单模块测试范围。
/// </summary>
public sealed class CoreModuleRegistrationTests {
    [Fact]
    public void ConfigureServices_RegistersEngineAutoRegisteredServices() {
        var services = new ServiceCollection();
        var context = new AppModuleContext {
            Options = new CommandLineOptions(),
            Config = new WorkflowConfig(),
        };

        new CoreModule().ConfigureServices(services, context);

        Assert.Contains(services, d => d.ServiceType == typeof(ITrustFolderManager));
        Assert.Contains(services, d => d.ServiceType == typeof(IClipboardService));
        Assert.Contains(services, d => d.ServiceType == typeof(IProactiveStateService));
        Assert.Contains(services, d => d.ServiceType == typeof(IAssistantModeService));
        Assert.Contains(services, d => d.ServiceType == typeof(IBuddyService));
        Assert.Contains(services, d => d.ServiceType == typeof(IStickerService));
    }

    [Fact]
    public void ConfigureServices_RegistersTelemetryPipelineHooks() {
        var services = new ServiceCollection();
        var context = new AppModuleContext {
            Options = new CommandLineOptions(),
            Config = new WorkflowConfig(),
        };

        new CoreModule().ConfigureServices(services, context);

        Assert.Contains(services, d => d.ServiceType == typeof(IPipelinePostHook<ChatMiddlewareContext>));
        Assert.Contains(services, d => d.ServiceType == typeof(IPipelinePreHook<ChatMiddlewareContext>));
        Assert.Contains(services, d => d.ServiceType == typeof(IPipelinePostHook<WebContext>));
    }
}
