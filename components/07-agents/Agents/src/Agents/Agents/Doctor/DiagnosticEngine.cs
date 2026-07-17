namespace Core.Agents.Doctor;

public sealed class DiagnosticEngine
{
    private readonly ILogger? _logger;
    private readonly Dictionary<string, int> _loopCountBySession = new();
    private readonly Dictionary<string, int> _permissionDeniedByTool = new();
    private readonly List<DiagnosticEvent> _recentApiErrors = new();
    private double _lastTokenUsageRatio;
    private readonly List<DiagnosticReport> _reports = [];
    private readonly object _lock = new();

    public IReadOnlyList<DiagnosticReport> Reports
    {
        get
        {
            lock (_lock) return _reports.ToList();
        }
    }

    public event EventHandler<DiagnosticReport>? DiagnosticReportGenerated;

    public DiagnosticEngine(ILogger? logger = null)
    {
        _logger = logger;
    }

    public DiagnosticReport? Evaluate(DiagnosticEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var report = evt.EventType switch
        {
            "loop_detected" => EvaluateLoopDetected(evt),
            "permission_denied" => EvaluatePermissionDenied(evt),
            "context_overflow" => EvaluateContextOverflow(evt),
            "api_error" or "api_timeout" => EvaluateApiError(evt),
            _ => null
        };

        if (report is not null)
        {
            lock (_lock) _reports.Add(report);
            _logger?.LogWarning("[Doctor] 诊断报告生成: {RuleId} - {Description}", report.RuleId, report.Description);
            DiagnosticReportGenerated?.Invoke(this, report);
        }

        return report;
    }

    public DiagnosticReport? EvaluateProcessHung(PatientInfo patientInfo)
    {
        ArgumentNullException.ThrowIfNull(patientInfo);

        if (patientInfo.State != PatientState.Hung) return null;

        var report = new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ProcessHung,
            Severity = DiagnosticSeverity.Critical,
            Description = $"病人进程卡死（退出码 1234），PID={patientInfo.ProcessId}",
            SuggestedFixType = HotFixActionType.RestartProcess,
            SuggestedFixDescription = "重启病人进程，检查是否存在死锁或无限循环"
        };

        lock (_lock) _reports.Add(report);
        _logger?.LogWarning("[Doctor] 诊断报告生成: {RuleId} - {Description}", report.RuleId, report.Description);
        DiagnosticReportGenerated?.Invoke(this, report);
        return report;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _loopCountBySession.Clear();
            _permissionDeniedByTool.Clear();
            _recentApiErrors.Clear();
            _lastTokenUsageRatio = 0;
            _reports.Clear();
        }
    }

    private DiagnosticReport? EvaluateLoopDetected(DiagnosticEvent evt)
    {
        var sessionId = evt.SessionId ?? "default";
        var count = _loopCountBySession.GetValueOrDefault(sessionId) + 1;
        _loopCountBySession[sessionId] = count;

        if (count < 3) return null;

        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.LoopDetected,
            Severity = DiagnosticSeverity.Warning,
            Description = $"会话 {sessionId} 检测到循环 {count} 次，可能存在工具调用死循环",
            TriggeringEvents = [evt],
            SuggestedFixType = HotFixActionType.SourceCodePatch,
            SuggestedFixDescription = "检查 LoopInterventionMiddleware 阈值配置或工具调用逻辑"
        };
    }

    private DiagnosticReport? EvaluatePermissionDenied(DiagnosticEvent evt)
    {
        var toolName = evt.Properties.GetValueOrDefault("tool") ?? evt.Properties.GetValueOrDefault("tool_name") ?? "unknown";
        var count = _permissionDeniedByTool.GetValueOrDefault(toolName) + 1;
        _permissionDeniedByTool[toolName] = count;

        if (count < 2) return null;

        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ToolPermissionDenied,
            Severity = DiagnosticSeverity.Error,
            Description = $"工具 {toolName} 权限被拒绝 {count} 次，可能需要更新权限配置",
            TriggeringEvents = [evt],
            SuggestedFixType = HotFixActionType.ConfigChange,
            SuggestedFixDescription = $"在 settings.json 中为工具 {toolName} 添加允许权限"
        };
    }

    private DiagnosticReport? EvaluateContextOverflow(DiagnosticEvent evt)
    {
        var usageStr = evt.Properties.GetValueOrDefault("token_usage_ratio")
            ?? evt.Properties.GetValueOrDefault("usage_ratio")
            ?? "0";

        if (!double.TryParse(usageStr, out var ratio)) ratio = 0;
        _lastTokenUsageRatio = ratio;

        if (ratio <= 0.8) return null;

        var severity = ratio > 0.95 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;

        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ContextOverflow,
            Severity = severity,
            Description = $"上下文 token 使用率达 {ratio:P1}，接近或超出窗口限制",
            TriggeringEvents = [evt],
            SuggestedFixType = HotFixActionType.CompactContext,
            SuggestedFixDescription = "执行 /compact 压缩上下文，或减少对话历史长度"
        };
    }

    private DiagnosticReport? EvaluateApiError(DiagnosticEvent evt)
    {
        _recentApiErrors.Add(evt);

        if (_recentApiErrors.Count > 10)
            _recentApiErrors.RemoveAt(0);

        if (_recentApiErrors.Count < 3) return null;

        var recentWindow = _recentApiErrors.TakeLast(3).ToList();
        var allRecent = recentWindow.All(e =>
            e.EventType is "api_error" or "api_timeout");

        if (!allRecent) return null;

        var errorTypes = string.Join(", ", recentWindow.Select(e => e.EventType).Distinct());

        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ApiError,
            Severity = DiagnosticSeverity.Error,
            Description = $"API 连续失败 3 次（{errorTypes}），可能存在网络或服务端问题",
            TriggeringEvents = recentWindow,
            SuggestedFixType = HotFixActionType.ConfigChange,
            SuggestedFixDescription = "检查 API 端点配置、网络连接、API Key 有效性"
        };
    }
}
