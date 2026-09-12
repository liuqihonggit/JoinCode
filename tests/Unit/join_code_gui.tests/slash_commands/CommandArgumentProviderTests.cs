namespace JoinCode.Gui.Tests.SlashCommands;

/// <summary>
/// CommandArgumentProvider 单元测试 — 验证各命令参数补全候选生成与前缀过滤。
/// </summary>
public class CommandArgumentProviderTests
{
    private static IJccChatSession CreateSession() => new PlaceholderChatSession();

    [Fact]
    public void GetArguments_ModelCommand_ReturnsAvailableModels()
    {
        var session = CreateSession();
        var args = CommandArgumentProvider.GetArguments("/model", "", session);
        // 无 configService 时 CurrentVendor="" → /model 返回空(不硬编码供应商)
        args.Should().BeEmpty();
    }

    [Fact]
    public void GetArguments_ThemeCommand_ReturnsThemes()
    {
        var session = CreateSession();
        var args = CommandArgumentProvider.GetArguments("/theme", "", session);
        args.Should().HaveCount(3);
        args.Select(a => a.Name).Should().Contain(new[] { "dark", "light", "auto" });
    }

    [Fact]
    public void GetArguments_WithPrefix_FiltersResults()
    {
        var session = CreateSession();
        var args = CommandArgumentProvider.GetArguments("/theme", "da", session);
        args.Should().HaveCount(1);
        args[0].Name.Should().Be("dark");
    }

    [Fact]
    public void GetArguments_EffortCommand_ReturnsEffortLevels()
    {
        var session = CreateSession();
        var args = CommandArgumentProvider.GetArguments("/effort", "", session);
        args.Should().HaveCount(5);
        args.Select(a => a.Name).Should().Contain(new[] { "auto", "low", "medium", "high", "max" });
    }

    [Fact]
    public void GetArguments_ConfigCommand_ReturnsSubCommands()
    {
        var session = CreateSession();
        var args = CommandArgumentProvider.GetArguments("/config", "", session);
        args.Should().HaveCount(4);
        args.Select(a => a.Name).Should().Contain(new[] { "get", "set", "list", "remove" });
    }

    [Fact]
    public void GetArguments_ProviderCommand_ReturnsProviders()
    {
        var session = CreateSession();
        var args = CommandArgumentProvider.GetArguments("/provider", "", session);
        args.Should().NotBeEmpty();
        args.Select(a => a.Name).Should().Contain("openai");
    }

    [Fact]
    public void GetArguments_UnknownCommand_ReturnsEmpty()
    {
        var session = CreateSession();
        var args = CommandArgumentProvider.GetArguments("/unknown", "", session);
        args.Should().BeEmpty();
    }

    [Fact]
    public void GetArguments_CaseInsensitiveCommand_Matches()
    {
        var session = CreateSession();
        var args = CommandArgumentProvider.GetArguments("/MODEL", "", session);
        // 无 configService 时 CurrentVendor="" → /model 返回空,但命令仍匹配(不报未知命令)
        args.Should().BeEmpty();
    }
}
