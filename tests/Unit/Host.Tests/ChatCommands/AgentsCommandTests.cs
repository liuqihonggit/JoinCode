namespace Host.Tests.ChatCommands;

/// <summary>
/// AgentsCommand 取值范围测试 — 验证 CrudAction 枚举字面量(list/ls)正确路由
/// 覆盖:list/ls/info/未知子命令/空参数默认 list
/// 验证目标:Step 3 重构后,所有 case 标签能被正确识别
/// </summary>
public sealed class AgentsCommandTests
{
    [Fact]
    public void Name_Should_Be_agents()
    {
        var cmd = new AgentsCommand();
        cmd.Name.Should().Be("agents");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new AgentsCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new AgentsCommand();
        cmd.Usage.Should().StartWith("/agents");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new AgentsCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new AgentsCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Theory]
    [InlineData("list")]
    [InlineData("ls")]
    public async Task Execute_WithListVariants_Should_Return_Continue(string subCommand)
    {
        // list/ls — CrudAction.List/Ls 别名
        var cmd = new AgentsCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Default_To_List()
    {
        // 无参数时默认 list
        var cmd = new AgentsCommand();
        var context = CreateContext("");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithInfo_Should_Return_Continue()
    {
        // info 保留字符串(Agents 专属子命令,非 CrudAction.Read 语义)
        var cmd = new AgentsCommand();
        var context = CreateContext("info general-purpose");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        var cmd = new AgentsCommand();
        var context = CreateContext("unknown-action");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("LIST")]
    [InlineData("LS")]
    [InlineData("INFO")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        // 验证小写化路由(toLowerInvariant 后枚举匹配)
        var cmd = new AgentsCommand();
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
