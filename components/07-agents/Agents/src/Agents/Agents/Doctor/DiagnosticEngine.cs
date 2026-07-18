namespace Core.Agents.Doctor;

/// <summary>
/// 诊断引擎 — 从遥测事件流中检测问题模式
/// 支持 1:N 多病人：所有内部计数器按 PatientId 隔离
/// </summary>
public sealed class DiagnosticEngine
{
    private readonly Dictionary<(string PatientId, string SessionId), int> _loopCountBySession = new();
    private readonly Dictionary<(string PatientId, string ToolName), int> _permissionDeniedByTool = new();
    private readonly Dictionary<string, List<DiagnosticEvent>> _recentApiErrorsByPatient = new();
    private readonly Dictionary<(string PatientId, string ToolName), int> _toolErrorsByPatientAndTool = new();
    private readonly Dictionary<string, double> _lastTokenUsageRatioByPatient = new();
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

    public DiagnosticEngine() { }

    public DiagnosticReport? Evaluate(DiagnosticEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var effectiveEventType = evt.EventType;
        if (effectiveEventType == "diag_output")
        {
            effectiveEventType = ClassifyDiagOutput(evt.RawData);
            if (effectiveEventType is null) return null;
        }

        var report = effectiveEventType switch
        {
            "loop_detected" => EvaluateLoopDetected(evt),
            "permission_denied" => EvaluatePermissionDenied(evt),
            "context_overflow" => EvaluateContextOverflow(evt),
            "api_error" or "api_timeout" => EvaluateApiError(evt),
            "tool_error" => EvaluateToolError(evt),
            _ => null
        };

        if (report is not null)
        {
            lock (_lock) _reports.Add(report);
            DoctorDiag.WriteError($"[Doctor] 诊断报告生成: {report.RuleId} - {report.Description} (病人: {report.PatientId})");
            DiagnosticReportGenerated?.Invoke(this, report);
        }

        return report;
    }

    /// <summary>
    /// 从 diag_output 的原始文本中分类出实际事件类型
    /// 匹配 jcc 诊断日志前缀 [WIRE]/[STEP]/[MAIN] 中的关键字
    /// </summary>
    internal static string? ClassifyDiagOutput(string? rawData)
    {
        if (string.IsNullOrWhiteSpace(rawData)) return null;

        if (rawData.Contains("LoopDetected", StringComparison.OrdinalIgnoreCase)
            || rawData.Contains("循环检测", StringComparison.OrdinalIgnoreCase))
            return "loop_detected";

        if (rawData.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase)
            || rawData.Contains("权限被拒绝", StringComparison.OrdinalIgnoreCase))
            return "permission_denied";

        if (rawData.Contains("ContextOverflow", StringComparison.OrdinalIgnoreCase)
            || rawData.Contains("上下文溢出", StringComparison.OrdinalIgnoreCase)
            || rawData.Contains("token_usage_ratio", StringComparison.OrdinalIgnoreCase))
            return "context_overflow";

        if (rawData.Contains("ApiError", StringComparison.OrdinalIgnoreCase)
            || rawData.Contains("api_timeout", StringComparison.OrdinalIgnoreCase)
            || rawData.Contains("API错误", StringComparison.OrdinalIgnoreCase))
            return "api_error";

<<<<<<< HEAD
        if (rawData.Contains("ToolError", StringComparison.OrdinalIgnoreCase)
            || rawData.Contains("tool_error", StringComparison.OrdinalIgnoreCase)
            || rawData.Contains("工具执行失败", StringComparison.OrdinalIgnoreCase)
            || rawData.Contains("Error executing tool", StringComparison.OrdinalIgnoreCase))
            return "tool_error";

        return null;
    }

    public DiagnosticReport? EvaluateProcessHung(PatientInfo patientInfo)
    {
        ArgumentNullException.ThrowIfNull(patientInfo);

        if (patientInfo.State != PatientState.Hung) return null;

        var report = new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ProcessHung,
            PatientId = patientInfo.PatientId,
            Severity = DiagnosticSeverity.Critical,
            Description = $"病人进程卡死（退出码 1234），PID={patientInfo.ProcessId}",
            SuggestedFixType = HotFixActionType.RestartProcess,
            SuggestedFixDescription = "重启病人进程，检查是否存在死锁或无限循环"
        };

        lock (_lock) _reports.Add(report);
        DoctorDiag.WriteError($"[Doctor] 诊断报告生成: {report.RuleId} - {report.Description} (病人: {report.PatientId})");
        DiagnosticReportGenerated?.Invoke(this, report);
        return report;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _loopCountBySession.Clear();
            _permissionDeniedByTool.Clear();
            _recentApiErrorsByPatient.Clear();
            _toolErrorsByPatientAndTool.Clear();
            _lastTokenUsageRatioByPatient.Clear();
            _reports.Clear();
        }
    }

    public void ResetPatient(string patientId)
    {
        lock (_lock)
        {
            var sessionKeysToRemove = _loopCountBySession.Keys.Where(k => k.PatientId == patientId).ToList();
            foreach (var key in sessionKeysToRemove) _loopCountBySession.Remove(key);

            var toolKeysToRemove = _permissionDeniedByTool.Keys.Where(k => k.PatientId == patientId).ToList();
            foreach (var key in toolKeysToRemove) _permissionDeniedByTool.Remove(key);

            var toolErrorKeysToRemove = _toolErrorsByPatientAndTool.Keys.Where(k => k.PatientId == patientId).ToList();
            foreach (var key in toolErrorKeysToRemove) _toolErrorsByPatientAndTool.Remove(key);

            _recentApiErrorsByPatient.Remove(patientId);
            _lastTokenUsageRatioByPatient.Remove(patientId);
        }
    }

    private DiagnosticReport? EvaluateLoopDetected(DiagnosticEvent evt)
    {
        var sessionId = evt.SessionId ?? "default";
        var key = (evt.PatientId, sessionId);
        var count = _loopCountBySession.GetValueOrDefault(key) + 1;
        _loopCountBySession[key] = count;

        if (count < 3) return null;

        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.LoopDetected,
            PatientId = evt.PatientId,
            Severity = DiagnosticSeverity.Warning,
            Description = $"病人 {evt.PatientId} 会话 {sessionId} 检测到循环 {count} 次，可能存在工具调用死循环",
            TriggeringEvents = [evt],
            SuggestedFixType = HotFixActionType.SourceCodePatch,
            SuggestedFixDescription = "检查 LoopInterventionMiddleware 阈值配置或工具调用逻辑"
        };
    }

    private DiagnosticReport? EvaluatePermissionDenied(DiagnosticEvent evt)
    {
        var toolName = evt.Properties.GetValueOrDefault("tool") ?? evt.Properties.GetValueOrDefault("tool_name") ?? "unknown";
        var key = (evt.PatientId, toolName);
        var count = _permissionDeniedByTool.GetValueOrDefault(key) + 1;
        _permissionDeniedByTool[key] = count;

        if (count < 2) return null;

        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ToolPermissionDenied,
            PatientId = evt.PatientId,
            Severity = DiagnosticSeverity.Error,
            Description = $"病人 {evt.PatientId} 工具 {toolName} 权限被拒绝 {count} 次，可能需要更新权限配置",
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
        _lastTokenUsageRatioByPatient[evt.PatientId] = ratio;

        if (ratio <= 0.8) return null;

        var severity = ratio > 0.95 ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;

        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ContextOverflow,
            PatientId = evt.PatientId,
            Severity = severity,
            Description = $"病人 {evt.PatientId} 上下文 token 使用率达 {ratio:P1}，接近或超出窗口限制",
            TriggeringEvents = [evt],
            SuggestedFixType = HotFixActionType.CompactContext,
            SuggestedFixDescription = "执行 /compact 压缩上下文，或减少对话历史长度"
        };
    }

    private DiagnosticReport? EvaluateApiError(DiagnosticEvent evt)
    {
        if (!_recentApiErrorsByPatient.TryGetValue(evt.PatientId, out var errors))
        {
            errors = [];
            _recentApiErrorsByPatient[evt.PatientId] = errors;
        }

        errors.Add(evt);

        if (errors.Count > 10)
            errors.RemoveAt(0);

        if (errors.Count < 3) return null;

        var recentWindow = errors.TakeLast(3).ToList();
        var allRecent = recentWindow.All(e =>
            e.EventType is "api_error" or "api_timeout");

        if (!allRecent) return null;

        var errorTypes = string.Join(", ", recentWindow.Select(e => e.EventType).Distinct());

        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ApiError,
            PatientId = evt.PatientId,
            Severity = DiagnosticSeverity.Error,
            Description = $"病人 {evt.PatientId} API 连续失败 3 次（{errorTypes}），可能存在网络或服务端问题",
            TriggeringEvents = recentWindow,
            SuggestedFixType = HotFixActionType.ConfigChange,
            SuggestedFixDescription = "检查 API 端点配置、网络连接、API Key 有效性"
        };
    }

    private DiagnosticReport? EvaluateToolError(DiagnosticEvent evt)
    {
        var toolName = evt.Properties.GetValueOrDefault("tool") ?? evt.Properties.GetValueOrDefault("tool_name") ?? "unknown";
        var key = (evt.PatientId, toolName);
        var count = _toolErrorsByPatientAndTool.GetValueOrDefault(key) + 1;
        _toolErrorsByPatientAndTool[key] = count;

        if (count < 3) return null;

        var errorMessage = evt.Properties.GetValueOrDefault("error") ?? evt.RawData;

        return new DiagnosticReport
        {
            RuleId = DiagnosticRuleId.ToolExecutionError,
            PatientId = evt.PatientId,
            Severity = DiagnosticSeverity.Error,
            Description = $"病人 {evt.PatientId} 工具 {toolName} 连续执行失败 {count} 次，可能存在配置或环境问题",
            TriggeringEvents = [evt],
            SuggestedFixType = HotFixActionType.ConfigChange,
            SuggestedFixDescription = string.IsNullOrWhiteSpace(errorMessage)
                ? $"检查工具 {toolName} 的配置和依赖"
                : $"检查工具 {toolName} 的配置和依赖，错误: {errorMessage}"
        };
    }
}
