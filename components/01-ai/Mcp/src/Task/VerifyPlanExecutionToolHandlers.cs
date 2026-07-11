

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Plan, Optional = true)]
public partial class VerifyPlanExecutionToolHandlers
{
    private readonly IPlanService _planService;
    [Inject] private readonly ILogger<VerifyPlanExecutionToolHandlers>? _logger;

    public VerifyPlanExecutionToolHandlers(IPlanService planService, ILogger<VerifyPlanExecutionToolHandlers>? logger = null)
    {
        _planService = planService ?? throw new ArgumentNullException(nameof(planService));
        _logger = logger;
    }

    [McpTool(SystemToolNameConstants.VerifyPlanExecution, "Verify plan execution results against expectations", "planning")]
    public async Task<ToolResult> VerifyPlanExecutionAsync(
        [McpToolParameter("Plan prompt (optional, default empty)", Required = false)] string? plan_prompt = null,
        [McpToolParameter("Verification criteria (optional, describes expected execution results)", Required = false)] string? criteria = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = string.IsNullOrEmpty(plan_prompt) ? L.T(StringKey.VerifyPlanDefaultPrompt) : plan_prompt;
            var result = await _planService.ExecutePlanWithResultAsync(prompt, cancellationToken).ConfigureAwait(false);

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.VerifyPlanTitle));
            response.AppendLine();

            if (!string.IsNullOrEmpty(criteria))
            {
                response.AppendLine(L.T(StringKey.VerifyPlanCriteria, criteria));
                response.AppendLine();
            }

            if (result.Success)
            {
                response.AppendLine(L.T(StringKey.VerifyPlanSuccess));
                if (!string.IsNullOrEmpty(result.Result))
                    response.AppendLine(result.Result);
            }
            else
            {
                response.AppendLine(L.T(StringKey.VerifyPlanHasIssues));
                if (!string.IsNullOrEmpty(result.Error))
                    response.AppendLine(L.T(StringKey.VerifyPlanError, result.Error));
            }

            var builder = result.Success ? McpResultBuilder.Success() : McpResultBuilder.Error();
            return builder.WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VerifyPlanFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.VerifyPlanFailed, ex.Message)).Build();
        }
    }
}
