namespace Core.Utils;

[Trait("Category", "Deterministic")]
public class GatewayActorEvaluateBreakerStateTests {
    [Fact]
    public void threshold_zero_disables_breaker() {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Open, TimeSpan.Zero, 0, TimeSpan.FromSeconds(30));
        reject.Should().BeFalse();
        newState.Should().BeNull();
    }

    [Fact]
    public void threshold_negative_disables_breaker() {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Open, TimeSpan.Zero, -1, TimeSpan.FromSeconds(30));
        reject.Should().BeFalse();
        newState.Should().BeNull();
    }

    [Fact]
    public void closed_state_never_rejects() {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Closed, TimeSpan.Zero, 5, TimeSpan.FromSeconds(30));
        reject.Should().BeFalse();
        newState.Should().BeNull();
    }

    [Fact]
    public void halfopen_state_never_rejects() {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.HalfOpen, TimeSpan.Zero, 5, TimeSpan.FromSeconds(30));
        reject.Should().BeFalse();
        newState.Should().BeNull();
    }

    [Fact]
    public void open_state_before_recovery_rejects() {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Open, TimeSpan.FromSeconds(10), 5, TimeSpan.FromSeconds(30));
        reject.Should().BeTrue();
        newState.Should().BeNull();
    }

    [Fact]
    public void open_state_at_recovery_boundary_transitions_to_halfopen() {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Open, TimeSpan.FromSeconds(30), 5, TimeSpan.FromSeconds(30));
        reject.Should().BeFalse();
        newState.Should().Be(GatewayCircuitState.HalfOpen);
    }

    [Fact]
    public void open_state_after_recovery_transitions_to_halfopen() {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            GatewayCircuitState.Open, TimeSpan.FromSeconds(60), 5, TimeSpan.FromSeconds(30));
        reject.Should().BeFalse();
        newState.Should().Be(GatewayCircuitState.HalfOpen);
    }

    [Theory]
    [InlineData(GatewayCircuitState.Closed, false, null)]
    [InlineData(GatewayCircuitState.HalfOpen, false, null)]
    public void non_open_states_never_reject(GatewayCircuitState state, bool expectedReject, object? _) {
        var (reject, newState) = GatewayActor<object, object>.EvaluateBreakerState(
            state, TimeSpan.FromSeconds(100), 5, TimeSpan.FromSeconds(30));
        reject.Should().Be(expectedReject);
        newState.Should().BeNull();
    }
}
