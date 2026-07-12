namespace Host.Tests.ChatCommands;

public sealed class StatusCommandTests
{
    [Fact]
    public void Name_Should_Be_status()
    {
        var cmd = new StatusCommand();
        cmd.Name.Should().Be("status");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new StatusCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new StatusCommand();
        cmd.Usage.Should().StartWith("/status");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new StatusCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Continue()
    {
        var cmd = new StatusCommand();
        var catalog = new ModelCatalog(new Core.Configuration.Providers.ProviderDefinitionRegistry());
        var mockProvider = new Mock<IServiceProvider>();
        mockProvider.Setup(p => p.GetService(typeof(IModelCatalog))).Returns(catalog);

        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ServiceProvider = mockProvider.Object,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }
}