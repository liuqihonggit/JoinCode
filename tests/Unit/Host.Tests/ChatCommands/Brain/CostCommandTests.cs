
namespace Core.Tests.Commands;

public class CostCommandTests : IDisposable
{
    private readonly CostCommand _command;
    private readonly CostTracker _costTracker;
    private readonly string _tempStoragePath;

    public CostCommandTests()
    {
        _command = new CostCommand();
        _tempStoragePath = "/test/cost_cmd.json";
        _costTracker = new CostTracker(new Mock<IFileOperationService>().Object, storagePath: _tempStoragePath, Microsoft.Extensions.Logging.Abstractions.NullLogger<CostTracker>.Instance);
    }

    public void Dispose()
    {
    }

    private ChatCommandContext CreateContext(string arguments, string sessionId = "test-session")
    {
        return new ChatCommandContext {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            SessionId = sessionId,
             Services = new CommandServices
             {
                ChatService = null!,
                CodeService = null!,
                PlanService = null!,
                CostTracker = _costTracker,
             FileSystem = TestFileSystem.Current,
             },
        };
    }

    [Fact]
    public void Name_ShouldBeCost()
    {
        _command.Name.Should().Be("cost");
    }

    [Fact]
    public void Description_ShouldNotBeEmpty()
    {
        _command.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_ShouldContainCost()
    {
        _command.Usage.Should().Contain("cost");
    }

    [Fact]
    public async Task ExecuteAsync_WithNoCostTracker_ShouldReturnContinue()
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

        var result = await _command.ExecuteAsync(context).ConfigureAwait(true);
        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithCostTracker_ShouldReturnContinue()
    {
        _costTracker.RecordUsage("gpt-4", 1000, 500, "test-session");
        var context = CreateContext("session", "test-session");

        var result = await _command.ExecuteAsync(context).ConfigureAwait(true);
        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithTodayArg_ShouldReturnContinue()
    {
        _costTracker.RecordUsage("gpt-4", 1000, 500);
        var context = CreateContext("today");

        var result = await _command.ExecuteAsync(context).ConfigureAwait(true);
        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithTotalArg_ShouldReturnContinue()
    {
        _costTracker.RecordUsage("gpt-4", 1000, 500);
        var context = CreateContext("total");

        var result = await _command.ExecuteAsync(context).ConfigureAwait(true);
        result.ShouldContinue.Should().BeTrue();
    }

    // ===== CostScope 枚举路由取值范围测试 =====

    [Theory]
    [InlineData("TODAY")]
    [InlineData("SESSION")]
    [InlineData("TOTAL")]
    public async Task ExecuteAsync_WithUppercaseScope_Should_Be_CaseInsensitive(string scope)
    {
        // 验证小写化路由(toLowerInvariant 后枚举匹配)
        _costTracker.RecordUsage("gpt-4", 1000, 500);
        var context = CreateContext(scope);

        var result = await _command.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownScope_Should_Fall_Through_To_Session()
    {
        // 未知 scope 走 default 分支 → Session 统计
        _costTracker.RecordUsage("gpt-4", 1000, 500);
        var context = CreateContext("unknown-scope");

        var result = await _command.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }
}
