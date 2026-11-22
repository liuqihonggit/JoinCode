namespace Core.Tests.Context;

/// <summary>
/// TASK127（#42）：查询期自动记忆召回中间件的门与注入行为。
/// 全部用 Mock 替身 —— 被测对象是「门是否生效、注入什么」，不是记忆扫描算法本身。
/// </summary>
public class MemoryRecallMiddlewareTests {
    private static readonly MiddlewareDelegate<PreprocessContext> NoopNext = static (_, _) => Task.CompletedTask;

    private static PreprocessContext Ctx(string message) => new() { Message = message };

    private static Core.Memdir.MemoryScanResult Scan(
        int total,
        params (string Id, string Content, double Score)[] hits) => new() {
            TotalMemories = total,
            RelevantMemories = hits.Select(h => new Core.Memdir.DetailedScoredMemory {
                Memory = new Core.Memdir.MemoryEntry {
                    Id = h.Id,
                    Type = Core.Memdir.MemoryType.User,
                    Content = h.Content,
                },
                RelevanceScore = h.Score,
            }).ToList(),
        };

    private static MemoryRecallMiddleware CreateSut(
        Mock<Core.Memdir.IMemoryManagementService>? memory,
        Mock<IChatContextManager>? context,
        bool enabled = true) =>
        new(memory?.Object, context?.Object, new WorkflowConfig { MemoryAutoRecallEnabled = enabled });

    private static Mock<IChatContextManager> ChatSpy(out List<string> captured) {
        var list = new List<string>();
        captured = list;
        var mock = new Mock<IChatContextManager>();
        mock.Setup(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((text, _) => list.Add(text))
            .Returns(Task.CompletedTask);
        return mock;
    }

    [Fact]
    public async Task Disabled_DoesNotScan() {
        var memory = new Mock<Core.Memdir.IMemoryManagementService>();
        var context = ChatSpy(out var captured);
        await using var sut = CreateSut(memory, context, enabled: false);

        await sut.InvokeAsync(Ctx("请帮我看看这个模块的分层设计"), NoopNext, CancellationToken.None).ConfigureAwait(true);

        memory.Verify(
            m => m.ScanMemoriesAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        captured.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/memory search 分层")]
    [InlineData("hi")]
    [InlineData("   ")]
    public async Task Gates_SkipWithoutScanning(string message) {
        var memory = new Mock<Core.Memdir.IMemoryManagementService>();
        var context = ChatSpy(out var captured);
        await using var sut = CreateSut(memory, context);

        await sut.InvokeAsync(Ctx(message), NoopNext, CancellationToken.None).ConfigureAwait(true);

        memory.Verify(
            m => m.ScanMemoriesAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task NoMemories_SkipsInjection() {
        var memory = new Mock<Core.Memdir.IMemoryManagementService>();
        memory.Setup(m => m.ScanMemoriesAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Scan(0));
        var context = ChatSpy(out var captured);
        await using var sut = CreateSut(memory, context);

        await sut.InvokeAsync(Ctx("请帮我看看这个模块的分层设计"), NoopNext, CancellationToken.None).ConfigureAwait(true);

        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task LowTopScore_SkipsInjection() {
        var memory = new Mock<Core.Memdir.IMemoryManagementService>();
        memory.Setup(m => m.ScanMemoriesAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Scan(3, ("a", "无关内容", 0.05)));
        var context = ChatSpy(out var captured);
        await using var sut = CreateSut(memory, context);

        await sut.InvokeAsync(Ctx("请帮我看看这个模块的分层设计"), NoopNext, CancellationToken.None).ConfigureAwait(true);

        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task Hit_InjectsSingleDynamicMessage() {
        var memory = new Mock<Core.Memdir.IMemoryManagementService>();
        memory.Setup(m => m.ScanMemoriesAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Scan(4, ("m1", "分层规则：组合根不允许被下层引用", 0.72)));
        var context = ChatSpy(out var captured);
        await using var sut = CreateSut(memory, context);

        await sut.InvokeAsync(Ctx("请帮我看看这个模块的分层设计"), NoopNext, CancellationToken.None).ConfigureAwait(true);

        captured.Should().ContainSingle("一次召回只写一条动态系统消息");
        captured[0].Should().Contain("组合根不允许被下层引用");
        captured[0].Should().Contain("系统自动检索", "必须标明来源，避免模型当成用户输入");
    }

    [Fact]
    public async Task ScanThrows_StillCallsNext() {
        var memory = new Mock<Core.Memdir.IMemoryManagementService>();
        memory.Setup(m => m.ScanMemoriesAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("扫描炸了"));
        var context = ChatSpy(out var captured);
        await using var sut = CreateSut(memory, context);
        var nextCalled = false;

        await sut.InvokeAsync(
            Ctx("请帮我看看这个模块的分层设计"),
            (_, _) => {
                nextCalled = true;
                return Task.CompletedTask;
            },
            CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue("召回失败绝不许拦住回合");
        captured.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingService_SkipsGracefully() {
        var context = ChatSpy(out var captured);
        await using var sut = CreateSut(memory: null, context);
        var nextCalled = false;

        await sut.InvokeAsync(
            Ctx("请帮我看看这个模块的分层设计"),
            (_, _) => {
                nextCalled = true;
                return Task.CompletedTask;
            },
            CancellationToken.None).ConfigureAwait(true);

        nextCalled.Should().BeTrue();
        captured.Should().BeEmpty();
    }

    [Fact]
    public void Format_RespectsCountAndCharBudget() {
        var hits = Enumerable.Range(0, 8)
            .Select(i => new Core.Memdir.DetailedScoredMemory {
                Memory = new Core.Memdir.MemoryEntry {
                    Id = $"m{i}",
                    Type = Core.Memdir.MemoryType.User,
                    Content = new string('x', 240),
                },
                RelevanceScore = 0.5,
            })
            .ToList();

        var text = MemoryRecallMiddleware.Format(hits);

        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.Should().BeLessThanOrEqualTo(MemoryRecallMiddleware.MaxMemories + 1);
        text.Length.Should().BeLessThanOrEqualTo(MemoryRecallMiddleware.MaxTotalChars);
    }

    [Fact]
    public void Format_FallsBackToContentFirstLineWhenNoTitle() {
        var hits = new List<Core.Memdir.DetailedScoredMemory> {
            new() {
                Memory = new Core.Memdir.MemoryEntry {
                    Id = "m1",
                    Type = Core.Memdir.MemoryType.User,
                    Content = "首行标题\n第二行正文",
                },
                RelevanceScore = 0.9,
            },
        };

        var text = MemoryRecallMiddleware.Format(hits);

        text.Should().Contain("首行标题");
        text.Should().NotContain("\n第二行正文", "标题只取首行");
    }
}
