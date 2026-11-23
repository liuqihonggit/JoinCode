// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
﻿namespace Dream.Tests.Client;

/// <summary>
/// 默认会话扫描器单元测试 — TASK071 服务域-① 布局修正锁：
/// 会话存于 SessionsDirectory/&lt;sessionId&gt;/transcript.jsonl（transcript.json 回退），
/// 旧实现扫顶层 *.json 恒 0 会话（autoDream 门控失效）。
/// </summary>
public sealed class DefaultSessionScannerTests {
    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task Constructor_NullConfig_UsesCurrentDirectory() {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        await using var scanner = new DefaultSessionScanner(null!, fs);

        Assert.Equal(fs.GetCurrentDirectory(), scanner.GetProjectDir());
    }

    [Fact]
    public async Task GetProjectDir_ReturnsConfigProjectDir() {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        await using var scanner = new DefaultSessionScanner(config, fs);

        Assert.Equal("/project", scanner.GetProjectDir());
    }

    [Fact]
    public async Task ListSessionsTouchedSinceAsync_NoSessionsDir_ReturnsEmpty() {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        await using var scanner = new DefaultSessionScanner(config, fs);

        var sessions = await scanner.ListSessionsTouchedSinceAsync(0).ConfigureAwait(true);

        Assert.Empty(sessions);
    }

    [Fact]
    public async Task ListSessionsTouchedSinceAsync_NewTranscriptJsonl_ReturnsSessionIds() {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        await using var scanner = new DefaultSessionScanner(config, fs);

        var sessionsDir = AppDataConstants.Paths.SessionsDirectory;
        WriteTranscript(fs, "session1", "jsonl");
        WriteTranscript(fs, "session2", "jsonl");

        var since = DateTime.UtcNow.AddHours(-1).Ticks / TimeSpan.TicksPerMillisecond;
        var sessions = await scanner.ListSessionsTouchedSinceAsync(since).ConfigureAwait(true);

        Assert.Equal(2, sessions.Count);
        Assert.Contains("session1", sessions);
        Assert.Contains("session2", sessions);
    }

    [Fact]
    public async Task ListSessionsTouchedSinceAsync_LegacyTranscriptJson_Fallback() {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        await using var scanner = new DefaultSessionScanner(config, fs);

        WriteTranscript(fs, "legacy-session", "json");

        var since = DateTime.UtcNow.AddHours(-1).Ticks / TimeSpan.TicksPerMillisecond;
        var sessions = await scanner.ListSessionsTouchedSinceAsync(since).ConfigureAwait(true);

        Assert.Contains("legacy-session", sessions);
    }

    [Fact]
    public async Task ListSessionsTouchedSinceAsync_OnlyOldTranscripts_ReturnsEmpty() {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        await using var scanner = new DefaultSessionScanner(config, fs);

        var sessionsDir = AppDataConstants.Paths.SessionsDirectory;
        WriteTranscript(fs, "old-session", "jsonl");
        fs.SetLastWriteTimeUtc($"{sessionsDir}/old-session/transcript.jsonl", DateTime.UtcNow.AddDays(-2));

        var since = DateTime.UtcNow.AddHours(-1).Ticks / TimeSpan.TicksPerMillisecond;
        var sessions = await scanner.ListSessionsTouchedSinceAsync(since).ConfigureAwait(true);

        Assert.Empty(sessions);
    }

    [Fact]
    public async Task ListSessionsTouchedSinceAsync_EmptyDirWithoutTranscript_Skipped() {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        await using var scanner = new DefaultSessionScanner(config, fs);

        // 无 transcript 的目录（如损坏会话）应跳过
        fs.CreateDirectory($"{AppDataConstants.Paths.SessionsDirectory}/broken-session");

        var since = DateTime.UtcNow.AddHours(-1).Ticks / TimeSpan.TicksPerMillisecond;
        var sessions = await scanner.ListSessionsTouchedSinceAsync(since).ConfigureAwait(true);

        Assert.Empty(sessions);
    }

    /// <summary>按新布局写会话转录（&lt;id&gt;/transcript.&lt;ext&gt;，mtime 取当前）。</summary>
    private static void WriteTranscript(Testing.Common.Services.InMemoryFileSystem fs, string sessionId, string ext) {
        var sessionsDir = AppDataConstants.Paths.SessionsDirectory;
        fs.WriteAllText($"{sessionsDir}/{sessionId}/transcript.{ext}", "{}");
    }
}
