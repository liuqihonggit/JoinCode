namespace Composition.Tests.Commands;

/// <summary>
/// /color 命令单元测试 — 覆盖空参/test 颜色测试面板/reset+default 重置确认/
/// 任意主题名回显+引导提示/大小写归一/命令元数据
/// <para>命令零服务依赖,直接经 CommandTestHarness 构造上下文</para>
/// </summary>
[Collection("command-output")]
public sealed class ColorCommandTest {
    private static async Task<(ChatCommandResult Result, string Output)> RunAsync(string arguments) {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return await CommandTestHarness.CaptureOutputAsync(() => new ColorCommand().ExecuteAsync(context));
    }

    [Fact]
    public async Task Execute_EmptyArgs_ShowsColorTestPanel() {
        var (result, output) = await RunAsync("");

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("=== 终端颜色测试 ===");
        output.Should().Contain("基础颜色:");
        output.Should().Contain("绿色(成功)");
        output.Should().Contain("红色(错误)");
        output.Should().Contain("ANSI 256色:");
        output.Should().Contain("样式:");
        output.Should().Contain("说明终端支持 ANSI 颜色");
    }

    [Fact]
    public async Task Execute_Test_ShowsColorTestPanel() {
        var (result, output) = await RunAsync("test");

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 终端颜色测试 ===");
        output.Should().Contain("基础颜色:");
    }

    [Fact]
    public async Task Execute_TestArg_IsCaseInsensitive() {
        var (_, output) = await RunAsync("TEST");

        output.Should().Contain("=== 终端颜色测试 ===", "参数应 ToLowerInvariant 归一后匹配 test");
    }

    [Fact]
    public async Task Execute_Reset_ShowsResetConfirmationWithHints() {
        var (result, output) = await RunAsync("reset");

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("颜色已重置为默认");
        output.Should().NotContain("颜色主题:");
    }

    [Fact]
    public async Task Execute_DefaultAlias_SameAsReset() {
        var (result, output) = await RunAsync("default");

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("颜色已重置为默认", "default 是 RESET_ALIASES 之一");
    }

    [Fact]
    public async Task Execute_ThemeName_EchoesThemeWithGuide() {
        var (result, output) = await RunAsync("ocean");

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("颜色主题: ocean");
        output.Should().Contain("使用 /color test 测试终端颜色支持");
        output.Should().Contain("使用 /color reset 重置为默认");
        output.Should().NotContain("颜色已重置为默认");
    }

    [Fact]
    public async Task Execute_TrimsWhitespaceAroundArgs() {
        var (_, output) = await RunAsync("  solarized  ");

        output.Should().Contain("颜色主题: solarized", "GetNormalizedArgs 应 Trim 后再匹配");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ColorCommand();
        cmd.Name.Should().Be("color");
        cmd.Usage.Should().Be("/color [theme|test|reset]");
        cmd.Aliases.Should().BeEmpty();
    }
}
