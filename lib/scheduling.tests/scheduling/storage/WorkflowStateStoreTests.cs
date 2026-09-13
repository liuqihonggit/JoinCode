
namespace Core.Tests.Scheduling.Storage;

public sealed class WorkflowStateStoreTests : IDisposable
{
    private readonly InMemoryFileOperationService _fileOperationService;
    private const string PersistDir = "/test/workflow-states";

    public WorkflowStateStoreTests()
    {
        _fileOperationService = new InMemoryFileOperationService();
    }

    public void Dispose()
    {
        _fileOperationService.Dispose();
    }

    private WorkflowStateStore CreateStore() => new(_fileOperationService, PersistDir);

    private static WorkflowSnapshot CreateSampleSnapshot(string workflowId = "wf-1")
    {
        return new WorkflowSnapshot
        {
            WorkflowId = workflowId,
            StepStates = new Dictionary<string, StepState>
            {
                ["step-a"] = StepState.Completed,
                ["step-b"] = StepState.Failed
            },
            SkipReasons = new Dictionary<string, string> { ["step-c"] = "依赖 step-b 失败" },
            LastUpdated = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero)
        };
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_ShouldPreserveData()
    {
        var store = CreateStore();
        var snapshot = CreateSampleSnapshot();

        await store.SaveSnapshotAsync("wf-1", snapshot);
        var loaded = await store.LoadSnapshotAsync("wf-1");

        loaded.Should().NotBeNull();
        loaded!.WorkflowId.Should().Be("wf-1");
        loaded.StepStates.Should().HaveCount(2);
        loaded.StepStates["step-a"].Should().Be(StepState.Completed);
        loaded.StepStates["step-b"].Should().Be(StepState.Failed);
        loaded.SkipReasons.Should().ContainKey("step-c").WhoseValue.Should().Be("依赖 step-b 失败");
        loaded.LastUpdated.Should().Be(new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task LoadSnapshot_NonExistent_ShouldReturnNull()
    {
        var store = CreateStore();

        var loaded = await store.LoadSnapshotAsync("non-existent");

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task LoadSnapshot_CorruptFile_ShouldReturnNullAndQuarantine()
    {
        var store = CreateStore();
        _fileOperationService.FileSystem.CreateDirectory(PersistDir);
        var filePath = $"{PersistDir}/workflow_wf-corrupt.state.json";
        _fileOperationService.FileSystem.WriteAllText(filePath, "{not valid json");

        var loaded = await store.LoadSnapshotAsync("wf-corrupt");

        loaded.Should().BeNull();
        _fileOperationService.FileExists(filePath).Should().BeFalse("损坏文件应被隔离");
        _fileOperationService.FileSystem.EnumerateFiles(PersistDir, "*.corrupt", SearchOption.TopDirectoryOnly)
            .Should().NotBeEmpty();
    }

    [Fact]
    public async Task SaveSnapshot_ShouldUseAtomicWrite_WithTempFileMove()
    {
        var store = CreateStore();
        _fileOperationService.FileSystem.CreateDirectory(PersistDir);

        var tmpPath = $"{PersistDir}/workflow_wf-atomic.state.json.tmp";
        _fileOperationService.FileSystem.WriteAllText(tmpPath, "stale temp from previous crash");

        await store.SaveSnapshotAsync("wf-atomic", CreateSampleSnapshot("wf-atomic"));

        _fileOperationService.FileExists(tmpPath).Should().BeFalse("原子写应清理临时文件");
    }

    [Fact]
    public async Task SaveSnapshot_DifferentWorkflows_ShouldNotConflict()
    {
        var store = CreateStore();

        await store.SaveSnapshotAsync("wf-a", CreateSampleSnapshot("wf-a"));
        await store.SaveSnapshotAsync("wf-b", CreateSampleSnapshot("wf-b"));

        var loadedA = await store.LoadSnapshotAsync("wf-a");
        var loadedB = await store.LoadSnapshotAsync("wf-b");

        loadedA!.WorkflowId.Should().Be("wf-a");
        loadedB!.WorkflowId.Should().Be("wf-b");
    }

    [Fact]
    public async Task SaveSnapshot_OverwriteExisting_ShouldReplace()
    {
        var store = CreateStore();

        var first = new WorkflowSnapshot
        {
            WorkflowId = "wf-overwrite",
            StepStates = new Dictionary<string, StepState> { ["old"] = StepState.Completed },
            LastUpdated = DateTimeOffset.UtcNow
        };
        await store.SaveSnapshotAsync("wf-overwrite", first);

        var second = new WorkflowSnapshot
        {
            WorkflowId = "wf-overwrite",
            StepStates = new Dictionary<string, StepState> { ["new"] = StepState.Completed },
            LastUpdated = DateTimeOffset.UtcNow
        };
        await store.SaveSnapshotAsync("wf-overwrite", second);

        var loaded = await store.LoadSnapshotAsync("wf-overwrite");
        loaded!.StepStates.Should().ContainKey("new").And.NotContainKey("old");
    }
}
