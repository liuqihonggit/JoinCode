namespace JoinCode.Agents.Tests.Worktree;

public class WorktreeAlignmentTests
{
    [Fact]
    public void ParsePRReference_ShouldParseHashFormat()
    {
        var result = AgentWorktreeSession.ParsePRReference("#123");
        result.Should().Be(123);
    }

    [Fact]
    public void ParsePRReference_ShouldParseGitHubUrl()
    {
        var result = AgentWorktreeSession.ParsePRReference("https://github.com/owner/repo/pull/456");
        result.Should().Be(456);
    }

    [Fact]
    public void ParsePRReference_ShouldParseGitHubUrlWithTrailingSlash()
    {
        var result = AgentWorktreeSession.ParsePRReference("https://github.com/owner/repo/pull/789/");
        result.Should().Be(789);
    }

    [Fact]
    public void ParsePRReference_ShouldReturnNullForNonPR()
    {
        AgentWorktreeSession.ParsePRReference("feature-branch").Should().BeNull();
        AgentWorktreeSession.ParsePRReference("").Should().BeNull();
        AgentWorktreeSession.ParsePRReference("123").Should().BeNull();
    }

    [Fact]
    public void GenerateBranchName_ShouldFlattenSlashes()
    {
        var result = AgentWorktreeSession.GenerateBranchName("user/feature");
        result.Should().Be("worktree-user+feature");
    }

    [Fact]
    public void GenerateBranchName_ShouldNotTruncate()
    {
        var longId = new string('a', 64);
        var result = AgentWorktreeSession.GenerateBranchName(longId);
        result.Should().Be($"worktree-{longId}");
    }

    [Fact]
    public void GenerateBranchName_ShouldFlattenBackslashes()
    {
        var result = AgentWorktreeSession.GenerateBranchName("user\\feature");
        result.Should().Be("worktree-user+feature");
    }

    [Fact]
    public void GenerateWorktreePath_ShouldUseJccWorktreesDir()
    {
        var result = AgentWorktreeSession.GenerateWorktreePath("/home/user/project", "my-agent");
        var normalized = result.Replace('\\', '/');
        normalized.Should().Be("/home/user/project/.jcc/worktrees/my-agent");
    }

    [Fact]
    public void GenerateWorktreePath_ShouldFlattenSlashes()
    {
        var result = AgentWorktreeSession.GenerateWorktreePath("/home/user/project", "user/feature");
        var normalized = result.Replace('\\', '/');
        normalized.Should().Be("/home/user/project/.jcc/worktrees/user+feature");
    }

    [Theory]
    [InlineData("agent-a1b2c3d", true)]
    [InlineData("agent-a1b2c3d4", true)]
    [InlineData("wf_a1b2c3d4-e1f-5", true)]
    [InlineData("wf-1", true)]
    [InlineData("bridge-session1-part2", true)]
    [InlineData("job-mytemplate-a1b2c3d4", true)]
    [InlineData("my-feature", false)]
    [InlineData("work-main", false)]
    [InlineData("wf-myfeature", false)]
    public void EphemeralPatterns_ShouldMatchCorrectly(string dirName, bool expectedMatch)
    {
        var opts = new WorktreeOptions();
        var isEphemeral = opts.EphemeralPatterns.Any(pattern =>
        {
            try { return System.Text.RegularExpressions.Regex.IsMatch(dirName, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
            catch { return false; }
        });
        isEphemeral.Should().Be(expectedMatch);
    }

    [Fact]
    public void WorktreeOptions_PrNumber_ShouldDefaultToNull()
    {
        var opts = new WorktreeOptions();
        opts.PrNumber.Should().BeNull();
    }

    [Fact]
    public void WorktreeOptions_PrNumber_ShouldBeSettable()
    {
        var opts = new WorktreeOptions { PrNumber = 42 };
        opts.PrNumber.Should().Be(42);
    }

    [Fact]
    public async Task KeepWorktreeAsync_ShouldRemoveSessionButKeepDirectory()
    {
        var fs = new InMemoryFileOperationService();
        var processService = new Mock<IProcessService>();
        var service = new AgentWorktreeService(fs, processService.Object);

        var session = new AgentWorktreeSession
        {
            AgentId = "test-agent",
            OriginalCwd = "/home/user/project",
            WorktreePath = "/home/user/project/.jcc/worktrees/test-agent",
            BranchName = "worktree-test-agent",
            GitRootPath = "/home/user/project",
            CreatedAt = DateTime.UtcNow,
            Existed = false
        };

        await service.SaveSessionAsync(session);
        var savedSession = await service.GetSessionAsync("test-agent");
        savedSession.Should().NotBeNull();

        await service.KeepWorktreeAsync("test-agent");

        var afterKeep = await service.GetSessionAsync("test-agent");
        afterKeep.Should().BeNull();
    }
}
