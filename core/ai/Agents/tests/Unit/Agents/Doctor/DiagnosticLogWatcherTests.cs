namespace Core.Tests.Agents.Doctor;


public class DiagnosticLogWatcherTests
{
    [Fact]
    public void ParseLogLine_LoopDetected_ReturnsCorrectEventType()
    {
        var evt = DiagnosticLogWatcher.ParseLogLine("[2026-08-01 12:00:00] [LOOP] Detected loop 3 times", "patient.log");

        Assert.NotNull(evt);
        Assert.Equal("loop_detected", evt.EventType);
        Assert.Equal("patient", evt.PatientId);
        Assert.Equal("log_file", evt.Properties["source"]);
    }

    [Fact]
    public void ParseLogLine_ApiError_ReturnsCorrectEventType()
    {
        var evt = DiagnosticLogWatcher.ParseLogLine("[2026-08-01] [API_ERROR] OpenAI returned 429", "main.log");

        Assert.NotNull(evt);
        Assert.Equal("api_error", evt.EventType);
    }

    [Fact]
    public void ParseLogLine_PermissionDenied_ReturnsCorrectEventType()
    {
        var evt = DiagnosticLogWatcher.ParseLogLine("PermissionDenied: Bash(git:reset) blocked", "test.log");

        Assert.NotNull(evt);
        Assert.Equal("permission_denied", evt.EventType);
    }

    [Fact]
    public void ParseLogLine_ProcessHung_ReturnsCorrectEventType()
    {
        var evt = DiagnosticLogWatcher.ParseLogLine("[HUNG] Process exited with ExitCode=1234", "hung.log");

        Assert.NotNull(evt);
        Assert.Equal("process_hung", evt.EventType);
    }

    [Fact]
    public void ParseLogLine_ToolError_ReturnsCorrectEventType()
    {
        var evt = DiagnosticLogWatcher.ParseLogLine("[TOOL_ERROR] Read tool failed 3 times", "tool.log");

        Assert.NotNull(evt);
        Assert.Equal("tool_error", evt.EventType);
    }

    [Fact]
    public void ParseLogLine_ContextOverflow_ReturnsCorrectEventType()
    {
        var evt = DiagnosticLogWatcher.ParseLogLine("[CTX_OVERFLOW] Token usage 85%", "ctx.log");

        Assert.NotNull(evt);
        Assert.Equal("context_overflow", evt.EventType);
    }

    [Fact]
    public void ParseLogLine_DiagOutput_ReturnsCorrectEventType()
    {
        var evt = DiagnosticLogWatcher.ParseLogLine("[WIRE] Sending request to API", "wire.log");

        Assert.NotNull(evt);
        Assert.Equal("diag_output", evt.EventType);
    }

    [Fact]
    public void ParseLogLine_UnknownLine_ReturnsNull()
    {
        var evt = DiagnosticLogWatcher.ParseLogLine("Just a normal log line", "normal.log");

        Assert.Null(evt);
    }

    [Fact]
    public void ParseLogLine_EmptyLine_ReturnsNull()
    {
        var evt = DiagnosticLogWatcher.ParseLogLine("", "empty.log");

        Assert.Null(evt);
    }

    [Fact]
    public void ParseLogLine_ExtractsTimestamp()
    {
        var evt = DiagnosticLogWatcher.ParseLogLine("[2026-08-01 14:30:00] [LOOP] loop", "ts.log");

        Assert.NotNull(evt);
        Assert.NotEqual(default, evt.Timestamp);
        Assert.Equal(2026, evt.Timestamp.UtcDateTime.Year);
        Assert.Equal(8, evt.Timestamp.UtcDateTime.Month);
        Assert.Equal(1, evt.Timestamp.UtcDateTime.Day);
    }

    [Fact]
    public void ParseLogLine_PreservesRawData()
    {
        var line = "[LOOP] Detected loop 3 times";
        var evt = DiagnosticLogWatcher.ParseLogLine(line, "raw.log");

        Assert.NotNull(evt);
        Assert.Equal(line, evt.RawData);
    }
}
