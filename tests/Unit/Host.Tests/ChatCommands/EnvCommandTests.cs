namespace Host.Tests.ChatCommands;

public sealed class EnvCommandTests
{
    [Fact]
    public void Name_Should_Be_env()
    {
        var cmd = new EnvCommand();
        cmd.Name.Should().Be("env");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new EnvCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new EnvCommand();
        cmd.Usage.Should().StartWith("/env");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new EnvCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_NoArgs_Should_List_All_Env_Vars()
    {
        var cmd = new EnvCommand();
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
    public async Task Execute_WithFilter_Should_Filter_Vars()
    {
        var cmd = new EnvCommand();
        var context = new ChatCommandContext {
            Arguments = "JCC",
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
}