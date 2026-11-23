namespace Core.Tests.Hooks.Lifecycle;

/// <summary>
/// TASK130-A6：SessionStart hook 的上下文类输出必须进类型化字段。
/// 改前它们被写进 AdditionalConfig 的字符串键（全仓无读取方），且解析层根本不产出这两个字段。
/// </summary>
public class SessionStartHookManagerTests {
    private readonly Mock<IHookOrchestrator> _orchestratorMock;
    private readonly SessionStartHookManager _sut;

    public SessionStartHookManagerTests() {
        _orchestratorMock = new Mock<IHookOrchestrator>();
        _sut = new SessionStartHookManager(_orchestratorMock.Object);
    }

    [Fact]
    public async Task OnSessionStartAsync_NoHooks_ProceedsWithEmptyContext() {
        SetupOrchestrator([]);

        var result = await _sut.OnSessionStartAsync(CreateContext());

        result.ShouldProceed.Should().BeTrue();
        result.AdditionalContext.Should().BeNull();
        result.InitialUserMessage.Should().BeNull();
        result.AdditionalConfig.Should().BeEmpty();
    }

    [Fact]
    public async Task OnSessionStartAsync_AdditionalContext_GoesToTypedProperty() {
        SetupOrchestrator([new HookResult { Outcome = HookOutcome.Success, AdditionalContext = "本会话只读" }]);

        var result = await _sut.OnSessionStartAsync(CreateContext());

        result.AdditionalContext.Should().Be("本会话只读");
        result.AdditionalConfig.Should().NotContainKey("additionalContext", "不许再走字符串键，唯一真相是类型化字段");
    }

    [Fact]
    public async Task OnSessionStartAsync_MultipleHooks_JoinsContextsInOrder() {
        SetupOrchestrator([
            new HookResult { Outcome = HookOutcome.Success, AdditionalContext = "第一条" },
            new HookResult { Outcome = HookOutcome.Success, AdditionalContext = "第二条" },
        ]);

        var result = await _sut.OnSessionStartAsync(CreateContext());

        result.AdditionalContext.Should().Be("第一条\n\n第二条");
    }

    [Fact]
    public async Task OnSessionStartAsync_InitialUserMessage_GoesToTypedProperty() {
        SetupOrchestrator([new HookResult { Outcome = HookOutcome.Success, InitialUserMessage = "继续上次的重构" }]);

        var result = await _sut.OnSessionStartAsync(CreateContext());

        result.InitialUserMessage.Should().Be("继续上次的重构");
        result.AdditionalConfig.Should().NotContainKey("initialUserMessage");
    }

    [Fact]
    public async Task OnSessionStartAsync_UpdatedInput_StaysInAdditionalConfig() {
        var updatedInput = new Dictionary<string, JsonElement> {
            ["model"] = JsonElementHelper.FromString("opus"),
        };
        SetupOrchestrator([new HookResult { Outcome = HookOutcome.Success, UpdatedInput = updatedInput }]);

        var result = await _sut.OnSessionStartAsync(CreateContext());

        result.AdditionalConfig.Should().ContainKey("model");
        result.AdditionalContext.Should().BeNull();
    }

    [Fact]
    public async Task OnSessionStartAsync_Blocking_ReturnsNoContext() {
        SetupOrchestrator([
            new HookResult { Outcome = HookOutcome.Success, AdditionalContext = "早到的上下文" },
            new HookResult { Outcome = HookOutcome.Blocking, PreventContinuation = true, Message = "别进来" },
        ]);

        var result = await _sut.OnSessionStartAsync(CreateContext());

        result.ShouldProceed.Should().BeFalse();
        result.Message.Should().Be("别进来");
        result.AdditionalContext.Should().BeNull("被阻断的会话不该再带上下文");
    }

    private static SessionStartHookContext CreateContext() => new() {
        SessionId = "session-001",
        Source = "tui",
    };

    private void SetupOrchestrator(IReadOnlyList<HookResult> results) {
        _orchestratorMock.Setup(o => o.ExecuteHooksAsync(
                HookEvent.SessionStart,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(results));
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IReadOnlyList<T> list) {
        foreach (var item in list) {
            await Task.Yield();
            yield return item;
        }
    }
}
