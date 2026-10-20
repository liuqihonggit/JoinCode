namespace Host.Tests.Engine;

/// <summary>
/// CliRegistrationModule CLI 自动注册兜底模块测试 — ADR 0143 Step 3b（CoreModule 拆分）+ Step 3c（分类搬移）。
/// 契约：Step 3c 搬走 24 个引擎侧类型（telemetry hooks/TrustFolderManager/user_experience 5 件经
/// EngineRegistrationModule 注册）后，本模块经 AddJccAutoRegisteredServices 只兜底 cli 程序集剩余 25 个
/// 宿主类型（ConfirmationGate/OnboardingFlowController/VimEngine 等），gui/tui 会话经 extraModules 传入。
/// 注：只断言注册描述符 — 实例解析需完整宿主 DI 图（构造依赖来自其他模块），不在单模块测试范围。
/// </summary>
public sealed class CliRegistrationModuleTests {
    [Fact]
    public void ConfigureServices_RegistersCliAutoRegisteredServices() {
        var services = new ServiceCollection();
        var context = new AppModuleContext {
            Options = new CommandLineOptions(),
            Config = new WorkflowConfig(),
        };

        new CliRegistrationModule().ConfigureServices(services, context);

        Assert.Contains(services, d => d.ServiceType == typeof(IConfirmationGate));
        Assert.Contains(services, d => d.ServiceType == typeof(IOnboardingService));
        Assert.Contains(services, d => d.ServiceType == typeof(IVimEngine));
    }
}
