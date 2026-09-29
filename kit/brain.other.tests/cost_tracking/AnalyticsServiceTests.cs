namespace Core.Tests.CostTracking;

/// <summary>
/// AnalyticsService 单元测试 — 分析服务全部 public 方法
/// 覆盖 TrackEvent/TrackToolCall/TrackToolError/TrackAgentStart/Complete/统计/报告/清除/导出
/// 不传 storagePath/fileOperation,纯内存确定性测试
/// </summary>
public sealed class AnalyticsServiceTests {
    // ---------- TrackEvent ----------

    [Fact]
    public async Task TrackEvent_SingleEvent_RetrievedByHistory() {
        await using var service = new AnalyticsService();

        service.TrackEvent(AnalyticsEventType.ToolCall, "bash", agentName: "agent1");

        var history = service.GetEventHistory();
        Assert.Single(history);
        Assert.Equal("bash", history[0].Name);
        Assert.Equal(AnalyticsEventType.ToolCall, history[0].Type);
        Assert.Equal("agent1", history[0].AgentName);
    }

    [Fact]
    public async Task TrackEvent_MultipleEvents_HistoryReturnsNewestFirst() {
        await using var service = new AnalyticsService();

        service.TrackEvent(AnalyticsEventType.ToolCall, "first");
        service.TrackEvent(AnalyticsEventType.ToolCall, "second");
        service.TrackEvent(AnalyticsEventType.ToolCall, "third");

        var history = service.GetEventHistory();
        Assert.Equal(3, history.Count);
        // GetEventHistory 从末尾遍历,返回最新在前
        Assert.Equal("third", history[0].Name);
        Assert.Equal("second", history[1].Name);
        Assert.Equal("first", history[2].Name);
    }

    [Fact]
    public async Task TrackEvent_WithTypeFilter_OnlyMatchingReturned() {
        await using var service = new AnalyticsService();

        service.TrackEvent(AnalyticsEventType.ToolCall, "call");
        service.TrackEvent(AnalyticsEventType.AgentStart, "start");

        var toolHistory = service.GetEventHistory(AnalyticsEventType.ToolCall);
        Assert.Single(toolHistory);
        Assert.Equal("call", toolHistory[0].Name);
    }

    [Fact]
    public async Task TrackEvent_WithLimit_RespectsLimit() {
        await using var service = new AnalyticsService();

        for (var i = 0; i < 5; i++) {
            service.TrackEvent(AnalyticsEventType.ToolCall, $"evt_{i}");
        }

        var history = service.GetEventHistory(limit: 2);
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task TrackEvent_WithData_StoresData() {
        await using var service = new AnalyticsService();
        var data = new Dictionary<string, JsonElement> {
            ["key"] = JsonSerializer.SerializeToElement("value")
        };

        service.TrackEvent(AnalyticsEventType.ToolCall, "bash", data);

        var history = service.GetEventHistory();
        Assert.Single(history);
        Assert.True(history[0].Data.ContainsKey("key"));
    }

    // ---------- TrackToolCall ----------

    [Fact]
    public async Task TrackToolCall_Success_RecordsToolSuccessEvent() {
        await using var service = new AnalyticsService();

        service.TrackToolCall("bash", success: true, durationMs: 100);

        var history = service.GetEventHistory(AnalyticsEventType.ToolSuccess);
        Assert.Single(history);
        Assert.Equal("bash", history[0].Name);
    }

    [Fact]
    public async Task TrackToolCall_Failure_RecordsToolErrorEvent() {
        await using var service = new AnalyticsService();

        service.TrackToolCall("bash", success: false, durationMs: 50);

        var history = service.GetEventHistory(AnalyticsEventType.ToolError);
        Assert.Single(history);
        Assert.Equal("bash", history[0].Name);
    }

    // ---------- TrackToolError ----------

    [Fact]
    public async Task TrackToolError_RecordsToolErrorEventWithErrorMessage() {
        await using var service = new AnalyticsService();

        service.TrackToolError("grep", "file not found");

        var history = service.GetEventHistory(AnalyticsEventType.ToolError);
        Assert.Single(history);
        Assert.Equal("grep", history[0].Name);
        Assert.True(history[0].Data.ContainsKey("error"));
    }

    // ---------- TrackAgentStart / Complete ----------

    [Fact]
    public async Task TrackAgentStart_RecordsAgentStartEvent() {
        await using var service = new AnalyticsService();

        service.TrackAgentStart("planner", sessionId: "sess1");

        var history = service.GetEventHistory(AnalyticsEventType.AgentStart);
        Assert.Single(history);
        Assert.Equal("planner", history[0].AgentName);
    }

    [Fact]
    public async Task TrackAgentCompleteAsync_RecordsAgentCompleteEvent() {
        await using var service = new AnalyticsService();

        await service.TrackAgentCompleteAsync("planner", success: true, durationMs: 200);

        var history = service.GetEventHistory(AnalyticsEventType.AgentComplete);
        Assert.Single(history);
        Assert.Equal("planner", history[0].AgentName);
    }

    // ---------- GetToolUsageStatistics ----------

    [Fact]
    public async Task GetToolUsageStatistics_NoEvents_ReturnsEmpty() {
        await using var service = new AnalyticsService();

        var stats = service.GetToolUsageStatistics();

        Assert.Empty(stats);
    }

    [Fact]
    public async Task GetToolUsageStatistics_GroupsByToolName() {
        await using var service = new AnalyticsService();

        service.TrackToolCall("bash", true, 100);
        service.TrackToolCall("bash", false, 200);
        service.TrackToolCall("grep", true, 50);

        var stats = service.GetToolUsageStatistics();

        Assert.Equal(2, stats.Count);
        var bashStat = stats.First(s => s.ToolName == "bash");
        Assert.Equal(2, bashStat.CallCount);
        Assert.Equal(1, bashStat.SuccessCount);
        Assert.Equal(1, bashStat.ErrorCount);
    }

    [Fact]
    public async Task GetToolUsageStatistics_OrderedByCallCountDescending() {
        await using var service = new AnalyticsService();

        service.TrackToolCall("grep", true, 10);
        service.TrackToolCall("bash", true, 10);
        service.TrackToolCall("bash", true, 10);

        var stats = service.GetToolUsageStatistics();

        Assert.Equal("bash", stats[0].ToolName); // 2 calls
        Assert.Equal("grep", stats[1].ToolName); // 1 call
    }

    /// <summary>
    /// 复现潜在 bug:TrackToolCall 不设置 AnalyticsEvent.DurationMs(只写入 Data["duration_ms"]),
    /// 而 GetToolUsageStatistics 的 AverageDurationMs = g.Where(e => e.DurationMs.HasValue).Average(...)
    /// 对空集合会抛 InvalidOperationException。应使用 DefaultIfEmpty(0)。
    /// </summary>
    [Fact]
    public async Task GetToolUsageStatistics_AfterTrackToolCall_DoesNotThrow() {
        await using var service = new AnalyticsService();

        service.TrackToolCall("bash", true, 100);

        var ex = Record.Exception(() => service.GetToolUsageStatistics());
        Assert.Null(ex);
    }

    // ---------- GetUsageReport ----------

    [Fact]
    public async Task GetUsageReport_NoEvents_ReturnsZeroTotals() {
        await using var service = new AnalyticsService();

        var report = service.GetUsageReport();

        Assert.Equal(0, report.TotalEvents);
        Assert.Equal(0, report.TotalToolCalls);
        Assert.Equal(0, report.ErrorRate);
    }

    [Fact]
    public async Task GetUsageReport_WithToolCalls_CalculatesTotals() {
        await using var service = new AnalyticsService();

        service.TrackToolCall("bash", true, 100);
        service.TrackToolCall("bash", false, 200);

        var report = service.GetUsageReport();

        Assert.Equal(2, report.TotalEvents);
        Assert.Equal(2, report.TotalToolCalls);
        Assert.True(report.ToolSuccessRate > 0);
    }

    [Fact]
    public async Task GetUsageReport_TopToolsCappedAt10() {
        await using var service = new AnalyticsService();

        for (var i = 0; i < 15; i++) {
            service.TrackToolCall($"tool_{i}", true, 10);
        }

        var report = service.GetUsageReport();
        Assert.True(report.TopTools.Count <= 10);
    }

    // ---------- ClearHistory ----------

    [Fact]
    public async Task ClearHistory_RemovesAllEvents() {
        await using var service = new AnalyticsService();
        service.TrackEvent(AnalyticsEventType.ToolCall, "bash");
        service.TrackEvent(AnalyticsEventType.AgentStart, "agent");

        service.ClearHistory();

        Assert.Empty(service.GetEventHistory());
    }

    [Fact]
    public async Task ClearHistory_CalledTwice_Idempotent() {
        await using var service = new AnalyticsService();
        service.TrackEvent(AnalyticsEventType.ToolCall, "bash");

        service.ClearHistory();
        service.ClearHistory();

        Assert.Empty(service.GetEventHistory());
    }

    // ---------- ExportDataAsync ----------

    [Fact]
    public async Task ExportDataAsync_NoEvents_ReturnsValidJsonWithZeroCount() {
        await using var service = new AnalyticsService();

        var json = await service.ExportDataAsync();

        Assert.Contains("\"eventCount\": 0", json);
    }

    [Fact]
    public async Task ExportDataAsync_WithEvents_ContainsEventNames() {
        await using var service = new AnalyticsService();
        service.TrackEvent(AnalyticsEventType.ToolCall, "myTool");

        var json = await service.ExportDataAsync();

        Assert.Contains("myTool", json);
        Assert.Contains("\"eventCount\": 1", json);
    }

    // ---------- Dispose ----------

    [Fact]
    public void Dispose_AfterDispose_TrackEventIsNoOp() {
        var service = new AnalyticsService();
        service.TrackEvent(AnalyticsEventType.ToolCall, "before");

        service.Dispose();

        // Dispose 后 TrackEvent 应早返回(_disposed != 0),不抛
        var ex = Record.Exception(() => service.TrackEvent(AnalyticsEventType.ToolCall, "after"));
        Assert.Null(ex);
    }

    [Fact]
    public async Task DisposeAsync_AfterDispose_TrackEventIsNoOp() {
        var service = new AnalyticsService();
        service.TrackEvent(AnalyticsEventType.ToolCall, "before");

        await service.DisposeAsync();

        var ex = Record.Exception(() => service.TrackEvent(AnalyticsEventType.ToolCall, "after"));
        Assert.Null(ex);
    }
}
