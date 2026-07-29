
namespace Core.Security.Services;

/// <summary>
/// 子智能体交接安全分类器 — 对齐 TS classifyHandoffIfNeeded
/// 基于 AutoModeClassifier 的规则引擎，审查子智能体整体执行过程
/// </summary>
[Register]
public sealed partial class HandoffClassifier : IHandoffClassifier
{
    private readonly IAutoModeClassifier _autoModeClassifier;
    [Inject] private readonly ILogger<HandoffClassifier>? _logger;

    public HandoffClassifier(IAutoModeClassifier autoModeClassifier, ILogger<HandoffClassifier>? logger = null)
    {
        _autoModeClassifier = autoModeClassifier;
        _logger = logger;
    }

    public async Task<HandoffClassificationResult?> ClassifyAsync(HandoffClassificationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 对齐 TS: 仅 auto 模式下需要审查
        if (request.PermissionMode != PermissionMode.Auto)
        {
            return null;
        }

        // 无工具调用 — 安全放行
        if (request.ToolInvocations.Count == 0)
        {
            return null;
        }

        // 审查每个工具调用
        var dangerousInvocations = new List<(AgentToolInvocation Invocation, ClassificationResult Result)>();

        foreach (var invocation in request.ToolInvocations)
        {
            // 跳过已手动确认的工具（非自动批准）
            if (!invocation.WasAutoApproved) continue;

            var classRequest = new ClassificationRequest
            {
                ToolName = invocation.ToolName,
                Parameters = invocation.Parameters ?? new Dictionary<string, JsonElement>(),
                OperationType = invocation.OperationType
            };

            var result = await _autoModeClassifier.ClassifyAsync(classRequest, ct).ConfigureAwait(false);

            // 检测高风险或危险操作
            if (result.Classification is SecurityClassification.HighRisk or SecurityClassification.Dangerous)
            {
                dangerousInvocations.Add((invocation, result));
            }
        }

        // 无危险操作 — 安全放行
        if (dangerousInvocations.Count == 0)
        {
            _logger?.LogDebug("Handoff classification for agent {AgentId}: allowed (no dangerous operations)", request.AgentId);
            return null;
        }

        // 构建警告消息 — 对齐 TS 的警告格式
        var reasons = dangerousInvocations
            .Select(d => $"{d.Invocation.ToolName}: {d.Result.Reason}")
            .Distinct()
            .ToList();

        var reasonStr = string.Join("; ", reasons);

        _logger?.LogWarning("Handoff classification for agent {AgentId}: blocked ({Reason})", request.AgentId, reasonStr);

        var warning = $"SECURITY WARNING: This sub-agent performed actions that may violate security policy. Reason: {reasonStr}. Review the sub-agent's actions carefully before acting on its output.";

        return new HandoffClassificationResult
        {
            Classification = HandoffClassification.Blocked,
            WarningMessage = warning,
            Reason = reasonStr
        };
    }
}
