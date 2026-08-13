namespace Infra.Tests.Resilience;

public sealed class CrashSnapshotTests
{
    [Fact]
    public void CrashSnapshot_Captures_Basic_Exception_Info()
    {
        var ex = new WorkflowException("test error", ErrorCode.WorkflowExecution.ToValue());
        var snapshot = new CrashSnapshot("TestFence", CrashSeverity.Error, ex);

        snapshot.FenceName.Should().Be("TestFence");
        snapshot.Severity.Should().Be(CrashSeverity.Error);
        snapshot.ExceptionType.Should().Be("JoinCode.Abstractions.Exceptions.WorkflowException");
        snapshot.ExceptionMessage.Should().Be("test error");
        snapshot.ErrorCode.Should().Be("WF003");
        snapshot.ErrorCategory.Should().Be(ErrorCategory.Workflow);
        snapshot.State.Should().Be(CrashSnapshotState.Captured);
        snapshot.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void CrashSnapshot_Extracts_ErrorCode_From_WorkflowException()
    {
        var ex = ApiException.RateLimit("test-endpoint");
        var snapshot = new CrashSnapshot("ApiFence", CrashSeverity.Warning, ex);

        snapshot.ErrorCode.Should().Be("API004");
        snapshot.ErrorCategory.Should().Be(ErrorCategory.Api);
    }

    [Fact]
    public void CrashSnapshot_Chain_Builds_InnerExceptions()
    {
        var inner = new InvalidOperationException("inner error");
        var outer = new WorkflowException("outer error", inner, ErrorCode.WorkflowExecution.ToValue());
        var snapshot = new CrashSnapshot("ChainFence", CrashSeverity.Error, outer);

        snapshot.ExceptionChain.Depth.Should().Be(2);
        snapshot.ExceptionChain.RootExceptionType.Should().Contain("WorkflowException");
        snapshot.ExceptionChain.Frames.Should().HaveCount(2);
        snapshot.ExceptionChain.Frames[0].Message.Should().Be("outer error");
        snapshot.ExceptionChain.Frames[1].Message.Should().Be("inner error");
    }

    [Fact]
    public void CrashSnapshot_WithContext_Populates_ExecutionContext()
    {
        var ex = new Exception("boom");
        var ctx = new CrashExecutionContext
        {
            ToolName = "Bash",
            TurnIndex = 3,
            RequestId = "req-123",
            SessionId = "sess-456"
        };
        var snapshot = new CrashSnapshot("ToolFence", CrashSeverity.Error, ex, ctx);

        snapshot.ExecutionContext.ToolName.Should().Be("Bash");
        snapshot.ExecutionContext.TurnIndex.Should().Be(3);
        snapshot.ExecutionContext.RequestId.Should().Be("req-123");
    }

    [Fact]
    public void CrashSnapshot_ToSummary_Includes_Key_Info()
    {
        var ex = new WorkflowException("test", ErrorCode.ApiTimeout.ToValue());
        var ctx = new CrashExecutionContext { ToolName = "Read", TurnIndex = 5 };
        var snapshot = new CrashSnapshot("SumFence", CrashSeverity.Fatal, ex, ctx);

        var summary = snapshot.ToSummary();
        summary.Should().Contain("FATAL");
        summary.Should().Contain("SumFence");
        summary.Should().Contain("API003");
        summary.Should().Contain("Read");
        summary.Should().Contain("Turn=5");
    }

    [Fact]
    public void CrashSnapshot_Tags_And_Attachments_Work()
    {
        var ex = new Exception("tagged");
        var snapshot = new CrashSnapshot("TagFence", CrashSeverity.Error, ex);
        snapshot.WithTag("env", "production");
        snapshot.WithAttachment("config", "key=value");

        snapshot.Tags["env"].Should().Be("production");
        snapshot.Attachments["config"].Should().Be("key=value");
    }
}

public sealed class CrashSnapshotStoreTests
{
    private readonly CrashSnapshotStore _store = new(maxCapacity: 10);

    [Fact]
    public void Add_Increments_Count()
    {
        var ex = new Exception("test");
        var snapshot = new CrashSnapshot("F1", CrashSeverity.Error, ex);

        _store.Add(snapshot);

        _store.TotalCount.Should().Be(1);
        _store.UnacknowledgedCount.Should().Be(1);
    }

    [Fact]
    public void GetRecent_Returns_Latest_First()
    {
        for (var i = 0; i < 5; i++)
            _store.Add(new CrashSnapshot($"F{i}", CrashSeverity.Error, new Exception($"e{i}")));

        var recent = _store.GetRecent(3);
        recent.Should().HaveCount(3);
        recent[0].FenceName.Should().Be("F4");
        recent[1].FenceName.Should().Be("F3");
    }

    [Fact]
    public void GetByFence_Filters_Correctly()
    {
        _store.Add(new CrashSnapshot("Alpha", CrashSeverity.Error, new Exception("a1")));
        _store.Add(new CrashSnapshot("Beta", CrashSeverity.Error, new Exception("b1")));
        _store.Add(new CrashSnapshot("Alpha", CrashSeverity.Fatal, new Exception("a2")));

        var alpha = _store.GetByFence("Alpha");
        alpha.Should().HaveCount(2);
        alpha.All(s => s.FenceName == "Alpha").Should().BeTrue();
    }

    [Fact]
    public void Acknowledge_Changes_State()
    {
        var snapshot = new CrashSnapshot("Ack", CrashSeverity.Error, new Exception("ack"));
        _store.Add(snapshot);

        _store.Acknowledge(snapshot.Id);

        snapshot.State.Should().Be(CrashSnapshotState.Acknowledged);
        _store.UnacknowledgedCount.Should().Be(0);
    }

    [Fact]
    public void Store_Evicts_Oldest_When_Over_Capacity()
    {
        var smallStore = new CrashSnapshotStore(maxCapacity: 3);
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var s = new CrashSnapshot($"F{i}", CrashSeverity.Error, new Exception($"e{i}"));
            ids.Add(s.Id);
            smallStore.Add(s);
        }

        smallStore.TotalCount.Should().Be(3);
        smallStore.GetById(ids[0]).Should().BeNull();
        smallStore.GetById(ids[4]).Should().NotBeNull();
    }

    [Fact]
    public void SnapshotAdded_Event_Fires()
    {
        CrashSnapshot? captured = null;
        _store.SnapshotAdded += (_, s) => captured = s;

        var snapshot = new CrashSnapshot("Event", CrashSeverity.Error, new Exception("evt"));
        _store.Add(snapshot);

        captured.Should().NotBeNull();
        captured!.FenceName.Should().Be("Event");
    }

    [Fact]
    public void FormatReport_Returns_NonEmpty_String()
    {
        _store.Add(new CrashSnapshot("R1", CrashSeverity.Error, new Exception("r1")));
        _store.Add(new CrashSnapshot("R2", CrashSeverity.Fatal, new Exception("r2")));

        var report = _store.FormatReport();
        report.Should().Contain("崩溃快照报告");
        report.Should().Contain("R1");
        report.Should().Contain("R2");
    }
}

public sealed class FaultFenceTests
{
    [Fact]
    public async Task ExecuteAsync_Success_Returns_Value()
    {
        var fence = new FaultFence("SuccessFence");
        var result = await fence.ExecuteAsync(() => Task.FromResult(42));
        result.Should().Be(42);
        fence.TotalExecutions.Should().Be(1);
        fence.TotalFailures.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_Exception_Captures_Snapshot()
    {
        var store = new CrashSnapshotStore();
        var fence = new FaultFence("ExFence", store: store);

        var act = async () => await fence.ExecuteAsync<int>(() => throw new WorkflowException("boom"));
        await act.Should().ThrowAsync<WorkflowException>();

        fence.TotalFailures.Should().Be(1);
        store.TotalCount.Should().Be(1);
        store.GetRecent(1)[0].FenceName.Should().Be("ExFence");
    }

    [Fact]
    public void TryExecute_Returns_FaultFenceResult()
    {
        var fence = new FaultFence("TryFence");
        var result = fence.TryExecute(() => "ok");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
        result.Snapshot.Should().BeNull();
    }

    [Fact]
    public void TryExecute_Exception_Returns_Snapshot()
    {
        var store = new CrashSnapshotStore();
        var fence = new FaultFence("TryExFence", store: store);

        var result = fence.TryExecute<int>(() => throw new Exception("fail"));

        result.IsSuccess.Should().BeFalse();
        result.Snapshot.Should().NotBeNull();
        result.Snapshot!.ExceptionMessage.Should().Be("fail");
    }

    [Fact]
    public async Task TryExecuteAsync_Exception_DoesNotThrow()
    {
        var fence = new FaultFence("TryAsyncFence");
        var result = await fence.TryExecuteAsync<int>(() => throw new Exception("async fail"));

        result.IsSuccess.Should().BeFalse();
        result.Snapshot.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_Fatal_Exception_Interrupts()
    {
        var fence = new FaultFence("FatalFence", shouldInterrupt: ex => ex is OutOfMemoryException);

        var act = async () => await fence.ExecuteAsync<int>(() => throw new OutOfMemoryException());
        await act.Should().ThrowAsync<OutOfMemoryException>();

        fence.TotalInterrupts.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_NonFatal_Exception_DoesNotInterrupt()
    {
        var fence = new FaultFence("NonFatalFence", shouldInterrupt: ex => ex is OutOfMemoryException);

        var act = async () => await fence.ExecuteAsync<int>(() => throw new InvalidOperationException());
        await act.Should().ThrowAsync<InvalidOperationException>();

        fence.TotalInterrupts.Should().Be(0);
    }

    [Fact]
    public void CaptureSnapshot_Creates_Snapshot_With_Context()
    {
        var store = new CrashSnapshotStore();
        var fence = new FaultFence("CaptureFence", store: store);
        var ex = ApiException.RateLimit("test-endpoint");
        var ctx = new CrashExecutionContext { ToolName = "Bash", TurnIndex = 2 };

        var snapshot = fence.CaptureSnapshot(ex, ctx);

        snapshot.FenceName.Should().Be("CaptureFence");
        snapshot.ErrorCode.Should().Be("API004");
        snapshot.ExecutionContext.ToolName.Should().Be("Bash");
        store.TotalCount.Should().Be(1);
    }

    [Fact]
    public void SeverityClassifier_Customizes_Severity()
    {
        var fence = new FaultFence("SeverityFence",
            severityClassifier: ex => ex is TimeoutException ? CrashSeverity.Warning : CrashSeverity.Fatal);
        var store = new CrashSnapshotStore();
        var fenceWithStore = new FaultFence("SeverityFence2", store: store,
            severityClassifier: ex => ex is TimeoutException ? CrashSeverity.Warning : CrashSeverity.Fatal);

        var timeoutSnapshot = fenceWithStore.CaptureSnapshot(new TimeoutException());
        timeoutSnapshot.Severity.Should().Be(CrashSeverity.Warning);

        var otherSnapshot = fenceWithStore.CaptureSnapshot(new InvalidOperationException());
        otherSnapshot.Severity.Should().Be(CrashSeverity.Fatal);
    }
}
