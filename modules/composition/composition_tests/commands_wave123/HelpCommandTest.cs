namespace Composition.Tests.Commands;

/// <summary>
/// /help 命令单元测试 — 覆盖注册表缺失降级/多分类命令渲染(usage+描述)/
/// 分类分组 Tab 顺序输出/空注册表边界
/// <para>CommandRegistry 为具体类,用真实 ChatCommandRegistry 注册真实命令实例验证渲染链路</para>
/// </summary>
[Collection("command-output")]
public sealed class HelpCommandTest {
    private static ChatCommandContext CreateContext(ChatCommandRegistry? registry) {
        var services = CommandTestHarness.CreateServices(commandRegistry: registry);
        return CommandTestHarness.CreateContext(services);
    }

    [Fact]
    public async Task Execute_WithoutRegistry_ShowsUnavailableMessage() {
        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new HelpCommand().ExecuteAsync(CreateContext(registry: null)));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("命令注册表不可用。", "注册表缺失应提示而非抛异常");
    }

    [Fact]
    public async Task Execute_WithEmptyRegistry_RendersWithoutCommands() {
        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new HelpCommand().ExecuteAsync(CreateContext(new ChatCommandRegistry())));

        result.ShouldContinue.Should().BeTrue();
        output.Should().NotContain("/quit", "空注册表不应渲染任何命令");
    }

    [Fact]
    public async Task Execute_WithRegisteredCommands_RendersUsageAndDescription() {
        var registry = new ChatCommandRegistry();
        registry.Register(new QuitCommand());
        registry.Register(new VersionCommand());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new HelpCommand().ExecuteAsync(CreateContext(registry)));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("/quit", "应渲染命令 usage");
        output.Should().Contain("退出程序", "应渲染命令描述");
        output.Should().Contain("/version");
        output.Should().Contain("显示版本信息");
    }

    [Fact]
    public async Task Execute_GroupsCommandsByCategoryWithSeparators() {
        var registry = new ChatCommandRegistry();
        registry.Register(new QuitCommand());
        registry.Register(new VersionCommand());
        // 分类由源码生成器在生产路径自动设置,测试手动补齐模拟真实注册形态
        registry.SetCategory("quit", ChatCommandCategory.Session);
        registry.SetCategory("version", ChatCommandCategory.Info);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new HelpCommand().ExecuteAsync(CreateContext(registry)));

        result.ShouldContinue.Should().BeTrue();
        // TabPanel 对多个分类顺序渲染,分类之间应有分隔线
        output.Should().Contain("─", "多分类应渲染 Tab 分隔线");
        var quitIndex = output.IndexOf("/quit", StringComparison.Ordinal);
        var versionIndex = output.IndexOf("/version", StringComparison.Ordinal);
        quitIndex.Should().BeGreaterThanOrEqualTo(0);
        versionIndex.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Execute_IgnoresArguments() {
        var registry = new ChatCommandRegistry();
        registry.Register(new QuitCommand());
        var services = CommandTestHarness.CreateServices(commandRegistry: registry);
        var context = CommandTestHarness.CreateContext(services, arguments: "anything");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new HelpCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("/quit", "/help 不消费参数,仍渲染全部命令");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new HelpCommand();
        cmd.Name.Should().Be("help");
        cmd.Usage.Should().Be("/help");
        cmd.Aliases.Should().Contain("?", "? 为 /help 的别名");
        cmd.ExposeToMcp.Should().BeTrue("help 应暴露给 MCP");
    }
}
