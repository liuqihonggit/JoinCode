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

    // ---------- ExportDataAsync (日期过滤分支,328行) ----------

    [Fact]
    public async Task ExportDataAsync_WithStartDate_FiltersOutEarlierEvents() {
        var fixedTime = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var clock = new Infrastructure.Time.FakeClockService(fixedTime);
        await using var service = new AnalyticsService(clock: clock);

        service.TrackEvent(AnalyticsEventType.ToolCall, "evt_old");
        clock.Advance(TimeSpan.FromHours(2));
        service.TrackEvent(AnalyticsEventType.ToolCall, "evt_new");

        var json = await service.ExportDataAsync(startDate: fixedTime.AddHours(1));

        Assert.Contains("evt_new", json);
        Assert.DoesNotContain("evt_old", json);
        Assert.Contains("\"eventCount\": 1", json);
    }

    [Fact]
    public async Task ExportDataAsync_WithEndDate_FiltersOutLaterEvents() {
        var fixedTime = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var clock = new Infrastructure.Time.FakeClockService(fixedTime);
        await using var service = new AnalyticsService(clock: clock);

        service.TrackEvent(AnalyticsEventType.ToolCall, "evt_old");
        clock.Advance(TimeSpan.FromHours(2));
        service.TrackEvent(AnalyticsEventType.ToolCall, "evt_new");

        var json = await service.ExportDataAsync(endDate: fixedTime.AddHours(1));

        Assert.Contains("evt_old", json);
        Assert.DoesNotContain("evt_new", json);
        Assert.Contains("\"eventCount\": 1", json);
    }

    [Fact]
    public async Task ExportDataAsync_WithDateRange_ReturnsOnlyMatching() {
        var fixedTime = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var clock = new Infrastructure.Time.FakeClockService(fixedTime);
        await using var service = new AnalyticsService(clock: clock);

        service.TrackEvent(AnalyticsEventType.ToolCall, "evt_a"); // t0
        clock.Advance(TimeSpan.FromHours(1));
        service.TrackEvent(AnalyticsEventType.ToolCall, "evt_b"); // t1
        clock.Advance(TimeSpan.FromHours(1));
        service.TrackEvent(AnalyticsEventType.ToolCall, "evt_c"); // t2

        var json = await service.ExportDataAsync(startDate: fixedTime.AddMinutes(30), endDate: fixedTime.AddHours(1).AddMinutes(30));

        Assert.Contains("evt_b", json);
        Assert.DoesNotContain("evt_a", json);
        Assert.DoesNotContain("evt_c", json);
        Assert.Contains("\"eventCount\": 1", json);
    }

    [Fact]
    public async Task ExportDataAsync_WithFakeClock_ExportTimeIsDeterministic() {
        var fixedTime = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var clock = new Infrastructure.Time.FakeClockService(fixedTime);
        await using var service = new AnalyticsService(clock: clock);

        var json = await service.ExportDataAsync();

        // ExportTime 应为 fixedTime(序列化含 2026-09-30)
        Assert.Contains("2026-09-30", json);
    }

    // ---------- SaveHistoryAsync (mock IFileOperationService,452行) ----------

    [Fact]
    public async Task TrackEvent_WithStoragePath_TriggersSaveHistoryOnDispose() {
        var mockFileOp = new Mock<IFileOperationService>();
        var writtenContent = string.Empty;
        mockFileOp.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(FileReadResult.FailureResult("path", "not found")));
        mockFileOp.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, content, _) => writtenContent = content)
            .Returns(Task.FromResult(FileWriteResult.SuccessResult("path", "content", "write")));
        var storagePath = "/tmp/analytics_test.json";

        var service = new AnalyticsService(fileOperationService: mockFileOp.Object, storagePath: storagePath);
        service.TrackEvent(AnalyticsEventType.ToolCall, "persisted_evt");

        await service.DisposeAsync(); // flush 后台任务

        mockFileOp.Verify(f => f.WriteFileAsync(storagePath, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        Assert.Contains("persisted_evt", writtenContent);
    }

    [Fact]
    public async Task SaveHistory_WriteFailure_LogsErrorDoesNotThrow() {
        var mockFileOp = new Mock<IFileOperationService>();
        mockFileOp.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(FileReadResult.FailureResult("path", "not found")));
        mockFileOp.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(FileWriteResult.FailureResult("path", "disk full")));

        var service = new AnalyticsService(fileOperationService: mockFileOp.Object, storagePath: "/tmp/analytics_fail.json");
        service.TrackEvent(AnalyticsEventType.ToolCall, "evt");

        var ex = await Record.ExceptionAsync(() => service.DisposeAsync().AsTask());
        Assert.Null(ex); // 写入失败不抛,仅日志
    }

    // ---------- LoadHistoryAsync (mock IFileOperationService,468行) ----------

    [Fact]
    public async Task Constructor_WithStoragePath_LoadsHistoryFromFile() {
        var presetEvents = new List<JoinCode.Abstractions.Models.Analytics.AnalyticsEvent> {
            new() { EventId = "e1", Type = AnalyticsEventType.ToolCall, Name = "loaded_evt1", Timestamp = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) },
            new() { EventId = "e2", Type = AnalyticsEventType.AgentStart, Name = "loaded_evt2", Timestamp = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc) }
        };
        var json = JsonSerializer.Serialize(presetEvents, CostTrackingIndentedJsonContext.Default.ListAnalyticsEvent);

        var mockFileOp = new Mock<IFileOperationService>();
        mockFileOp.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(FileReadResult.SuccessResult("/tmp/analytics.json", json, 2, 1, 2)));
        mockFileOp.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(FileWriteResult.SuccessResult("path", "content", "write")));

        var service = new AnalyticsService(fileOperationService: mockFileOp.Object, storagePath: "/tmp/analytics_load.json");
        await service.DisposeAsync(); // flush LoadHistory 后台任务

        // Dispose 后 GetEventHistory 仍可工作(不检查 _disposed)
        var history = service.GetEventHistory();
        Assert.True(history.Count >= 2);
        Assert.Contains(history, e => e.Name == "loaded_evt1");
        Assert.Contains(history, e => e.Name == "loaded_evt2");
    }

    [Fact]
    public async Task LoadHistory_ReadFailure_DoesNotLoadAndDoesNotThrow() {
        var mockFileOp = new Mock<IFileOperationService>();
        mockFileOp.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(FileReadResult.FailureResult("path", "permission denied")));
        mockFileOp.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(FileWriteResult.SuccessResult("path", "content", "write")));

        var service = new AnalyticsService(fileOperationService: mockFileOp.Object, storagePath: "/tmp/analytics_noload.json");
        var ex = await Record.ExceptionAsync(() => service.DisposeAsync().AsTask());
        Assert.Null(ex);

        Assert.Empty(service.GetEventHistory());
    }

    // ---------- Track 系列带 telemetry (mock ITelemetryService) ----------

    [Fact]
    public async Task TrackToolCall_WithTelemetry_RecordsDurationAndCount() {
        var mockTelemetry = new Mock<ITelemetryService>();
        var mockHistogram = new Mock<ITelemetryHistogram>();
        var mockCounter = new Mock<ITelemetryCounter>();
        mockTelemetry.Setup(t => t.GetHistogram(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(mockHistogram.Object);
        mockTelemetry.Setup(t => t.GetCounter(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(mockCounter.Object);

        await using var service = new AnalyticsService(telemetryService: mockTelemetry.Object);
        service.TrackToolCall("bash", success: true, durationMs: 150);

        mockTelemetry.Verify(t => t.GetHistogram("analytics.tool.duration", "ms", "Tool call duration"), Times.Once);
        mockTelemetry.Verify(t => t.GetCounter("analytics.tool.calls", "count", "Tool call count"), Times.Once);
        mockHistogram.Verify(h => h.Record(150, It.IsAny<Dictionary<string, string>>()), Times.Once);
        mockCounter.Verify(c => c.Add(1, It.IsAny<Dictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task TrackToolError_WithTelemetry_RecordsErrorCounter() {
        var mockTelemetry = new Mock<ITelemetryService>();
        var mockCounter = new Mock<ITelemetryCounter>();
        mockTelemetry.Setup(t => t.GetCounter(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(mockCounter.Object);

        await using var service = new AnalyticsService(telemetryService: mockTelemetry.Object);
        service.TrackToolError("grep", "file not found");

        mockTelemetry.Verify(t => t.GetCounter("analytics.tool.errors", "count", "Tool error count"), Times.Once);
        mockCounter.Verify(c => c.Add(1, It.IsAny<Dictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task TrackAgentStart_WithTelemetry_StartsSpanAndStoresTag() {
        var mockTelemetry = new Mock<ITelemetryService>();
        var mockSpan = new Mock<ITelemetrySpan>();
        mockTelemetry.Setup(t => t.StartSpan(It.IsAny<string>(), It.IsAny<JoinCode.Abstractions.Models.Telemetry.TelemetrySpanKind>(), It.IsAny<ITelemetrySpan?>())).Returns(mockSpan.Object);

        await using var service = new AnalyticsService(telemetryService: mockTelemetry.Object);
        service.TrackAgentStart("planner", sessionId: "sess1");

        mockTelemetry.Verify(t => t.StartSpan("agent.planner", JoinCode.Abstractions.Models.Telemetry.TelemetrySpanKind.Server, It.IsAny<ITelemetrySpan?>()), Times.Once);
        mockSpan.Verify(s => s.SetTag("agent.name", "planner"), Times.Once);
        mockSpan.Verify(s => s.SetTag("agent.session_id", "sess1"), Times.Once);
    }

    [Fact]
    public async Task TrackAgentCompleteAsync_WithTelemetry_EndsSpanAndRecordsDuration() {
        var mockTelemetry = new Mock<ITelemetryService>();
        var mockSpan = new Mock<ITelemetrySpan>();
        var mockHistogram = new Mock<ITelemetryHistogram>();
        mockTelemetry.Setup(t => t.StartSpan(It.IsAny<string>(), It.IsAny<JoinCode.Abstractions.Models.Telemetry.TelemetrySpanKind>(), It.IsAny<ITelemetrySpan?>())).Returns(mockSpan.Object);
        mockTelemetry.Setup(t => t.GetHistogram(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(mockHistogram.Object);

        await using var service = new AnalyticsService(telemetryService: mockTelemetry.Object);
        service.TrackAgentStart("worker", sessionId: "s1");
        await service.TrackAgentCompleteAsync("worker", success: true, durationMs: 300, sessionId: "s1");

        mockSpan.Verify(s => s.SetStatus(JoinCode.Abstractions.Models.Telemetry.TelemetryStatusCode.Ok, It.IsAny<string?>()), Times.Once);
        mockSpan.Verify(s => s.SetTag("agent.duration_ms", 300.0), Times.Once);
        mockSpan.Verify(s => s.DisposeAsync(), Times.Once);
        mockHistogram.Verify(h => h.Record(300, It.IsAny<Dictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task TrackAgentCompleteAsync_WithTelemetry_FailureSetsErrorStatus() {
        var mockTelemetry = new Mock<ITelemetryService>();
        var mockSpan = new Mock<ITelemetrySpan>();
        var mockHistogram = new Mock<ITelemetryHistogram>();
        mockTelemetry.Setup(t => t.StartSpan(It.IsAny<string>(), It.IsAny<JoinCode.Abstractions.Models.Telemetry.TelemetrySpanKind>(), It.IsAny<ITelemetrySpan?>())).Returns(mockSpan.Object);
        mockTelemetry.Setup(t => t.GetHistogram(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(mockHistogram.Object);

        await using var service = new AnalyticsService(telemetryService: mockTelemetry.Object);
        service.TrackAgentStart("failing", sessionId: "s2");
        await service.TrackAgentCompleteAsync("failing", success: false, durationMs: 50, sessionId: "s2");

        mockSpan.Verify(s => s.SetStatus(JoinCode.Abstractions.Models.Telemetry.TelemetryStatusCode.Error, It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task TrackAgentCompleteAsync_WithoutPriorStart_OnlyRecordsHistogram() {
        // 无对应 span(未 TrackAgentStart)时,_agentSpans 无记录,跳过 span 结束,仅 Record histogram
        var mockTelemetry = new Mock<ITelemetryService>();
        var mockHistogram = new Mock<ITelemetryHistogram>();
        mockTelemetry.Setup(t => t.GetHistogram(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(mockHistogram.Object);

        await using var service = new AnalyticsService(telemetryService: mockTelemetry.Object);
        await service.TrackAgentCompleteAsync("orphan", success: true, durationMs: 10);

        mockHistogram.Verify(h => h.Record(10, It.IsAny<Dictionary<string, string>>()), Times.Once);
        mockTelemetry.Verify(t => t.StartSpan(It.IsAny<string>(), It.IsAny<JoinCode.Abstractions.Models.Telemetry.TelemetrySpanKind>(), It.IsAny<ITelemetrySpan?>()), Times.Never);
    }
}
