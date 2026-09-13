

namespace McpToolDispatch;

/// <summary>
/// 计划执行验证工具处理器 — 提供按预期标准验证计划执行结果的功能
/// </summary>
[McpToolDispatch(ToolCategory.Plan, Optional = true)]
public partial class VerifyPlanExecutionToolHandlers
{
    private readonly IPlanService _planService;
    private readonly ILogger<VerifyPlanExecutionToolHandlers>? _logger;

    /// <summary>
    /// 初始化 <see cref="VerifyPlanExecutionToolHandlers"/> 实例
    /// </summary>
    /// <param name="planService">计划服务</param>
    /// <param name="logger">日志记录器（可选）</param>
    public VerifyPlanExecutionToolHandlers(IPlanService planService, ILogger<VerifyPlanExecutionToolHandlers>? logger = null)
    {
        _planService = planService ?? throw new ArgumentNullException(nameof(planService));
        _logger = logger;
    }

    /// <summary>
    /// 按预期标准验证计划执行结果
    /// </summary>
    /// <param name="plan_prompt">计划提示（可选，默认为空）</param>
    /// <param name="criteria">验证标准（可选，描述预期执行结果）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
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

            var builder = result.Success ? ToolResultBuilder.Success() : ToolResultBuilder.Error();
            return builder.WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VerifyPlanFailedLog));
            return ToolResultBuilder.Error().WithText(L.T(StringKey.VerifyPlanFailed, ex.Message)).Build();
        }
    }
}
