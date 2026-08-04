
namespace Core.Tests.Hooks.Lifecycle;

using JoinCode.Abstractions.Models.Agent;
using JoinCode.Abstractions.Models.Goal;

public class ClusterPlanApprovalHookManagerTests
{
    private readonly Mock<IHookOrchestrator> _orchestratorMock;
    private readonly ClusterPlanApprovalHookManager _sut;

    public ClusterPlanApprovalHookManagerTests()
    {
        _orchestratorMock = new Mock<IHookOrchestrator>();
        _sut = new ClusterPlanApprovalHookManager(_orchestratorMock.Object);
    }

    [Fact]
    public async Task OnClusterPlanApprovalAsync_NoHooksRegistered_ShouldProceed()
    {
        var context = CreateContext();
        SetupOrchestrator([]);

        var result = await _sut.OnClusterPlanApprovalAsync(context);

        result.ShouldProceed.Should().BeTrue();
    }

    [Fact]
    public async Task OnClusterPlanApprovalAsync_BlockingHook_ShouldReturnBlock()
    {
        var context = CreateContext();
        SetupOrchestrator([new HookResult { Outcome = HookOutcome.Blocking, Message = "plan rejected" }]);

        var result = await _sut.OnClusterPlanApprovalAsync(context);

        result.ShouldProceed.Should().BeFalse();
        result.Message.Should().Be("plan rejected");
    }

    [Fact]
    public async Task OnClusterPlanApprovalAsync_PreventContinuation_ShouldReturnBlock()
    {
        var context = CreateContext();
        SetupOrchestrator([new HookResult { PreventContinuation = true, Outcome = HookOutcome.Blocking, Message = "prevented" }]);

        var result = await _sut.OnClusterPlanApprovalAsync(context);

        result.ShouldProceed.Should().BeFalse();
        result.Message.Should().Be("prevented");
    }

    [Fact]
    public async Task OnClusterPlanApprovalAsync_NonBlockingHook_ShouldProceed()
    {
        var context = CreateContext();
        SetupOrchestrator([new HookResult { Outcome = HookOutcome.Success, Message = "ok" }]);

        var result = await _sut.OnClusterPlanApprovalAsync(context);

        result.ShouldProceed.Should().BeTrue();
    }

    [Fact]
    public async Task OnClusterPlanApprovalAsync_ShouldPassPlanInfoInPayload()
    {
        var context = CreateContext();
        Dictionary<string, JsonElement>? capturedPayload = null;

        _orchestratorMock.Setup(o => o.ExecuteHooksAsync(
                HookEvent.ClusterPlanApproval,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((HookEvent _, Dictionary<string, JsonElement> payload, string? _, string? _, CancellationToken _) =>
            {
                capturedPayload = payload;
                return AsyncEnumerable.Empty<HookResult>();
            });

        await _sut.OnClusterPlanApprovalAsync(context);

        capturedPayload.Should().NotBeNull();
        capturedPayload!["session_id"].GetString().Should().Be("session-001");
        capturedPayload["objective"].GetString().Should().Be("test objective");
        capturedPayload["is_decomposable"].GetBoolean().Should().BeTrue();
        capturedPayload["sub_task_count"].GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task OnClusterPlanApprovalAsync_WithValidationResult_ShouldIncludeInPayload()
    {
        var plan = CreatePlan();
        plan.ValidationResult = ClusterPlanValidationResult.Valid([]);
        var context = new ClusterPlanApprovalHookContext
        {
            SessionId = "session-001",
            Objective = "test",
            Plan = plan
        };

        Dictionary<string, JsonElement>? capturedPayload = null;
        _orchestratorMock.Setup(o => o.ExecuteHooksAsync(
                HookEvent.ClusterPlanApproval,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((HookEvent _, Dictionary<string, JsonElement> payload, string? _, string? _, CancellationToken _) =>
            {
                capturedPayload = payload;
                return AsyncEnumerable.Empty<HookResult>();
            });

        await _sut.OnClusterPlanApprovalAsync(context);

        capturedPayload.Should().NotBeNull();
        capturedPayload!.ContainsKey("is_valid").Should().BeTrue();
        capturedPayload["is_valid"].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task OnClusterPlanApprovalAsync_NullContext_ShouldThrow()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.OnClusterPlanApprovalAsync(null!));
    }

    private static ClusterPlanApprovalHookContext CreateContext()
    {
        return new ClusterPlanApprovalHookContext
        {
            SessionId = "session-001",
            Objective = "test objective",
            Plan = CreatePlan()
        };
    }

    private static ClusterPlan CreatePlan()
    {
        return new ClusterPlan
        {
            Objective = "test objective",
            Decomposition = DecompositionResult.Decomposable("test", [
                new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", OwnedFiles = ["a.cs"] },
                new SubTaskDefinition { Id = "sub_2", Title = "B", Description = "DB", OwnedFiles = ["b.cs"] }
            ]),
            ExecutionOptions = new ClusterExecutionOptions()
        };
    }

    private void SetupOrchestrator(IReadOnlyList<HookResult> results)
    {
        _orchestratorMock.Setup(o => o.ExecuteHooksAsync(
                HookEvent.ClusterPlanApproval,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(results));
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IReadOnlyList<T> list)
    {
        foreach (var item in list)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
