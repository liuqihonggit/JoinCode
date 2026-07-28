namespace Core.Tests.Agents.Doctor;

public class DiagnosticEngineTests
{
    private readonly DiagnosticEngine _engine = new();

    [Fact]
    public void Evaluate_LoopDetected_LessThan3Times_ReturnsNull()
    {
        var evt1 = CreateEvent("loop_detected", sessionId: "s1");
        var evt2 = CreateEvent("loop_detected", sessionId: "s1");

        Assert.Null(_engine.Evaluate(evt1));
        Assert.Null(_engine.Evaluate(evt2));
    }

    [Fact]
    public void Evaluate_LoopDetected_3Times_ReturnsD001Report()
    {
        var evt1 = CreateEvent("loop_detected", sessionId: "s1");
        var evt2 = CreateEvent("loop_detected", sessionId: "s1");
        var evt3 = CreateEvent("loop_detected", sessionId: "s1");

        _engine.Evaluate(evt1);
        _engine.Evaluate(evt2);
        var report = _engine.Evaluate(evt3);

        Assert.NotNull(report);
        Assert.Equal(DiagnosticRuleId.LoopDetected, report.RuleId);
        Assert.Equal(DiagnosticSeverity.Warning, report.Severity);
        Assert.Equal(HotFixActionType.SourceCodePatch, report.SuggestedFixType);
        Assert.Contains("3 次", report.Description);
    }

    [Fact]
    public void Evaluate_LoopDetected_DifferentSessions_CountedSeparately()
    {
        var evt1 = CreateEvent("loop_detected", sessionId: "s1");
        var evt2 = CreateEvent("loop_detected", sessionId: "s2");
        var evt3 = CreateEvent("loop_detected", sessionId: "s1");

        Assert.Null(_engine.Evaluate(evt1));
        Assert.Null(_engine.Evaluate(evt2));
        Assert.Null(_engine.Evaluate(evt3));
    }

    [Fact]
    public void Evaluate_PermissionDenied_LessThan2Times_ReturnsNull()
    {
        var evt = CreateEvent("permission_denied", properties: new Dictionary<string, string> { ["tool"] = "Bash" });

        Assert.Null(_engine.Evaluate(evt));
    }

    [Fact]
    public void Evaluate_PermissionDenied_2Times_ReturnsD002Report()
    {
        var evt1 = CreateEvent("permission_denied", properties: new Dictionary<string, string> { ["tool"] = "Bash" });
        var evt2 = CreateEvent("permission_denied", properties: new Dictionary<string, string> { ["tool"] = "Bash" });

        _engine.Evaluate(evt1);
        var report = _engine.Evaluate(evt2);

        Assert.NotNull(report);
        Assert.Equal(DiagnosticRuleId.ToolPermissionDenied, report.RuleId);
        Assert.Equal(DiagnosticSeverity.Error, report.Severity);
        Assert.Equal(HotFixActionType.ConfigChange, report.SuggestedFixType);
        Assert.Contains("Bash", report.Description);
    }

    [Fact]
    public void Evaluate_PermissionDenied_DifferentTools_CountedSeparately()
    {
        var evt1 = CreateEvent("permission_denied", properties: new Dictionary<string, string> { ["tool"] = "Bash" });
        var evt2 = CreateEvent("permission_denied", properties: new Dictionary<string, string> { ["tool"] = "Edit" });

        Assert.Null(_engine.Evaluate(evt1));
        Assert.Null(_engine.Evaluate(evt2));
    }

    [Fact]
    public void Evaluate_PermissionDenied_UsesToolNameFallback()
    {
        var evt1 = CreateEvent("permission_denied", properties: new Dictionary<string, string> { ["tool_name"] = "Read" });
        var evt2 = CreateEvent("permission_denied", properties: new Dictionary<string, string> { ["tool_name"] = "Read" });

        _engine.Evaluate(evt1);
        var report = _engine.Evaluate(evt2);

        Assert.NotNull(report);
        Assert.Contains("Read", report.Description);
    }

    [Theory]
    [InlineData("0.5", false)]
    [InlineData("0.79", false)]
    [InlineData("0.80", false)]
    [InlineData("0.81", true)]
    [InlineData("0.95", true)]
    public void Evaluate_ContextOverflow_ThresholdAt80Percent(string ratioStr, bool shouldTrigger)
    {
        var evt = CreateEvent("context_overflow",
            properties: new Dictionary<string, string> { ["token_usage_ratio"] = ratioStr });

        var report = _engine.Evaluate(evt);

        Assert.Equal(shouldTrigger, report is not null);
        if (shouldTrigger)
        {
            Assert.Equal(DiagnosticRuleId.ContextOverflow, report!.RuleId);
            Assert.Equal(HotFixActionType.CompactContext, report.SuggestedFixType);
        }
    }

    [Fact]
    public void Evaluate_ContextOverflow_Above95Percent_IsErrorSeverity()
    {
        var evt = CreateEvent("context_overflow",
            properties: new Dictionary<string, string> { ["token_usage_ratio"] = "0.96" });

        var report = _engine.Evaluate(evt);

        Assert.NotNull(report);
        Assert.Equal(DiagnosticSeverity.Error, report.Severity);
    }

    [Fact]
    public void Evaluate_ContextOverflow_Between80And95_IsWarningSeverity()
    {
        var evt = CreateEvent("context_overflow",
            properties: new Dictionary<string, string> { ["token_usage_ratio"] = "0.85" });

        var report = _engine.Evaluate(evt);

        Assert.NotNull(report);
        Assert.Equal(DiagnosticSeverity.Warning, report.Severity);
    }

    [Fact]
    public void Evaluate_ApiError_LessThan3Consecutive_ReturnsNull()
    {
        var evt1 = CreateEvent("api_error");
        var evt2 = CreateEvent("api_error");

        Assert.Null(_engine.Evaluate(evt1));
        Assert.Null(_engine.Evaluate(evt2));
    }

    [Fact]
    public void Evaluate_ApiError_3Consecutive_ReturnsD005Report()
    {
        var evt1 = CreateEvent("api_error");
        var evt2 = CreateEvent("api_error");
        var evt3 = CreateEvent("api_error");

        _engine.Evaluate(evt1);
        _engine.Evaluate(evt2);
        var report = _engine.Evaluate(evt3);

        Assert.NotNull(report);
        Assert.Equal(DiagnosticRuleId.ApiError, report.RuleId);
        Assert.Equal(DiagnosticSeverity.Error, report.Severity);
        Assert.Equal(HotFixActionType.ConfigChange, report.SuggestedFixType);
        Assert.Contains("3 次", report.Description);
    }

    [Fact]
    public void Evaluate_ApiTimeout_3Consecutive_ReturnsD005Report()
    {
        var evt1 = CreateEvent("api_timeout");
        var evt2 = CreateEvent("api_timeout");
        var evt3 = CreateEvent("api_timeout");

        _engine.Evaluate(evt1);
        _engine.Evaluate(evt2);
        var report = _engine.Evaluate(evt3);

        Assert.NotNull(report);
        Assert.Equal(DiagnosticRuleId.ApiError, report.RuleId);
    }

    [Fact]
    public void Evaluate_ApiError_MixedWithOtherEvents_NoReport()
    {
        var evt1 = CreateEvent("api_error");
        var evt2 = CreateEvent("step_trace");
        var evt3 = CreateEvent("api_error");

        _engine.Evaluate(evt1);
        _engine.Evaluate(evt2);
        var report = _engine.Evaluate(evt3);

        Assert.Null(report);
    }

    [Fact]
    public void EvaluateProcessHung_HungState_ReturnsD004Report()
    {
        var info = new PatientInfo
        {
            ProcessId = 1234,
            State = PatientState.Hung,
            ExitCode = 1234,
            StartedAt = DateTimeOffset.UtcNow
        };

        var report = _engine.EvaluateProcessHung(info);

        Assert.NotNull(report);
        Assert.Equal(DiagnosticRuleId.ProcessHung, report.RuleId);
        Assert.Equal(DiagnosticSeverity.Critical, report.Severity);
        Assert.Equal(HotFixActionType.RestartProcess, report.SuggestedFixType);
        Assert.Contains("1234", report.Description);
    }

    [Fact]
    public void EvaluateProcessHung_CompletedState_ReturnsNull()
    {
        var info = new PatientInfo
        {
            ProcessId = 1234,
            State = PatientState.Completed,
            ExitCode = 0,
            StartedAt = DateTimeOffset.UtcNow
        };

        Assert.Null(_engine.EvaluateProcessHung(info));
    }

    [Fact]
    public void Evaluate_UnknownEventType_ReturnsNull()
    {
        var evt = CreateEvent("unknown_event_type");
        Assert.Null(_engine.Evaluate(evt));
    }

    [Fact]
    public void Evaluate_NullEvent_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _engine.Evaluate(null!));
    }

    [Fact]
    public void Reports_AccumulatesAllReports()
    {
        var evt1 = CreateEvent("loop_detected", sessionId: "s1");
        var evt2 = CreateEvent("loop_detected", sessionId: "s1");
        var evt3 = CreateEvent("loop_detected", sessionId: "s1");
        var evt4 = CreateEvent("permission_denied", properties: new Dictionary<string, string> { ["tool"] = "Bash" });
        var evt5 = CreateEvent("permission_denied", properties: new Dictionary<string, string> { ["tool"] = "Bash" });

        _engine.Evaluate(evt1);
        _engine.Evaluate(evt2);
        _engine.Evaluate(evt3);
        _engine.Evaluate(evt4);
        _engine.Evaluate(evt5);

        Assert.Equal(2, _engine.Reports.Count);
    }

    [Fact]
    public void Reset_ClearsAllState()
    {
        var evt1 = CreateEvent("loop_detected", sessionId: "s1");
        var evt2 = CreateEvent("loop_detected", sessionId: "s1");
        var evt3 = CreateEvent("loop_detected", sessionId: "s1");

        _engine.Evaluate(evt1);
        _engine.Evaluate(evt2);
        _engine.Evaluate(evt3);
        Assert.Single(_engine.Reports);

        _engine.Reset();
        Assert.Empty(_engine.Reports);

        var evt4 = CreateEvent("loop_detected", sessionId: "s1");
        Assert.Null(_engine.Evaluate(evt4));
    }

    [Fact]
    public void DiagnosticReportGenerated_EventFired()
    {
        DiagnosticReport? captured = null;
        _engine.DiagnosticReportGenerated += (_, r) => captured = r;

        var evt1 = CreateEvent("loop_detected", sessionId: "s1");
        var evt2 = CreateEvent("loop_detected", sessionId: "s1");
        var evt3 = CreateEvent("loop_detected", sessionId: "s1");

        _engine.Evaluate(evt1);
        _engine.Evaluate(evt2);
        _engine.Evaluate(evt3);

        Assert.NotNull(captured);
        Assert.Equal(DiagnosticRuleId.LoopDetected, captured.RuleId);
    }

    [Fact]
    public void Evaluate_ContextOverflow_UsesUsageRatioFallback()
    {
        var evt = CreateEvent("context_overflow",
            properties: new Dictionary<string, string> { ["usage_ratio"] = "0.85" });

        var report = _engine.Evaluate(evt);

        Assert.NotNull(report);
        Assert.Equal(DiagnosticRuleId.ContextOverflow, report.RuleId);
    }

    [Fact]
    public void Evaluate_ToolError_LessThan3Times_ReturnsNull()
    {
        var evt1 = CreateEvent("tool_error", properties: new Dictionary<string, string> { ["tool"] = "Bash" });
        var evt2 = CreateEvent("tool_error", properties: new Dictionary<string, string> { ["tool"] = "Bash" });

        Assert.Null(_engine.Evaluate(evt1));
        Assert.Null(_engine.Evaluate(evt2));
    }

    [Fact]
    public void Evaluate_ToolError_3Times_ReturnsD006Report()
    {
        var evt1 = CreateEvent("tool_error", properties: new Dictionary<string, string> { ["tool"] = "Bash" });
        var evt2 = CreateEvent("tool_error", properties: new Dictionary<string, string> { ["tool"] = "Bash" });
        var evt3 = CreateEvent("tool_error", properties: new Dictionary<string, string> { ["tool"] = "Bash" });

        _engine.Evaluate(evt1);
        _engine.Evaluate(evt2);
        var report = _engine.Evaluate(evt3);

        Assert.NotNull(report);
        Assert.Equal(DiagnosticRuleId.ToolExecutionError, report.RuleId);
        Assert.Equal(DiagnosticSeverity.Error, report.Severity);
        Assert.Equal(HotFixActionType.ConfigChange, report.SuggestedFixType);
        Assert.Contains("Bash", report.Description);
    }

    [Fact]
    public void Evaluate_ToolError_DifferentTools_CountedSeparately()
    {
        var evt1 = CreateEvent("tool_error", properties: new Dictionary<string, string> { ["tool"] = "Bash" });
        var evt2 = CreateEvent("tool_error", properties: new Dictionary<string, string> { ["tool"] = "Read" });
        var evt3 = CreateEvent("tool_error", properties: new Dictionary<string, string> { ["tool"] = "Bash" });

        Assert.Null(_engine.Evaluate(evt1));
        Assert.Null(_engine.Evaluate(evt2));
        Assert.Null(_engine.Evaluate(evt3));
    }

    [Fact]
    public void Evaluate_ToolError_PatientIsolation()
    {
        var evt1 = CreateEvent("tool_error", patientId: "p1", properties: new Dictionary<string, string> { ["tool"] = "Bash" });
        var evt2 = CreateEvent("tool_error", patientId: "p2", properties: new Dictionary<string, string> { ["tool"] = "Bash" });
        var evt3 = CreateEvent("tool_error", patientId: "p1", properties: new Dictionary<string, string> { ["tool"] = "Bash" });

        Assert.Null(_engine.Evaluate(evt1));
        Assert.Null(_engine.Evaluate(evt2));
        Assert.Null(_engine.Evaluate(evt3));
    }

    [Fact]
    public void ClassifyDiagOutput_ToolErrorKeywords()
    {
        Assert.Equal("tool_error", DiagnosticEngine.ClassifyDiagOutput("ToolError: something failed"));
        Assert.Equal("tool_error", DiagnosticEngine.ClassifyDiagOutput("Error executing tool 'Bash'"));
        Assert.Equal("tool_error", DiagnosticEngine.ClassifyDiagOutput("工具执行失败"));
    }

    private static DiagnosticEvent CreateEvent(
        string eventType,
        string? sessionId = null,
        string? patientId = null,
        Dictionary<string, string>? properties = null)
    {
        return new DiagnosticEvent
        {
            EventType = eventType,
            SessionId = sessionId,
            PatientId = patientId ?? string.Empty,
            Properties = properties ?? new Dictionary<string, string>()
        };
    }
}
