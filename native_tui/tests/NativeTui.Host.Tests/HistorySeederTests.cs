namespace NativeTui.Host.Tests;

/// <summary>
/// 输入历史种子加载器测试 — 复用 sessions transcript 存储：
/// 同项目会话过滤（异项目跳过）、当前会话排除防双份、时序聚合（会话旧→新 + 会话内提交序）、
/// 末尾容量截断、transcript 服务未注册静默降级。
/// </summary>
public sealed class HistorySeederTests
{
    /// <summary>假 transcript 服务 — 内存会话存储。</summary>
    private sealed class FakeTranscriptService : ITranscriptService
    {
        public List<(string SessionId, string ProjectPath, List<TranscriptEntry> Entries)> Sessions { get; } = [];

        public Task AppendEntryAsync(string sessionId, TranscriptEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AppendEntriesAsync(string sessionId, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<TranscriptEntry>> LoadTranscriptAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var session = Sessions.Single(s => s.SessionId == sessionId);
            return Task.FromResult<IReadOnlyList<TranscriptEntry>>(session.Entries);
        }

        public Task<IReadOnlyList<TranscriptSummary>> ListTranscriptsAsync(int limit = 20, CancellationToken cancellationToken = default)
        {
            var summaries = Sessions
                .OrderByDescending(s => s.Entries.Count > 0 ? s.Entries[^1].Timestamp : DateTime.MinValue)
                .Take(limit)
                .Select(s => new TranscriptSummary { SessionId = s.SessionId })
                .ToList();
            return Task.FromResult<IReadOnlyList<TranscriptSummary>>(summaries);
        }

        public Task<SessionInfo?> GetSessionInfoAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var index = Sessions.FindIndex(s => s.SessionId == sessionId);
            return Task.FromResult(index < 0 ? null : new SessionInfo { Id = sessionId, ProjectPath = Sessions[index].ProjectPath });
        }
        public Task<bool> DeleteTranscriptAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<bool> TranscriptExistsAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task SaveCustomTitleAsync(string sessionId, string customTitle, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> GetCustomTitleAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task InsertContentReplacementAsync(string sessionId, IReadOnlyList<ContentReplacementRecord> records, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ContentReplacementRecord>> LoadContentReplacementsAsync(string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ContentReplacementRecord>>([]);

        public Task SaveSessionInfoAsync(string sessionId, SessionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task MigrateLegacyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>用户输入条目。</summary>
    private static TranscriptEntry User(string content, int minute) => new() { Role = "user", Content = content, Timestamp = DateTime.UtcNow.AddMinutes(minute) };

    /// <summary>最小假引擎（历史种子测试不需要回合行为）。</summary>
    private sealed class StubEngine : IReplEngine
    {
        public void Submit(string text) { }
        public void Interrupt() { }
        public void SetPermissionMode(string mode) { }
        public IReadOnlyList<BridgeEvent> DrainEvents() => [];
        public bool IsAlive => true;
    }

    /// <summary>构造 shell + 注册假 transcript 的服务容器。</summary>
    private static (ReplShell Shell, FakeTranscriptService Transcript, IServiceProvider Services) CreateShell()
    {
        var transcript = new FakeTranscriptService();
        var services = new ServiceCollection().AddSingleton<ITranscriptService>(transcript).BuildServiceProvider();
        var shell = new ReplShell(new StubEngine(), new BannerItem("1.0.0", "m", "API 按量计费", "/tmp"), new StringWriter());
        return (shell, transcript, services);
    }

    /// <summary>同项目两个会话（新→旧列出）：时序聚合为 旧会话输入 → 新会话输入。</summary>
    [Fact]
    public async Task SeedAsync_AggregatesProjectSessionsChronologically()
    {
        var (shell, transcript, services) = CreateShell();
        var project = Environment.CurrentDirectory;
        transcript.Sessions.Add(("session-new", project, [User("new-1", 10), User("new-2", 11)]));
        transcript.Sessions.Add(("session-old", project, [User("old-1", 1), User("old-2", 2)]));

        await HistorySeeder.SeedAsync(shell, services, excludeSessionId: "current");
        Assert.Equal(["old-1", "old-2", "new-1", "new-2"], ReadHistory(shell));
    }

    /// <summary>异项目会话跳过（基准按 project 过滤）。</summary>
    [Fact]
    public async Task SeedAsync_SkipsOtherProjects()
    {
        var (shell, transcript, services) = CreateShell();
        transcript.Sessions.Add(("other-project", @"D:\other", [User("foreign", 5)]));
        transcript.Sessions.Add(("same-project", Environment.CurrentDirectory, [User("local", 6)]));

        await HistorySeeder.SeedAsync(shell, services, excludeSessionId: "current");
        Assert.Equal(["local"], ReadHistory(shell));
    }

    /// <summary>当前会话排除（防双份 — 本会话输入由运行时 Submit 入史）。</summary>
    [Fact]
    public async Task SeedAsync_ExcludesCurrentSession()
    {
        var (shell, transcript, services) = CreateShell();
        var project = Environment.CurrentDirectory;
        transcript.Sessions.Add(("current", project, [User("already-live", 5)]));
        transcript.Sessions.Add(("past", project, [User("past-input", 1)]));

        await HistorySeeder.SeedAsync(shell, services, excludeSessionId: "current");
        Assert.Equal(["past-input"], ReadHistory(shell));
    }

    /// <summary>assistant 回复/元数据条目/侧链/空输入不入史（仅 Role=user 且 Type=null）。</summary>
    [Fact]
    public async Task SeedAsync_FiltersUserEntriesOnly()
    {
        var (shell, transcript, services) = CreateShell();
        transcript.Sessions.Add(("s", Environment.CurrentDirectory,
        [
            User("real-input", 1),
            new TranscriptEntry { Role = "assistant", Content = "reply", Timestamp = DateTime.UtcNow },
            new TranscriptEntry { Role = "user", Type = "custom-title", Content = "title", Timestamp = DateTime.UtcNow },
            new TranscriptEntry { Role = "user", IsSidechain = true, Content = "side", Timestamp = DateTime.UtcNow },
            new TranscriptEntry { Role = "user", Content = string.Empty, Timestamp = DateTime.UtcNow },
        ]));

        await HistorySeeder.SeedAsync(shell, services, excludeSessionId: "current");
        Assert.Equal(["real-input"], ReadHistory(shell));
    }

    /// <summary>超过容量 100：取末尾（最旧淘汰）。</summary>
    [Fact]
    public async Task SeedAsync_OverCapacity_KeepsLatestHundred()
    {
        var (shell, transcript, services) = CreateShell();
        transcript.Sessions.Add(("s", Environment.CurrentDirectory,
            Enumerable.Range(0, 120).Select(i => User($"input-{i}", i)).ToList()));

        await HistorySeeder.SeedAsync(shell, services, excludeSessionId: "current");
        Assert.Equal(Enumerable.Range(20, 100).Select(i => $"input-{i}"), ReadHistory(shell));
    }

    /// <summary>transcript 服务未注册：静默降级（无历史不报错）。</summary>
    [Fact]
    public async Task SeedAsync_NoTranscriptService_SilentDegradation()
    {
        var shell = new ReplShell(new StubEngine(), new BannerItem("1.0.0", "m", "API 按量计费", "/tmp"), new StringWriter());
        var services = new ServiceCollection().BuildServiceProvider();
        await HistorySeeder.SeedAsync(shell, services, excludeSessionId: "current");
        Assert.Equal([], ReadHistory(shell));
    }

    /// <summary>读取 shell 历史条目（旧→新）。</summary>
    private static List<string> ReadHistory(ReplShell shell) => shell.History.Entries.ToList();
}
