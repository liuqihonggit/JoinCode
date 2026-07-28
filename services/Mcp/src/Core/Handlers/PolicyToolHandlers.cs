

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Policy, Optional = true)]
public sealed partial class PolicyToolHandlers
{
    private readonly IRemotePolicyService _policyService;
    [Inject] private readonly ILogger<PolicyToolHandlers>? _logger;

    public PolicyToolHandlers(IRemotePolicyService policyService, ILogger<PolicyToolHandlers>? logger = null)
    {
        _policyService = policyService ?? throw new ArgumentNullException(nameof(policyService));
        _logger = logger;
    }

    [McpTool(InteractionToolNameConstants.PolicyCheck, "Check if an action complies with policy rules", "policy")]
    public async Task<ToolResult> PolicyCheckAsync(
        [McpToolParameter("Action name")] string action,
        [McpToolParameter("Context information (JSON object, optional)", Required = false)] Dictionary<string, string>? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ActionCannotBeEmpty)).Build();
        }

        try
        {
            var result = await _policyService.EvaluateAsync(action, context, cancellationToken).ConfigureAwait(false);

            var response = new StringBuilder(256);
            response.AppendLine(L.T(StringKey.PolicyCheckResult, result.Allowed ? L.T(StringKey.Allowed) : L.T(StringKey.Denied)));
            response.AppendLine(L.T(StringKey.LabelAction, action));
            response.AppendLine(L.T(StringKey.LabelRuleId, result.RuleId));
            response.AppendLine(L.T(StringKey.LabelPolicyAction, result.Action));

            if (!string.IsNullOrEmpty(result.Reason))
            {
                response.AppendLine(L.T(StringKey.LabelReason, result.Reason));
            }

            if (result.RemainingLimit.HasValue)
            {
                response.AppendLine(L.T(StringKey.LabelRemainingLimit, result.RemainingLimit.Value));
            }

            if (result.RetryAfter.HasValue)
            {
                response.AppendLine(L.T(StringKey.LabelRetryAfter, result.RetryAfter.Value.ToString("hh\\:mm\\:ss")));
            }

            return result.Allowed
                ? McpResultBuilder.Success().WithText(response.ToString()).Build()
                : McpResultBuilder.Error().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.PolicyCheckFailedLog), action);
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.PolicyCheckFailed, ex.Message))
                .Build();
        }
    }

    [McpTool(InteractionToolNameConstants.PolicyList, "List all active policy rules", "policy")]
    public async Task<ToolResult> PolicyListAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rules = await _policyService.GetActiveRulesAsync(cancellationToken).ConfigureAwait(false);

            var response = new StringBuilder(512);
            response.AppendLine(L.T(StringKey.ActivePolicyRules, rules.Count));

            if (rules.Count == 0)
            {
                response.AppendLine(L.T(StringKey.NoActivePolicyRules));
            }
            else
            {
                foreach (var rule in rules)
                {
                    var statusIcon = rule.Enabled ? "✓" : "✗";
                    response.AppendLine($"  {statusIcon} [{rule.Priority}] {rule.Name} ({rule.Type}) -> {rule.Action}");

                    if (rule.Limit.HasValue)
                    {
                        response.AppendLine($"     {L.T(StringKey.LabelLimit, rule.Limit.Value)}");
                    }

                    if (rule.CostLimit.HasValue)
                    {
                        response.AppendLine($"     {L.T(StringKey.LabelCostLimit, rule.CostLimit.Value)}");
                    }
                }
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.GetPolicyListFailedLog));
            return McpResultBuilder.Error()
                .WithText(L.T(StringKey.GetPolicyListFailed, ex.Message))
                .Build();
        }
    }
}
