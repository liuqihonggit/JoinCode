
namespace JoinCode.ChatCommands.Tests;

public class VimCommandTests
{
    [Fact]
    public async Task VimOn_CallsVimEngineEnable()
    {
        var vimEngineMock = new Mock<IVimEngine>();
        vimEngineMock.SetupGet(v => v.IsEnabled).Returns(false);

        var command = new VimCommand();
        var context = BuildContext("on", vimEngineMock.Object);

        await command.ExecuteAsync(context).ConfigureAwait(true);

        vimEngineMock.Verify(v => v.Enable(), Times.Once);
    }

    [Fact]
    public async Task VimOff_CallsVimEngineDisable()
    {
        var vimEngineMock = new Mock<IVimEngine>();
        var command = new VimCommand();
        var context = BuildContext("off", vimEngineMock.Object);

        await command.ExecuteAsync(context).ConfigureAwait(true);

        vimEngineMock.Verify(v => v.Disable(), Times.Once);
    }

    [Fact]
    public async Task VimEmpty_ShowsCurrentStatus()
    {
        var vimEngineMock = new Mock<IVimEngine>();
        vimEngineMock.SetupGet(v => v.IsEnabled).Returns(true);
        vimEngineMock.SetupGet(v => v.CurrentMode).Returns(VimMode.Normal);

        var command = new VimCommand();
        var context = BuildContext("", vimEngineMock.Object);

        var result = await command.ExecuteAsync(context).ConfigureAwait(true);

        result.IsHandled.Should().BeTrue();
    }

    private static ChatCommandContext BuildContext(string arguments, IVimEngine vimEngine)
    {
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IVimEngine))).Returns(vimEngine);

        return new ChatCommandContext {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                ServiceProvider = serviceProviderMock.Object,
             FileSystem = TestFileSystem.Current,
             },
        };
    }
}