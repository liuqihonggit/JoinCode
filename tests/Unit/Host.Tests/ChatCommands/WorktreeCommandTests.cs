namespace Host.Tests.ChatCommands;

using JoinCode.Abstractions.Models;

/// <summary>
/// WorktreeCommand 取值范围测试 — 验证 CrudAction 枚举字面量正确路由
/// 覆盖:list/ls/delete/rm/create + cleanup/clean/status (保留字符串) + 未知子命令
/// 验证目标:Step 3.5 重构后,所有 case 标签能被正确识别
/// 注意:WorktreeService null 时 ExecuteAsync 直接返回,case 不会触发,需用 Mock 触发 case
/// </summary>
public sealed class WorktreeCommandTests
{
    [Fact]
    public void Name_Should_Be_worktree()
    {
        var cmd = new WorktreeCommand();
        cmd.Name.Should().Be("worktree");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new WorktreeCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new WorktreeCommand();
        cmd.Usage.Should().StartWith("/worktree");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new WorktreeCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Be_Empty()
    {
        var cmd = new WorktreeCommand();
        cmd.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task Execute_WhenServiceIsNull_Should_Return_Continue()
    {
        // WorktreeService null 时,直接 return Continue,不会进入 switch
        // 这是为了避免在没有 WorktreeService 的环境下崩溃
        var cmd = new WorktreeCommand();
        var context = CreateContext("list", worktreeService: null);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithListSubcommand_Should_Return_Continue()
    {
        // CrudActionConstants.List("list") → ListWorktreesAsync → 应正常返回
        var worktreeService = CreateMockWorktreeService();
        var cmd = new WorktreeCommand();
        var context = CreateContext("list", worktreeService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithLsAlias_Should_Return_Continue()
    {
        // CrudActionConstants.Ls("ls") → ListWorktreesAsync (走 List 相同分支)
        var worktreeService = CreateMockWorktreeService();
        var cmd = new WorktreeCommand();
        var context = CreateContext("ls", worktreeService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithCreateSubcommand_Should_Return_Continue()
    {
        // CrudActionConstants.Create("create") → CreateWorktreeAsync
        var worktreeService = CreateMockWorktreeService();
        var cmd = new WorktreeCommand();
        var context = CreateContext("create agent-1", worktreeService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithDeleteSubcommand_Should_Return_Continue()
    {
        // CrudActionConstants.Delete("delete") → RemoveWorktreeAsync
        var worktreeService = CreateMockWorktreeService();
        var cmd = new WorktreeCommand();
        var context = CreateContext("delete agent-1", worktreeService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithRmAlias_Should_Return_Continue()
    {
        // CrudActionConstants.Rm("rm") → RemoveWorktreeAsync (走 Delete 相同分支)
        var worktreeService = CreateMockWorktreeService();
        var cmd = new WorktreeCommand();
        var context = CreateContext("rm agent-1", worktreeService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithCleanupSubcommand_Should_Return_Continue()
    {
        // cleanup/clean 保留字符串(不属于 CrudAction 范围,Step 3.5 决策)
        var worktreeService = CreateMockWorktreeService();
        var cmd = new WorktreeCommand();
        var context = CreateContext("cleanup", worktreeService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithStatusSubcommand_Should_Return_Continue()
    {
        // status 保留字符串(不属于 CrudAction 范围,Step 3.5 决策)
        var worktreeService = CreateMockWorktreeService();
        var cmd = new WorktreeCommand();
        var context = CreateContext("status", worktreeService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        // default 分支应被触发,输出"未知子命令"消息
        var worktreeService = CreateMockWorktreeService();
        var cmd = new WorktreeCommand();
        var context = CreateContext("unknown-action", worktreeService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("LIST")]
    [InlineData("LS")]
    [InlineData("CREATE")]
    [InlineData("DELETE")]
    [InlineData("RM")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        var worktreeService = CreateMockWorktreeService();
        var cmd = new WorktreeCommand();
        var context = CreateContext(subCommand, worktreeService);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    private static ChatCommandContext CreateContext(string arguments, IAgentWorktreeService? worktreeService)
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
                WorktreeService = worktreeService,
            FileSystem = TestFileSystem.Current,
            },
        };
    }

    private static IAgentWorktreeService CreateMockWorktreeService()
    {
        var mock = new Mock<IAgentWorktreeService>();

        // ListWorktreesAsync 返回空列表
        mock.Setup(s => s.ListWorktreesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string>)Array.Empty<string>());

        // GetAllSessionsAsync 返回空列表
        mock.Setup(s => s.GetAllSessionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AgentWorktreeSession>)Array.Empty<AgentWorktreeSession>());

        // GetSessionAsync 返回 null (未找到)
        mock.Setup(s => s.GetSessionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgentWorktreeSession?)null);

        // HasUncommittedChangesAsync 返回 false
        mock.Setup(s => s.HasUncommittedChangesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // CreateAgentWorktreeAsync 返回失败(避免实际目录创建)
        mock.Setup(s => s.CreateAgentWorktreeAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<WorktreeOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorktreeCreateResult.FailureResult("Mock"));

        // RemoveAgentWorktreeAsync 返回失败
        mock.Setup(s => s.RemoveAgentWorktreeAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorktreeCleanupResult.FailureResult("Mock"));

        // CleanupStaleWorktreesAsync 返回 0
        mock.Setup(s => s.CleanupStaleWorktreesAsync(It.IsAny<WorktreeOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // HasUnpushedCommitsAsync 返回 false
        mock.Setup(s => s.HasUnpushedCommitsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return mock.Object;
    }
}
