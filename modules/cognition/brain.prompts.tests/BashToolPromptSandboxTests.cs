namespace Brain.Prompts.Tests;

/// <summary>
/// BashToolPrompt 沙箱三段式措辞测试 — 对齐基准 <c>tools/BashTool/prompt.ts:228-256</c>：
/// 未启用沙箱不出现沙箱段；启用且允许逃生阀 → 默认留沙箱 + 何时可跳出；策略禁止 → 所有命令 MUST 在沙箱内。
/// </summary>
public class BashToolPromptSandboxTests {
    [Fact]
    public void Given_沙箱未启用_When_GetDescription_Then_无沙箱段() {
        BashToolPrompt.SetSandboxPolicy(enabled: false, allowUnsandboxedCommands: true);

        var description = BashToolPrompt.GetDescription();

        description.Should().NotContain("沙箱模式：");
        description.Should().Contain("dangerously_disable_sandbox 参数覆盖沙箱模式");
    }

    [Fact]
    public void Given_启用且允许逃生阀_When_GetDescription_Then_默认留沙箱并给跳出条件() {
        BashToolPrompt.SetSandboxPolicy(enabled: true, allowUnsandboxedCommands: true);

        var description = BashToolPrompt.GetDescription();

        description.Should().Contain("沙箱模式：");
        description.Should().Contain("默认在沙箱内执行所有命令");
        description.Should().Contain("用户明确要求绕过沙箱");
        description.Should().Contain("Operation not permitted");
        description.Should().Contain("$TMPDIR");
        description.Should().Contain("不要建议把 ~/.bashrc");
    }

    [Fact]
    public void Given_启用且策略禁止逃生阀_When_GetDescription_Then_所有命令必须在沙箱内() {
        BashToolPrompt.SetSandboxPolicy(enabled: true, allowUnsandboxedCommands: false);

        var description = BashToolPrompt.GetDescription();

        description.Should().Contain("所有命令必须在沙箱内执行");
        description.Should().Contain("已被策略禁用");
        description.Should().NotContain("用户明确要求绕过沙箱");
    }

    [Fact]
    public void Given_状态切换_When_GetDescription_Then_立即生效() {
        BashToolPrompt.SetSandboxPolicy(enabled: true, allowUnsandboxedCommands: true);
        BashToolPrompt.GetDescription().Should().Contain("沙箱模式：");

        BashToolPrompt.SetSandboxPolicy(enabled: false, allowUnsandboxedCommands: true);
        BashToolPrompt.GetDescription().Should().NotContain("沙箱模式：");
    }
}
