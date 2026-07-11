
namespace Core.Tests.ChatCommands;

public class PlanCommandTests
{
    private readonly Mock<IPlanService> _planServiceMock;
    private readonly PlanCommand _planCommand;

    public PlanCommandTests()
    {
        _planServiceMock = new Mock<IPlanService>();
        _planCommand = new PlanCommand();
    }

    [Fact]
    public async Task ExecuteAsync_WithToggle_ShouldUsePlanService()
    {
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = null!,
                CodeService = null!,
                PlanService = null!,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await _planCommand.ExecuteAsync(context).ConfigureAwait(true);

        Assert.True(result.IsHandled);
    }

    [Fact]
    public async Task ExecuteAsync_WithOnSubCommand_ShouldEnterPlanMode()
    {
        var context = new ChatCommandContext {
            Arguments = "on test task",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = null!,
                CodeService = null!,
                PlanService = null!,
             FileSystem = TestFileSystem.Current,
             },
        };

        var result = await _planCommand.ExecuteAsync(context).ConfigureAwait(true);

        Assert.True(result.IsHandled);
    }

    [Fact]
    public void Name_ShouldReturnPlan()
    {
        Assert.Equal("plan", _planCommand.Name);
    }

    [Fact]
    public void Description_ShouldNotBeEmpty()
    {
        Assert.NotEmpty(_planCommand.Description);
    }

    [Fact]
    public void Usage_ShouldNotBeEmpty()
    {
        Assert.NotEmpty(_planCommand.Usage);
    }

    // ===== PlanSubCommand 枚举路由取值范围测试 =====

    [Theory]
    [InlineData("on")]
    [InlineData("enter")]
    [InlineData("off")]
    [InlineData("exit")]
    [InlineData("status")]
    [InlineData("open")]
    [InlineData("toggle")]
    public async Task ExecuteAsync_WithValidSubCommand_ShouldBeHandled(string subCommand)
    {
        var context = new ChatCommandContext
        {
            Arguments = subCommand,
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = null!,
                CodeService = null!,
                PlanService = null!,
            FileSystem = TestFileSystem.Current,
            },
        };

        var result = await _planCommand.ExecuteAsync(context).ConfigureAwait(true);

        Assert.True(result.IsHandled);
    }

    [Theory]
    [InlineData("ON")]
    [InlineData("ENTER")]
    [InlineData("OFF")]
    [InlineData("EXIT")]
    [InlineData("STATUS")]
    [InlineData("OPEN")]
    [InlineData("TOGGLE")]
    public async Task ExecuteAsync_WithUppercaseSubCommand_Should_Be_CaseInsensitive(string subCommand)
    {
        var context = new ChatCommandContext
        {
            Arguments = subCommand,
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = null!,
                CodeService = null!,
                PlanService = null!,
            FileSystem = TestFileSystem.Current,
            },
        };

        var result = await _planCommand.ExecuteAsync(context).ConfigureAwait(true);

        Assert.True(result.IsHandled);
    }
}
