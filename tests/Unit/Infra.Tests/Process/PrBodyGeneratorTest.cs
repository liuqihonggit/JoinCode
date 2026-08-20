namespace Infra.Tests.Process;

/// <summary>
/// PrBodyGenerator 单元测试 — 验证从 commits/分支名/diff/模板生成 PR body
/// </summary>
public sealed class PrBodyGeneratorTest
{
    private readonly Mock<IGitCommandRunner> _gitRunner = new();
    private readonly PrBodyGenerator _generator;

    public PrBodyGeneratorTest()
    {
        _generator = new PrBodyGenerator(_gitRunner.Object);
    }

    // === GenerateFromCommitsAsync ===

    [Fact]
    public async Task GenerateFromCommitsAsync_SuccessWithCommits_ReturnsCommitList()
    {
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = "feat: add A\nfix: fix B" });

        var result = await _generator.GenerateFromCommitsAsync("main", "feature");

        result.Should().Contain("## 变更内容");
        result.Should().Contain("- feat: add A");
        result.Should().Contain("- fix: fix B");
    }

    [Fact]
    public async Task GenerateFromCommitsAsync_SingleCommit_ReturnsSingleItem()
    {
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = "feat: solo commit" });

        var result = await _generator.GenerateFromCommitsAsync("main", "feature");

        result.Should().Contain("- feat: solo commit");
    }

    [Fact]
    public async Task GenerateFromCommitsAsync_EmptyOutput_FallsBackToBranchName()
    {
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = "" });

        var result = await _generator.GenerateFromCommitsAsync("main", "feature");

        result.Should().Contain("分支: `feature`");
        result.Should().Contain("## 变更内容");
    }

    [Fact]
    public async Task GenerateFromCommitsAsync_Failure_FallsBackToBranchName()
    {
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = false, Output = "" });

        var result = await _generator.GenerateFromCommitsAsync("main", "feature");

        result.Should().Contain("分支: `feature`");
    }

    [Fact]
    public async Task GenerateFromCommitsAsync_PassesCorrectGitLogArguments()
    {
        GitCommandResult capturedArgs = new() { Success = true, Output = "commit" };
        string? capturedArgsStr = null;
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((args, _, _) => capturedArgsStr = args)
            .ReturnsAsync(capturedArgs);

        await _generator.GenerateFromCommitsAsync("main", "feature");

        capturedArgsStr.Should().Be("log main..feature --pretty=format:%s");
    }

    // === GenerateFromBranchName ===

    [Fact]
    public void GenerateFromBranchName_ValidBranch_IncludesBranchName()
    {
        var result = PrBodyGenerator.GenerateFromBranchName("feature/add-login");

        result.Should().Contain("分支: `feature/add-login`");
        result.Should().Contain("## 变更内容");
        result.Should().Contain("### 变更类型");
        result.Should().Contain("- [ ] 新功能");
        result.Should().Contain("- [ ] Bug 修复");
        result.Should().Contain("- [ ] 重构");
        result.Should().Contain("- [ ] 文档更新");
        result.Should().Contain("- [ ] 其他");
        result.Should().Contain("### 变更描述");
    }

    [Fact]
    public void GenerateFromBranchName_EmptyBranch_ReturnsPlaceholder()
    {
        var result = PrBodyGenerator.GenerateFromBranchName("");

        result.Should().Contain("（请在 PR 中描述变更内容）");
    }

    [Fact]
    public void GenerateFromBranchName_NullBranch_ReturnsPlaceholder()
    {
        var result = PrBodyGenerator.GenerateFromBranchName(null!);

        result.Should().Contain("（请在 PR 中描述变更内容）");
    }

    [Fact]
    public void GenerateFromBranchName_WhitespaceBranch_ReturnsPlaceholder()
    {
        var result = PrBodyGenerator.GenerateFromBranchName("   ");

        result.Should().Contain("（请在 PR 中描述变更内容）");
    }

    // === GenerateFromDiffAsync ===

    [Fact]
    public async Task GenerateFromDiffAsync_Success_ReturnsDiffStat()
    {
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = "file1.cs | 10 +++---\nfile2.cs | 5 ++" });

        var result = await _generator.GenerateFromDiffAsync("main", "feature");

        result.Should().Contain("## 变更文件");
        result.Should().Contain("file1.cs | 10 +++---");
        result.Should().Contain("file2.cs | 5 ++");
        result.Should().Contain("```");
    }

    [Fact]
    public async Task GenerateFromDiffAsync_EmptyOutput_FallsBackToBranchName()
    {
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = true, Output = "" });

        var result = await _generator.GenerateFromDiffAsync("main", "feature");

        result.Should().Contain("分支: `feature`");
    }

    [Fact]
    public async Task GenerateFromDiffAsync_Failure_FallsBackToBranchName()
    {
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GitCommandResult { Success = false, Output = "" });

        var result = await _generator.GenerateFromDiffAsync("main", "feature");

        result.Should().Contain("分支: `feature`");
    }

    [Fact]
    public async Task GenerateFromDiffAsync_PassesCorrectGitDiffArguments()
    {
        string? capturedArgsStr = null;
        _gitRunner.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((args, _, _) => capturedArgsStr = args)
            .ReturnsAsync(new GitCommandResult { Success = true, Output = "stat" });

        await _generator.GenerateFromDiffAsync("main", "feature");

        capturedArgsStr.Should().Be("diff main..feature --stat");
    }

    // === GenerateWithTemplate ===

    [Fact]
    public void GenerateWithTemplate_WithDescription_IncludesDescription()
    {
        var result = PrBodyGenerator.GenerateWithTemplate("Add feature", "This adds X feature");

        result.Should().Contain("## Add feature");
        result.Should().Contain("This adds X feature");
        result.Should().Contain("### 变更类型");
        result.Should().Contain("### 测试");
        result.Should().Contain("- [ ] 新功能");
        result.Should().Contain("- [ ] Bug 修复");
        result.Should().Contain("- [ ] 重构");
    }

    [Fact]
    public void GenerateWithTemplate_NoDescription_OmitsDescriptionContent()
    {
        var result = PrBodyGenerator.GenerateWithTemplate("Add feature");

        result.Should().Contain("## Add feature");
        result.Should().Contain("### 测试");
    }

    [Fact]
    public void GenerateWithTemplate_NullDescription_OmitsDescriptionContent()
    {
        var result = PrBodyGenerator.GenerateWithTemplate("Add feature", null);

        result.Should().Contain("## Add feature");
        result.Should().Contain("### 测试");
    }

    [Fact]
    public void GenerateWithTemplate_EmptyDescription_OmitsDescriptionContent()
    {
        var result = PrBodyGenerator.GenerateWithTemplate("Add feature", "");

        result.Should().Contain("## Add feature");
        result.Should().Contain("### 测试");
    }
}
