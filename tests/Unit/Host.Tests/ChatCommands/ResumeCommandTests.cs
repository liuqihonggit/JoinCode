namespace Host.Tests.ChatCommands;

public sealed class ResumeCommandTests
{
    [Fact]
    public void Name_Should_Be_resume()
    {
        var cmd = new ResumeCommand();
        cmd.Name.Should().Be("resume");
    }

    [Fact]
    public void Description_Should_Be_恢复之前的会话()
    {
        var cmd = new ResumeCommand();
        cmd.Description.Should().Be("恢复之前的会话");
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new ResumeCommand();
        cmd.Usage.Should().StartWith("/resume");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new ResumeCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Contain_continue()
    {
        var cmd = new ResumeCommand();
        cmd.Aliases.Should().Contain("continue");
    }

    [Fact]
    public void ArgumentHint_Should_Not_Be_Empty()
    {
        var cmd = new ResumeCommand();
        cmd.ArgumentHint.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Execute_NoArgs_Should_Return_Continue()
    {
        var cmd = new ResumeCommand();
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithSessionId_Should_Return_Continue()
    {
        var cmd = new ResumeCommand();
        var context = new ChatCommandContext {
            Arguments = "test-session",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public void CrossProjectResumeResult_SameProject_Should_Not_Be_CrossProject()
    {
        var result = CrossProjectResumeResult.SameProject();
        result.IsCrossProject.Should().BeFalse();
    }

    [Fact]
    public void CrossProjectResumeResult_SameRepoWorktree_Should_Be_CrossProject_And_SameRepo()
    {
        var result = CrossProjectResumeResult.SameRepoWorktree("/path/to/worktree");
        result.IsCrossProject.Should().BeTrue();
        result.IsSameRepoWorktree.Should().BeTrue();
        result.ProjectPath.Should().Be("/path/to/worktree");
    }

    [Fact]
    public void CrossProjectResumeResult_DifferentProject_Should_Be_CrossProject_And_Not_SameRepo()
    {
        var result = CrossProjectResumeResult.DifferentProject("/path/to/other/project");
        result.IsCrossProject.Should().BeTrue();
        result.IsSameRepoWorktree.Should().BeFalse();
        result.ProjectPath.Should().Be("/path/to/other/project");
    }
}