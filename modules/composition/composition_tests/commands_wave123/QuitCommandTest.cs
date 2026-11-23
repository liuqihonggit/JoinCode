namespace Composition.Tests.Commands;

/// <summary>
/// /quit 命令单元测试 — 直接返回 Exit 结果(ShouldContinue=false),别名 q
/// </summary>
public sealed class QuitCommandTest {
    [Fact]
    public async Task Execute_ReturnsExitResult() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services);

        var result = await new QuitCommand().ExecuteAsync(context);

        result.ShouldContinue.Should().BeFalse("/quit 应终止会话循环");
        result.IsHandled.Should().BeTrue("命令已被处理");
    }

    [Fact]
    public async Task Execute_IgnoresArguments() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "anything");

        var result = await new QuitCommand().ExecuteAsync(context);

        result.ShouldContinue.Should().BeFalse("/quit 不接受参数,始终退出");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new QuitCommand();
        cmd.Name.Should().Be("quit");
        cmd.Usage.Should().Be("/quit");
        cmd.Description.Should().Be("退出程序");
        cmd.Aliases.Should().Contain("q", "q 为 /quit 的别名");
    }
}
