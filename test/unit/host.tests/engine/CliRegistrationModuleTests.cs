namespace Host.Tests.Engine;

/// <summary>
/// CliRegistrationModule CLI 自动注册兜底模块测试 — ADR 0143 Step 3b（CoreModule 拆分）。
/// 契约：CoreModule 拆分后，cli 程序集内 49 个 [Register] 类型（telemetry hooks/ConfirmationGate/
/// OnboardingFlowController 等）由本模块经生成器方法 AddJccAutoRegisteredServices 兜底注册，
/// gui/tui 会话经 extraModules 传入以保持行为零变化。
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
        Assert.Contains(services, d => d.ServiceType == typeof(IClipboardService));
        Assert.Contains(services, d => d.ServiceType == typeof(IProactiveStateService));
        Assert.Contains(services, d => d.ServiceType == typeof(IVimEngine));
    }
}
