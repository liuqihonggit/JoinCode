namespace Dream.Tests.Client;

/// <summary>
/// 默认会话扫描器单元测试
/// </summary>
public sealed class DefaultSessionScannerTests
{
    [Fact]
    public void Constructor_NullConfig_UsesCurrentDirectory()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();

        var scanner = new DefaultSessionScanner(null!, fs);

        Assert.Equal(fs.GetCurrentDirectory(), scanner.GetProjectDir());
    }

    [Fact]
    public void GetProjectDir_ReturnsConfigProjectDir()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };

        var scanner = new DefaultSessionScanner(config, fs);

        Assert.Equal("/project", scanner.GetProjectDir());
    }

    [Fact]
    public async Task ListSessionsTouchedSinceAsync_NoSessionsDir_ReturnsEmpty()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        var scanner = new DefaultSessionScanner(config, fs);

        var sessions = await scanner.ListSessionsTouchedSinceAsync(0).ConfigureAwait(true);

        Assert.Empty(sessions);
    }

    [Fact]
    public async Task ListSessionsTouchedSinceAsync_WithNewFiles_ReturnsSessionIds()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        var scanner = new DefaultSessionScanner(config, fs);

        var sessionsDir = "/project/.jcc/sessions";
        fs.WriteAllText($"{sessionsDir}/session1.json", "data");
        fs.WriteAllText($"{sessionsDir}/session2.json", "data");
        fs.SetLastWriteTimeUtc($"{sessionsDir}/session1.json", DateTime.UtcNow.AddHours(1));
        fs.SetLastWriteTimeUtc($"{sessionsDir}/session2.json", DateTime.UtcNow.AddHours(1));

        var since = DateTime.UtcNow.AddHours(-1).Ticks / TimeSpan.TicksPerMillisecond;
        var sessions = await scanner.ListSessionsTouchedSinceAsync(since).ConfigureAwait(true);

        Assert.Equal(2, sessions.Count);
        Assert.Contains("session1", sessions);
        Assert.Contains("session2", sessions);
    }

    [Fact]
    public async Task ListSessionsTouchedSinceAsync_WithOnlyOldFiles_ReturnsEmpty()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        var scanner = new DefaultSessionScanner(config, fs);

        var sessionsDir = "/project/.jcc/sessions";
        fs.WriteAllText($"{sessionsDir}/session1.json", "data");
        fs.SetLastWriteTimeUtc($"{sessionsDir}/session1.json", DateTime.UtcNow.AddDays(-2));

        var since = DateTime.UtcNow.AddHours(-1).Ticks / TimeSpan.TicksPerMillisecond;
        var sessions = await scanner.ListSessionsTouchedSinceAsync(since).ConfigureAwait(true);

        Assert.Empty(sessions);
    }

    [Fact]
    public async Task ListSessionsTouchedSinceAsync_IgnoresNonJsonlFiles()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        var scanner = new DefaultSessionScanner(config, fs);

        var sessionsDir = "/project/.jcc/sessions";
        fs.WriteAllText($"{sessionsDir}/session1.json", "data");
        fs.WriteAllText($"{sessionsDir}/notes.txt", "data");
        fs.SetLastWriteTimeUtc($"{sessionsDir}/session1.json", DateTime.UtcNow.AddHours(1));
        fs.SetLastWriteTimeUtc($"{sessionsDir}/notes.txt", DateTime.UtcNow.AddHours(1));

        var since = DateTime.UtcNow.AddHours(-1).Ticks / TimeSpan.TicksPerMillisecond;
        var sessions = await scanner.ListSessionsTouchedSinceAsync(since).ConfigureAwait(true);

        Assert.Single(sessions);
        Assert.Contains("session1", sessions);
    }

    [Fact]
    public async Task ListSessionsTouchedSinceAsync_SkipsEmptySessionIdFile()
    {
        var fs = new Testing.Common.Services.InMemoryFileSystem();
        var config = new AutoDreamConfig { ProjectDir = "/project" };
        var scanner = new DefaultSessionScanner(config, fs);

        var sessionsDir = "/project/.jcc/sessions";
        fs.WriteAllText($"{sessionsDir}/.json", "data");
        fs.SetLastWriteTimeUtc($"{sessionsDir}/.json", DateTime.UtcNow.AddHours(1));

        var since = DateTime.UtcNow.AddHours(-1).Ticks / TimeSpan.TicksPerMillisecond;
        var sessions = await scanner.ListSessionsTouchedSinceAsync(since).ConfigureAwait(true);

        Assert.Empty(sessions);
    }
}
