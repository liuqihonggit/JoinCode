namespace Core.Tests.Services.Policy;

public sealed class RemotePolicyServiceTests : IDisposable
{
    private readonly RemotePolicyService _service;
    private readonly HttpClient _httpClient;

    public RemotePolicyServiceTests()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var options = Options.Create(new RemotePolicyOptions
        {
            EnableCache = false
        });
        _service = new RemotePolicyService(_httpClient, options);
    }

    public void Dispose()
    {
        _service.Dispose();
        _httpClient.Dispose();
    }

    private static global::System.Collections.Concurrent.ConcurrentDictionary<string, PolicyRule> GetRules(RemotePolicyService service)
    {
        var rulesField = typeof(RemotePolicyService).GetField("_rules", global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
        return (global::System.Collections.Concurrent.ConcurrentDictionary<string, PolicyRule>)rulesField!.GetValue(service)!;
    }

    [Fact]
    public async Task EvaluateAsync_AllowedAction_ReturnsAllowed()
    {
        var result = await _service.EvaluateAsync("allowed-action").ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Allowed.Should().BeTrue();
        result.Action.Should().Be(PolicyAction.Allow);
    }

    [Fact]
    public async Task EvaluateAsync_DeniedAction_ReturnsDenied()
    {
        var rules = GetRules(_service);
        rules["deny-rule"] = new PolicyRule
        {
            RuleId = "deny-rule",
            Name = "Deny Test",
            Type = PolicyType.ToolRestriction,
            Action = PolicyAction.Deny,
            RestrictedTools = new List<string> { "dangerous-tool" },
            Enabled = true,
            Priority = 100
        };

        var result = await _service.EvaluateAsync("dangerous-tool").ConfigureAwait(true);

        result.Allowed.Should().BeFalse();
        result.Action.Should().Be(PolicyAction.Deny);
    }

    [Fact]
    public async Task GetActiveRulesAsync_ReturnsEnabledPolicies()
    {
        var rules = GetRules(_service);
        rules["enabled-rule"] = new PolicyRule
        {
            RuleId = "enabled-rule",
            Name = "Enabled Rule",
            Type = PolicyType.ToolUsageLimit,
            Action = PolicyAction.Deny,
            Enabled = true,
            Priority = 10
        };
        rules["disabled-rule"] = new PolicyRule
        {
            RuleId = "disabled-rule",
            Name = "Disabled Rule",
            Type = PolicyType.ToolUsageLimit,
            Action = PolicyAction.Deny,
            Enabled = false,
            Priority = 5
        };

        var activeRules = await _service.GetActiveRulesAsync().ConfigureAwait(true);

        activeRules.Should().ContainSingle(r => r.RuleId == "enabled-rule");
        activeRules.Should().NotContain(r => r.RuleId == "disabled-rule");
    }

    [Fact]
    public async Task RefreshAsync_WithNoEndpoint_DoesNotThrow()
    {
        var act = () => _service.RefreshAsync();

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task EvaluateAsync_WithEmptyAction_ThrowsArgumentException()
    {
        var act = () => _service.EvaluateAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ThrowsArgumentNullException()
    {
        var act = () => new RemotePolicyService(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task EvaluateAsync_UsageLimit_EnforcesLimit()
    {
        var rules = GetRules(_service);
        rules["limit-rule"] = new PolicyRule
        {
            RuleId = "limit-rule",
            Name = "Usage Limit",
            Type = PolicyType.ToolUsageLimit,
            Action = PolicyAction.Deny,
            Limit = 1,
            Enabled = true,
            Priority = 10
        };

        var firstResult = await _service.EvaluateAsync("some-action").ConfigureAwait(true);
        firstResult.Allowed.Should().BeTrue();

        var secondResult = await _service.EvaluateAsync("some-action").ConfigureAwait(true);
        secondResult.Allowed.Should().BeFalse();
    }
}
