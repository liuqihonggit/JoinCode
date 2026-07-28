
namespace Core.Tests.Hooks.ToolPermission;

/// <summary>
/// PermissionHookExecutor 测试
/// </summary>
public class PermissionHookExecutorTests
{
    private readonly PermissionHookExecutor _executor;
    private readonly Mock<IHookOrchestrator> _orchestratorMock;

    public PermissionHookExecutorTests()
    {
        _orchestratorMock = new Mock<IHookOrchestrator>();
        _executor = new PermissionHookExecutor(_orchestratorMock.Object, NullLogger<PermissionHookExecutor>.Instance);
    }

    [Fact]
    public async Task RegisterHookAsync_ShouldNotThrow()
    {
        var hook = new TestPermissionHook("TestHook");

        Func<Task> act = async () => await _executor.RegisterHookAsync(hook).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task UnregisterHookAsync_ShouldNotThrow()
    {
        Func<Task> act = async () => await _executor.UnregisterHookAsync("NonExistent").ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteHooksAsync_WithAllowResult_ShouldReturnAllow()
    {
        var expectedResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            PermissionRequestResult = new PermissionAllowResult()
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(It.IsAny<HookInput>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { expectedResult }.ToAsyncEnumerable());

        var results = new List<PermissionHookResult>();
        await foreach (var result in _executor.ExecuteHooksAsync(
            "test-tool", "tool-1", new Dictionary<string, JsonElement>(), null, null))
        {
            results.Add(result);
        }

        results.Should().HaveCount(1);
        results[0].PermissionRequestResult!.Behavior.Should().Be(PermissionBehavior.Allow);
    }

    [Fact]
    public async Task ExecuteHooksAsync_WithDenyResult_ShouldReturnDeny()
    {
        var expectedResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            PermissionRequestResult = new PermissionDenyResult { Message = "Denied" }
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(It.IsAny<HookInput>(), It.IsAny<CancellationToken>()))
            .Returns(new[] { expectedResult }.ToAsyncEnumerable());

        var results = new List<PermissionHookResult>();
        await foreach (var result in _executor.ExecuteHooksAsync(
            "test-tool", "tool-1", new Dictionary<string, JsonElement>(), null, null))
        {
            results.Add(result);
        }

        results.Should().HaveCount(1);
        results[0].PermissionRequestResult!.Behavior.Should().Be(PermissionBehavior.Deny);
    }

    [Fact]
    public async Task ExecuteHooksAsync_NoResults_ShouldReturnEmpty()
    {
        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(It.IsAny<HookInput>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<HookResult>());

        var results = new List<PermissionHookResult>();
        await foreach (var result in _executor.ExecuteHooksAsync(
            "test-tool", "tool-1", new Dictionary<string, JsonElement>(), null, null))
        {
            results.Add(result);
        }

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRegisteredHookCountAsync_ShouldReturnZero()
    {
        var count = await _executor.GetRegisteredHookCountAsync().ConfigureAwait(true);

        count.Should().Be(0);
    }
}

/// <summary>
/// 测试用的权限 Hook
/// </summary>
internal class TestPermissionHook : IPermissionHook
{
    public string Name { get; }

    public TestPermissionHook(string name)
    {
        Name = name;
    }

    public Task<PermissionHookResult?> ExecuteAsync(PermissionHookContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PermissionHookResult?>(new PermissionHookResult
        {
            HookName = Name
        });
    }
}
