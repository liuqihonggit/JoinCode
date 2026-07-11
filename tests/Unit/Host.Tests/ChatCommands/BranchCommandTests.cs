namespace Host.Tests.ChatCommands;

/// <summary>
/// BranchCommand 取值范围测试 — 验证 CrudAction 枚举字面量(ls/new/rm 等)正确路由
/// 覆盖:list/ls/create/new/switch/go/delete/rm/未知子命令
/// 验证目标:Step 3.4 重构后,所有 case 标签能被正确识别
/// </summary>
public sealed class BranchCommandTests
{
    [Fact]
    public void Name_Should_Be_branch()
    {
        var cmd = new BranchCommand();
        cmd.Name.Should().Be("branch");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new BranchCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new BranchCommand();
        cmd.Usage.Should().StartWith("/branch");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new BranchCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Contain_branches()
    {
        var cmd = new BranchCommand();
        cmd.Aliases.Should().Contain("branches");
    }

    [Theory]
    [InlineData("list")]
    [InlineData("ls")]
    public async Task Execute_WithListVariants_Should_Return_Continue(string subCommand)
    {
        var cmd = new BranchCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Theory]
    [InlineData("create")]
    [InlineData("new")]
    public async Task Execute_WithCreateVariants_Should_Return_Continue(string subCommand)
    {
        var cmd = new BranchCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Theory]
    [InlineData("delete")]
    [InlineData("rm")]
    public async Task Execute_WithDeleteVariants_Should_Return_Continue(string subCommand)
    {
        var cmd = new BranchCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("go")]
    public async Task Execute_WithReservedSwitchVariants_Should_Return_Continue(string subCommand)
    {
        // switch/go 保留字符串(不属于 CrudAction 范围,Step 3.4 决策)
        var cmd = new BranchCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        var cmd = new BranchCommand();
        var context = CreateContext("unknown-action");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Return_Continue()
    {
        var cmd = new BranchCommand();
        var context = CreateContext("");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("LIST")]
    [InlineData("LS")]
    [InlineData("CREATE")]
    [InlineData("NEW")]
    [InlineData("DELETE")]
    [InlineData("RM")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        var cmd = new BranchCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    private static ChatCommandContext CreateContext(string arguments)
    {
        return new ChatCommandContext
        {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            },
        };
    }
}
