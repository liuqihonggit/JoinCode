namespace Composition.Tests.DependencyInjection;

/// <summary>
/// SandboxPromptPolicySync 测试 — sandbox 两个开关的配置变更必须即时反映到 Bash 提示词三段式措辞。
/// </summary>
public class SandboxPromptPolicySyncTest {
    private static (SandboxPromptPolicySync Sync, Mock<IConfigurationService> Config) Create(bool enabled, bool allowUnsandboxed = true) {
        var configService = new Mock<IConfigurationService>();
        var workflow = new WorkflowConfig {
            Sandbox = new SandboxConfig { Enabled = enabled, AllowUnsandboxedCommands = allowUnsandboxed },
        };
        var sync = new SandboxPromptPolicySync(configService.Object, workflow);
        return (sync, configService);
    }

    [Fact]
    public void Given_启动配置启用_When_构造_Then_立即应用三段式() {
        var (sync, _) = Create(enabled: true);

        BashToolPrompt.GetDescription().Should().Contain("沙箱模式：");
        sync.Dispose();
    }

    [Fact]
    public void Given_启动配置关闭_When_构造_Then_无沙箱段() {
        var (sync, config) = Create(enabled: false);

        BashToolPrompt.GetDescription().Should().NotContain("沙箱模式：");

        config.Raise(c => c.SettingChanged += null, new SettingChangeEventArgs {
            Key = ConfigKeys.SandboxEnabled, NewValue = "true",
        });
        BashToolPrompt.GetDescription().Should().Contain("沙箱模式：", "配置变更后应即时生效");

        sync.Dispose();
    }

    [Fact]
    public void Given_策略禁止逃生阀_When_收到配置变更_Then_提示词改为强制沙箱() {
        var (sync, config) = Create(enabled: true, allowUnsandboxed: true);

        config.Raise(c => c.SettingChanged += null, new SettingChangeEventArgs {
            Key = ConfigKeys.SandboxAllowUnsandboxedCommands, NewValue = "false",
        });

        BashToolPrompt.GetDescription().Should().Contain("所有命令必须在沙箱内执行");
        BashToolPrompt.GetDescription().Should().NotContain("用户明确要求绕过沙箱");

        sync.Dispose();
    }

    [Fact]
    public void Given_无关键变更_When_收到事件_Then_不改策略() {
        var (sync, config) = Create(enabled: true);

        config.Raise(c => c.SettingChanged += null, new SettingChangeEventArgs {
            Key = ConfigKeys.Theme, NewValue = "dark",
        });

        BashToolPrompt.GetDescription().Should().Contain("沙箱模式：");

        sync.Dispose();
    }
}
