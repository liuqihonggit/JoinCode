namespace JoinCode.Host.Tests.Commands.Prefix;

/// <summary>
/// PrefixCommandRouter 单元测试 — 验证 ! / !! 前缀命令的解析与路由逻辑。
/// </summary>
public class PrefixCommandRouterTests
{
    #region IsPrefixCommand

    [Theory]
    [InlineData("!cmd", true)]
    [InlineData("!!cmd", true)]
    [InlineData("!git status", true)]
    [InlineData("!!dir", true)]
    [InlineData("! cmd", true)]          // ! 后有空格，trim 后有内容
    [InlineData("!! cmd", true)]         // !! 后有空格，trim 后有内容
    [InlineData("/cmd", false)]
    [InlineData("hello", false)]
    [InlineData("", false)]
    [InlineData("!", false)]             // 无命令内容
    [InlineData("!!", false)]            // 无命令内容
    [InlineData("! ", false)]            // trim 后为空
    [InlineData("!! ", false)]           // trim 后为空
    [InlineData("@mention", false)]
    [InlineData("  !cmd", false)]        // 前导空格不算前缀命令
    public void IsPrefixCommand_ShouldDetectCorrectly(string input, bool expected)
    {
        PrefixCommandRouter.IsPrefixCommand(input).Should().Be(expected);
    }

    #endregion

    #region Parse

    [Fact]
    public void Parse_SingleExclamation_ReturnsPrefixAndCommand()
    {
        var result = PrefixCommandRouter.Parse("!git status");
        result.Should().NotBeNull();
        result!.Value.Prefix.Should().Be("!");
        result.Value.Command.Should().Be("git status");
    }

    [Fact]
    public void Parse_DoubleExclamation_ReturnsPrefixAndCommand()
    {
        var result = PrefixCommandRouter.Parse("!!dir");
        result.Should().NotBeNull();
        result!.Value.Prefix.Should().Be("!!");
        result.Value.Command.Should().Be("dir");
    }

    [Fact]
    public void Parse_DoubleExclamation_TakesPriorityOverSingle()
    {
        var result = PrefixCommandRouter.Parse("!!echo hello");
        result.Should().NotBeNull();
        result!.Value.Prefix.Should().Be("!!");
        result.Value.Command.Should().Be("echo hello");
    }

    [Fact]
    public void Parse_NonPrefixCommand_ReturnsNull()
    {
        PrefixCommandRouter.Parse("/cmd").Should().BeNull();
        PrefixCommandRouter.Parse("hello").Should().BeNull();
        PrefixCommandRouter.Parse("").Should().BeNull();
    }

    [Fact]
    public void Parse_EmptyPrefix_ReturnsNull()
    {
        PrefixCommandRouter.Parse("!").Should().BeNull();
        PrefixCommandRouter.Parse("!!").Should().BeNull();
        PrefixCommandRouter.Parse("! ").Should().BeNull();
        PrefixCommandRouter.Parse("!! ").Should().BeNull();
    }

    [Fact]
    public void Parse_TrimLeadingWhitespace_AfterPrefix()
    {
        var single = PrefixCommandRouter.Parse("!  cmd");
        single.Should().NotBeNull();
        single!.Value.Prefix.Should().Be("!");
        single.Value.Command.Should().Be("cmd");

        var dbl = PrefixCommandRouter.Parse("!!  cmd");
        dbl.Should().NotBeNull();
        dbl!.Value.Prefix.Should().Be("!!");
        dbl.Value.Command.Should().Be("cmd");
    }

    #endregion

    #region Handler Properties

    [Fact]
    public void ShellPrefixCommandHandler_Properties_ShouldBeCorrect()
    {
        var handler = new ShellPrefixCommandHandler();
        handler.Prefix.Should().Be("!");
        handler.TriggersAi.Should().BeTrue();
    }

    [Fact]
    public void SilentShellPrefixCommandHandler_Properties_ShouldBeCorrect()
    {
        var handler = new SilentShellPrefixCommandHandler();
        handler.Prefix.Should().Be("!!");
        handler.TriggersAi.Should().BeFalse();
    }

    #endregion

    #region ExecuteAsync

    [Fact]
    public async Task ExecuteAsync_EmptyCommand_ReturnsNotHandled()
    {
        var context = new PrefixCommandContext { CancellationToken = CancellationToken.None };
        var result = await PrefixCommandRouter.ExecuteAsync("!", context);
        result.Handled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_SilentCommand_Url_ShouldNotInjectToAi()
    {
        var context = new PrefixCommandContext { CancellationToken = CancellationToken.None };
        var result = await PrefixCommandRouter.ExecuteAsync("!!echo test", context);
        result.Handled.Should().BeTrue();
        result.ShouldInjectToAi.Should().BeFalse();
    }

    #endregion
}
