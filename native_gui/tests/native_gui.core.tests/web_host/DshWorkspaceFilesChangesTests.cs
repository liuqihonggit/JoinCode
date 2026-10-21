namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// workspaceFiles/changes 流测试（TASK058 P2-B4）：CreateChangeFeedAsync 产出帧 Channel
/// （ready 由宿主发；change 帧由 watcher 变更驱动，version 复用 size-mtime 合成，deleted → absent）。
/// 用 InMemoryFileSystem.Watch 的写联动（写文件自动 NotifyChanged）做确定性验证。
/// </summary>
public sealed class DshWorkspaceFilesChangesTests {
    /// <summary>构造带可监听文件系统的会话（cwd 下有子目录）</summary>
    private static async Task<(DshHostServices Services, InMemoryFileSystem Fs, string SessionId)> CreateWatchableSessionAsync() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        fs.CreateDirectory("/test/proj/docs");
        await fs.WriteAllTextAsync("/test/proj/readme.md", "v1\n");
        return (services, fs, sessionId);
    }

    [Fact]
    public async Task CreateChangeFeed_WriteFile_EmitsChangeFrameWithVersion() {
        var (services, fs, sessionId) = await CreateWatchableSessionAsync();

        var feed = await services.WorkspaceFiles.CreateChangeFeedAsync(sessionId, "docs", CancellationToken.None);
        feed.Error.Should().BeNull();
        var frames = feed.Frames ?? throw new Xunit.Sdk.XunitException("Frames 缺失");
        using var lease = feed.Lease ?? throw new Xunit.Sdk.XunitException("Lease 缺失");

        await fs.WriteAllTextAsync("/test/proj/docs/note.md", "hello\n");

        var frame = await ReadFrameAsync(frames);
        var kind = frame.GetProperty("kind").GetString();
        kind.Should().Be("change");
        var change = frame.GetProperty("change");
        change.GetProperty("absolutePath").GetString().Should().Contain("docs").And.Contain("note.md");
        change.GetProperty("version").GetString().Should().NotBeNullOrEmpty("version=size-mtime 合成");
        change.TryGetProperty("absent", out _).Should().BeFalse("创建/变更非删除");
    }

    [Fact]
    public async Task CreateChangeFeed_DeleteFile_EmitsAbsentFrame() {
        var (services, fs, sessionId) = await CreateWatchableSessionAsync();

        var feed = await services.WorkspaceFiles.CreateChangeFeedAsync(sessionId, "", CancellationToken.None);
        using var lease = feed.Lease ?? throw new Xunit.Sdk.XunitException("Lease 缺失");

        fs.DeleteFile("/test/proj/readme.md");

        var frame = await ReadFrameAsync(feed.Frames ?? throw new Xunit.Sdk.XunitException("Frames 缺失"));
        var change = frame.GetProperty("change");
        change.GetProperty("absolutePath").GetString().Should().Contain("readme.md");
        change.GetProperty("absent").GetBoolean().Should().BeTrue("删除事件 → absent:true");
    }

    [Fact]
    public async Task CreateChangeFeed_UnknownSession_ReturnsGuidanceError() {
        var (services, _, _) = await CreateWatchableSessionAsync();

        var feed = await services.WorkspaceFiles.CreateChangeFeedAsync("no-such-session", "", CancellationToken.None);

        feed.Error.Should().NotBeNull();
        if (feed.Error is { Error: { } detail }) {
            detail.Code.Should().Be("workspaceFiles/rejected");
        }
        feed.Frames.Should().BeNull();
    }

    [Fact]
    public async Task CreateChangeFeed_LeaseDisposal_StopsEmission() {
        var (services, fs, sessionId) = await CreateWatchableSessionAsync();
        var feed = await services.WorkspaceFiles.CreateChangeFeedAsync(sessionId, "", CancellationToken.None);
        {
            using var lease = feed.Lease ?? throw new Xunit.Sdk.XunitException("Lease 缺失");
        }
        await fs.WriteAllTextAsync("/test/proj/new-file.md", "after dispose\n");

        // 释放后通道应已完成（读端收到 0 项）— 确定性验证：TryRead 恒 false
        var frames = feed.Frames ?? throw new Xunit.Sdk.XunitException("Frames 缺失");
        frames.Reader.TryRead(out _).Should().BeFalse("lease 释放后不再产帧");
        await frames.Reader.Completion;
    }

    /// <summary>读一帧（5s 超时 — watcher 通知经内存联动同步/异步混合，收敛等待）</summary>
    private static async Task<JsonElement> ReadFrameAsync(Channel<JsonElement> frames) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try {
            return await frames.Reader.ReadAsync(cts.Token);
        } catch (OperationCanceledException) {
            throw new Xunit.Sdk.XunitException("5s 内未收到变更帧（watcher 联动失效）");
        }
    }
}
