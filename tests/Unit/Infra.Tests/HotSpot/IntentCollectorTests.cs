namespace Infra.Tests.HotSpot;


public sealed class IntentCollectorTests
{
    private readonly IIntentCollector _sut = new IntentCollector();

    private static FileModifyIntent MakeIntent(string path, ModifyIntent intent, string workerId) =>
        new() { FilePath = path, Intent = intent, WorkerId = workerId, ReportedAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task ReportAsync_SingleWorkerSingleFile_ShouldBeRetrievable()
    {
        var intent = MakeIntent("src/IFoo.cs", ModifyIntent.ContractChange, "worker-1");
        await _sut.ReportAsync("worker-1", [intent]);

        var result = _sut.GetIntents("src/IFoo.cs");
        result.Should().HaveCount(1);
        result[0].FilePath.Should().Be("src/IFoo.cs");
        result[0].Intent.Should().Be(ModifyIntent.ContractChange);
        result[0].WorkerId.Should().Be("worker-1");
    }

    [Fact]
    public async Task ReportAsync_MultipleWorkersSameFile_ShouldAggregateAll()
    {
        var intents = new List<FileModifyIntent>
        {
            MakeIntent("src/IFoo.cs", ModifyIntent.ContractChange, "worker-1"),
            MakeIntent("src/IFoo.cs", ModifyIntent.InternalChange, "worker-2"),
            MakeIntent("src/IFoo.cs", ModifyIntent.ContractChange, "worker-3")
        };

        await _sut.ReportAsync("worker-1", [intents[0]]);
        await _sut.ReportAsync("worker-2", [intents[1]]);
        await _sut.ReportAsync("worker-3", [intents[2]]);

        var result = _sut.GetIntents("src/IFoo.cs");
        result.Should().HaveCount(3);
        result.Select(x => x.WorkerId).Should().BeEquivalentTo(["worker-1", "worker-2", "worker-3"]);
    }

    [Fact]
    public async Task ReportAsync_DifferentFiles_ShouldBeIsolated()
    {
        await _sut.ReportAsync("worker-1", [
            MakeIntent("src/IFoo.cs", ModifyIntent.ContractChange, "worker-1"),
            MakeIntent("src/IBar.cs", ModifyIntent.InternalChange, "worker-1")
        ]);

        _sut.GetIntents("src/IFoo.cs").Should().HaveCount(1);
        _sut.GetIntents("src/IBar.cs").Should().HaveCount(1);
        _sut.GetIntents("src/IBaz.cs").Should().BeEmpty();
    }

    [Fact]
    public async Task ReportAsync_BatchIntents_ShouldStoreAll()
    {
        var batch = new List<FileModifyIntent>
        {
            MakeIntent("a.cs", ModifyIntent.InternalChange, "w1"),
            MakeIntent("b.cs", ModifyIntent.ContractChange, "w1"),
            MakeIntent("c.cs", ModifyIntent.InternalChange, "w1")
        };

        await _sut.ReportAsync("w1", batch);

        _sut.GetIntents("a.cs").Should().HaveCount(1);
        _sut.GetIntents("b.cs").Should().HaveCount(1);
        _sut.GetIntents("c.cs").Should().HaveCount(1);
    }

    [Fact]
    public async Task GetIntents_WindowsStylePath_ShouldNormalizeAndMatch()
    {
        await _sut.ReportAsync("w1", [MakeIntent("src/IFoo.cs", ModifyIntent.ContractChange, "w1")]);

        _sut.GetIntents(@"src\IFoo.cs").Should().HaveCount(1, "反斜杠路径应归一化后匹配");
    }

    [Fact]
    public async Task GetAllIntents_ShouldReturnAllAcrossFiles()
    {
        await _sut.ReportAsync("w1", [
            MakeIntent("a.cs", ModifyIntent.InternalChange, "w1"),
            MakeIntent("b.cs", ModifyIntent.ContractChange, "w1")
        ]);
        await _sut.ReportAsync("w2", [
            MakeIntent("c.cs", ModifyIntent.InternalChange, "w2")
        ]);

        _sut.GetAllIntents().Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAllIntents_Empty_ShouldReturnEmpty()
    {
        _sut.GetAllIntents().Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveWorkerAsync_ShouldRemoveOnlyThatWorkerIntents()
    {
        await _sut.ReportAsync("w1", [MakeIntent("a.cs", ModifyIntent.ContractChange, "w1")]);
        await _sut.ReportAsync("w2", [MakeIntent("a.cs", ModifyIntent.InternalChange, "w2")]);
        await _sut.ReportAsync("w3", [MakeIntent("a.cs", ModifyIntent.ContractChange, "w3")]);

        await _sut.RemoveWorkerAsync("w2");

        var remaining = _sut.GetIntents("a.cs");
        remaining.Should().HaveCount(2);
        remaining.Select(x => x.WorkerId).Should().BeEquivalentTo(["w1", "w3"]);
    }

    [Fact]
    public async Task RemoveWorkerAsync_NonExistentWorker_ShouldNotThrow()
    {
        await _sut.ReportAsync("w1", [MakeIntent("a.cs", ModifyIntent.ContractChange, "w1")]);

        var act = () => _sut.RemoveWorkerAsync("non-existent");
        await act.Should().NotThrowAsync();

        _sut.GetIntents("a.cs").Should().HaveCount(1);
    }

    [Fact]
    public async Task ReportAsync_ConcurrentReports_ShouldNotLoseData()
    {
        const int workerCount = 10;
        const int intentsPerWorker = 50;
        var workers = Enumerable.Range(0, workerCount).Select(i => $"worker-{i}").ToList();

        var tasks = workers.Select(w => Task.Run(async () =>
        {
            var batch = Enumerable.Range(0, intentsPerWorker)
                .Select(j => MakeIntent($"file-{j}.cs", ModifyIntent.InternalChange, w))
                .ToList();
            await _sut.ReportAsync(w, batch);
        }));

        await Task.WhenAll(tasks);

        var allIntents = _sut.GetAllIntents();
        allIntents.Should().HaveCount(workerCount * intentsPerWorker);

        for (int j = 0; j < intentsPerWorker; j++)
        {
            _sut.GetIntents($"file-{j}.cs").Should().HaveCount(workerCount);
        }
    }

    [Fact]
    public async Task ReportAsync_NullWorkerId_ShouldThrow()
    {
        var act = () => _sut.ReportAsync("", [MakeIntent("a.cs", ModifyIntent.InternalChange, "w1")]);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ReportAsync_NullIntents_ShouldThrow()
    {
        var act = () => _sut.ReportAsync("w1", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
