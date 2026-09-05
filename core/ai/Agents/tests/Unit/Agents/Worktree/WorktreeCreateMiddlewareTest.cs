namespace JoinCode.Agents.Tests.Worktree;

/// <summary>
/// WorktreeCreateMiddleware 单元测试 — 验证空 BranchName/WorktreePath 自动生成逻辑（bug 修复回归测试）。
/// <para>bug 背景：当上游中间件未填充 BranchName 时，git worktree add 使用空分支名报错。</para>
/// <para>修复：InvokeAsync 中 branchName/worktreePath 为空时自动生成并写回 context。</para>
/// </summary>
public class WorktreeCreateMiddlewareTest
{
    /// <summary>
    /// 创建中间件实例与 mock，捕获传给 ExecuteGitCommandAsync 的 git 参数。
    /// 默认 Mock：HasLocalBranchAsync 返回 false，ExecuteGitCommandAsync 返回成功。
    /// 返回 opsMock 供调用方按场景重新配置。
    /// </summary>
    private static (WorktreeCreateMiddleware middleware, Mock<IWorktreePipelineOperations> opsMock, List<string> capturedArgs) CreateMiddleware()
    {
        var capturedArgs = new List<string>();
        var opsMock = new Mock<IWorktreePipelineOperations>();
        opsMock
            .Setup(x => x.HasLocalBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        opsMock
            .Setup(x => x.ExecuteGitCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, args, _) => capturedArgs.Add(args))
            .ReturnsAsync(new GitCommandResult { Success = true });

        var fs = new InMemoryFileOperationService();
        var clockMock = new Mock<IClockService>();
        clockMock.Setup(x => x.GetUtcNow()).Returns(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var middleware = new WorktreeCreateMiddleware(
            new Lazy<IWorktreePipelineOperations>(() => opsMock.Object),
            fs,
            clockMock.Object);
        return (middleware, opsMock, capturedArgs);
    }

    /// <summary>
    /// 构造测试上下文，BranchName/WorktreePath 默认为空字符串（模拟上游未填充的场景）。
    /// </summary>
    private static WorktreeCreateContext CreateContext(string agentId, string gitRoot, string branchName = "", string worktreePath = "", WorktreeOptions? options = null)
        => new()
        {
            AgentId = agentId,
            GitRoot = gitRoot,
            BranchName = branchName,
            WorktreePath = worktreePath,
            Options = options,
        };

    /// <summary>
    /// 空操作 next 委托 — 测试只关注中间件自身行为，不关心下游。
    /// </summary>
    private static Task NextNoOp(WorktreeCreateContext ctx, CancellationToken ct) => Task.CompletedTask;

    // === bug 修复核心场景 ===

    [Fact]
    public async Task InvokeAsync_EmptyBranchName_AutoGeneratesFromAgentId()
    {
        var (middleware, _, capturedArgs) = CreateMiddleware();
        var context = CreateContext("test-agent", "/repo");

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.BranchName.Should().Be("worktree-test-agent");
        context.Failed.Should().BeFalse();
        capturedArgs.Should().NotBeEmpty();
        capturedArgs[0].Should().Contain("worktree-test-agent");
        // bug 核心症状：git 命令不应包含空分支名导致的 "-B  "（B 后双空格）
        capturedArgs[0].Should().NotContain("-B  ");
    }

    [Fact]
    public async Task InvokeAsync_NonEmptyBranchName_ShouldNotOverwrite()
    {
        var (middleware, _, capturedArgs) = CreateMiddleware();
        var context = CreateContext("test-agent", "/repo", branchName: "feature-x");

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.BranchName.Should().Be("feature-x");
        context.Failed.Should().BeFalse();
        capturedArgs.Should().NotBeEmpty();
        capturedArgs[0].Should().Contain("feature-x");
        capturedArgs[0].Should().NotContain("worktree-test-agent");
    }

    [Fact]
    public async Task InvokeAsync_EmptyWorktreePath_AutoGeneratesFromGitRootAndAgentId()
    {
        var (middleware, _, capturedArgs) = CreateMiddleware();
        var context = CreateContext("test-agent", "/repo");

        await middleware.InvokeAsync(context, NextNoOp, default);

        var expectedPath = AgentWorktreeSession.GenerateWorktreePath("/repo", "test-agent");
        context.WorktreePath.Should().Be(expectedPath);
        context.Failed.Should().BeFalse();
        capturedArgs.Should().NotBeEmpty();
        capturedArgs[0].Should().Contain(expectedPath);
    }

    [Fact]
    public async Task InvokeAsync_NonEmptyWorktreePath_ShouldNotOverwrite()
    {
        var (middleware, _, capturedArgs) = CreateMiddleware();
        var customPath = "/custom/worktree/path";
        var context = CreateContext("test-agent", "/repo", worktreePath: customPath);

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.WorktreePath.Should().Be(customPath);
        context.Failed.Should().BeFalse();
        capturedArgs.Should().NotBeEmpty();
        capturedArgs[0].Should().Contain(customPath);
    }

    [Fact]
    public async Task InvokeAsync_BothEmpty_AutoGeneratesBoth()
    {
        var (middleware, _, capturedArgs) = CreateMiddleware();
        var context = CreateContext("my-agent", "/project");

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.BranchName.Should().Be("worktree-my-agent");
        var expectedPath = AgentWorktreeSession.GenerateWorktreePath("/project", "my-agent");
        context.WorktreePath.Should().Be(expectedPath);
        context.Failed.Should().BeFalse();
        capturedArgs.Should().NotBeEmpty();
        capturedArgs[0].Should().Contain("worktree-my-agent");
        capturedArgs[0].Should().Contain(expectedPath);
    }

    [Fact]
    public async Task InvokeAsync_BothProvided_ShouldNotOverwriteEither()
    {
        var (middleware, _, capturedArgs) = CreateMiddleware();
        var context = CreateContext("my-agent", "/project", branchName: "dev-branch", worktreePath: "/custom/wt");

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.BranchName.Should().Be("dev-branch");
        context.WorktreePath.Should().Be("/custom/wt");
        context.Failed.Should().BeFalse();
        capturedArgs.Should().NotBeEmpty();
        capturedArgs[0].Should().Contain("dev-branch");
        capturedArgs[0].Should().Contain("/custom/wt");
    }

    [Fact]
    public async Task InvokeAsync_EmptyBranchName_GitCommandUsesGeneratedBranchNotEmpty()
    {
        var (middleware, _, capturedArgs) = CreateMiddleware();
        var context = CreateContext("agent-42", "/repo");

        await middleware.InvokeAsync(context, NextNoOp, default);

        capturedArgs.Should().NotBeEmpty();
        // git worktree add 命令应包含 -B worktree-agent-42
        capturedArgs[0].Should().Contain("-B worktree-agent-42");
        // 不应出现空分支名导致的畸形参数（-B 后紧跟空格再空格）
        capturedArgs[0].Should().NotContain("-B  ");
    }

    [Fact]
    public async Task InvokeAsync_EmptyBranchNameWithSlashes_FlattensToPlusInGeneratedBranch()
    {
        var (middleware, _, capturedArgs) = CreateMiddleware();
        var context = CreateContext("user/feature", "/repo");

        await middleware.InvokeAsync(context, NextNoOp, default);

        // 斜杠应被展平为 +，避免 git 分支名非法
        context.BranchName.Should().Be("worktree-user+feature");
        context.Failed.Should().BeFalse();
        capturedArgs.Should().NotBeEmpty();
        capturedArgs[0].Should().Contain("worktree-user+feature");
    }

    // === 分支覆盖场景 ===

    [Fact]
    public async Task InvokeAsync_GitCreateFails_ShouldFailContextButBranchNameStillGenerated()
    {
        var (middleware, opsMock, capturedArgs) = CreateMiddleware();
        opsMock
            .Setup(x => x.ExecuteGitCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, args, _) => capturedArgs.Add(args))
            .ReturnsAsync(new GitCommandResult { Success = false, Error = "fatal: '' is not a valid branch name" });
        var context = CreateContext("test-agent", "/repo");

        await middleware.InvokeAsync(context, NextNoOp, default);

        // bug 修复：即使 git 命令失败，分支名也应已生成（不再是空）
        context.BranchName.Should().Be("worktree-test-agent");
        context.Failed.Should().BeTrue();
        context.ErrorMessage.Should().Contain("创建 worktree 失败");
    }

    [Fact]
    public async Task InvokeAsync_HasLocalBranchTrue_UsesLocalBranchCommandFormat()
    {
        var (middleware, opsMock, capturedArgs) = CreateMiddleware();
        opsMock
            .Setup(x => x.HasLocalBranchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var context = CreateContext("test-agent", "/repo", branchName: "existing-branch");

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.Failed.Should().BeFalse();
        capturedArgs.Should().NotBeEmpty();
        // hasLocalRef=true 时命令格式：worktree add "path" -B branch（无 baseRef）
        capturedArgs[0].Should().StartWith("worktree add ");
        capturedArgs[0].Should().Contain("existing-branch");
        capturedArgs[0].Should().NotContain("HEAD");
    }

    [Fact]
    public async Task InvokeAsync_WithSparsePaths_AppliesSparseCheckoutAndSucceeds()
    {
        var (middleware, opsMock, capturedArgs) = CreateMiddleware();
        opsMock
            .Setup(x => x.ApplySparseCheckoutAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var context = CreateContext("test-agent", "/repo", options: new WorktreeOptions { SparsePaths = new List<string> { "src/", "tests/" } });

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.Failed.Should().BeFalse();
        context.BranchName.Should().Be("worktree-test-agent");
        // 应执行多条 git 命令：worktree add (--no-checkout) + checkout HEAD
        capturedArgs.Should().HaveCountGreaterThanOrEqualTo(2);
        capturedArgs.Should().Contain(args => args.Contains("worktree add"));
        capturedArgs.Should().Contain(args => args.Contains("checkout HEAD"));
    }

    [Fact]
    public async Task InvokeAsync_SparseCheckoutFails_RollsBackAndFails()
    {
        var (middleware, opsMock, capturedArgs) = CreateMiddleware();
        opsMock
            .Setup(x => x.ApplySparseCheckoutAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var context = CreateContext("test-agent", "/repo", options: new WorktreeOptions { SparsePaths = new List<string> { "src/" } });

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.Failed.Should().BeTrue();
        context.ErrorMessage.Should().Contain("稀疏检出失败");
        // bug 修复：回滚命令中应使用生成的分支名（非空）
        context.BranchName.Should().Be("worktree-test-agent");
        // 应包含回滚命令
        capturedArgs.Should().Contain(args => args.Contains("worktree remove"));
        capturedArgs.Should().Contain(args => args.Contains("branch -D worktree-test-agent"));
    }

    [Fact]
    public async Task InvokeAsync_CheckoutHeadFails_RollsBackAndFails()
    {
        var (middleware, opsMock, capturedArgs) = CreateMiddleware();
        opsMock
            .Setup(x => x.ApplySparseCheckoutAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // ExecuteGitCommandAsync 第一次（worktree add）成功，第二次（checkout HEAD）失败
        var callCount = 0;
        opsMock
            .Setup(x => x.ExecuteGitCommandAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, args, _) => { capturedArgs.Add(args); callCount++; })
            .Returns(() => Task.FromResult(callCount == 1
                ? new GitCommandResult { Success = true }
                : new GitCommandResult { Success = false, Error = "checkout failed" }));
        var context = CreateContext("test-agent", "/repo", options: new WorktreeOptions { SparsePaths = new List<string> { "src/" } });

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.Failed.Should().BeTrue();
        context.ErrorMessage.Should().Contain("checkout HEAD 失败");
        // bug 修复：回滚时分支名已生成
        context.BranchName.Should().Be("worktree-test-agent");
        capturedArgs.Should().Contain(args => args.Contains("worktree remove"));
        capturedArgs.Should().Contain(args => args.Contains("branch -D worktree-test-agent"));
    }

    [Fact]
    public async Task InvokeAsync_WithBaseBranch_UsesBaseRefInCommand()
    {
        var (middleware, _, capturedArgs) = CreateMiddleware();
        var context = CreateContext("test-agent", "/repo");
        context.BaseBranch = "main";

        await middleware.InvokeAsync(context, NextNoOp, default);

        context.Failed.Should().BeFalse();
        capturedArgs.Should().NotBeEmpty();
        // baseBranch 非空时命令末尾应使用 baseRef 而非 HEAD
        capturedArgs[0].Should().Contain("main");
    }
}
