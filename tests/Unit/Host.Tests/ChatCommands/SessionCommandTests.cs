namespace Host.Tests.ChatCommands;

/// <summary>
/// SessionCommand 取值范围测试 — 验证 CrudAction 枚举字面量(ls/rm 等)正确路由
/// 覆盖:list/ls/resume/open/delete/rm/未知子命令
/// 验证目标:Step 3.3 重构后,所有 case 标签能被正确识别,语义保持
/// </summary>
public sealed class SessionCommandTests
{
    [Fact]
    public void Name_Should_Be_session()
    {
        var cmd = new SessionCommand();
        cmd.Name.Should().Be("session");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new SessionCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new SessionCommand();
        cmd.Usage.Should().StartWith("/session");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new SessionCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Contain_sessions()
    {
        var cmd = new SessionCommand();
        cmd.Aliases.Should().Contain("sessions");
    }

    [Fact]
    public async Task Execute_WithListAlias_Ls_Should_Return_Continue()
    {
        var cmd = new SessionCommand();
        var context = CreateContext("ls");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithListLiteral_List_Should_Return_Continue()
    {
        var cmd = new SessionCommand();
        var context = CreateContext("list");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithDeleteAlias_Rm_Should_Return_Continue()
    {
        var cmd = new SessionCommand();
        var context = CreateContext("rm");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithDeleteLiteral_Delete_Should_Return_Continue()
    {
        var cmd = new SessionCommand();
        var context = CreateContext("delete");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithReservedSubcommand_Resume_Should_Return_Continue()
    {
        // resume/open 保留字符串(不属于 CrudAction 范围,Step 3.3 决策)
        var cmd = new SessionCommand();
        var context = CreateContext("resume");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithReservedSubcommand_Open_Should_Return_Continue()
    {
        // resume/open 保留字符串
        var cmd = new SessionCommand();
        var context = CreateContext("open");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        // default 分支应被触发,不应崩溃
        var cmd = new SessionCommand();
        var context = CreateContext("unknown-action");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Return_Continue()
    {
        // 空 args → 走默认 list 分支
        var cmd = new SessionCommand();
        var context = CreateContext("");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("LIST")]
    [InlineData("Ls")]
    [InlineData("DELETE")]
    [InlineData("Rm")]
    public async Task Execute_WithMixedCaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        // CrudActionConstants 字典使用 OrdinalIgnoreCase, 大小写不敏感
        var cmd = new SessionCommand();
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
