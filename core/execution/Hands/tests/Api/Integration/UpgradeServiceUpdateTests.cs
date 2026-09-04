namespace Hands.Tests.Integration;

/// <summary>
/// UpgradeService 下载/SHA256/应用更新单元测试
/// > ADR: 0064
/// </summary>
public sealed class UpgradeServiceUpdateTests
{
    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fs;

    public UpgradeServiceUpdateTests()
    {
        _httpClient = new HttpClient();
        _fs = new IO.FileSystem.PhysicalFileSystem();
    }

    [Fact]
    public async Task GetLatestVersionAsync_WithUpdateSource_UsesManifestInsteadOfGitHub()
    {
        var manifest = new UpdateManifest
        {
            LatestVersion = "3.5.7",
            Channel = "stable",
            Releases = []
        };
        var source = new MockUpdateSource(manifest);
        var service = new UpgradeService(_httpClient, _fs, updateSource: source);

        var version = await service.GetLatestVersionAsync();

        version.Should().Be(new Version(3, 5, 7));
    }

    [Fact]
    public async Task GetUpdateEntryAsync_NoUpdateSource_ReturnsNull()
    {
        var service = new UpgradeService(_httpClient, _fs);

        var entry = await service.GetUpdateEntryAsync();

        entry.Should().BeNull();
    }

    [Fact]
    public async Task GetUpdateEntryAsync_NewerVersionExists_ReturnsEntry()
    {
        var manifest = new UpdateManifest
        {
            LatestVersion = "999.0.0",
            Channel = "stable",
            Releases =
            [
                new UpdateManifestEntry
                {
                    Version = "999.0.0",
                    DownloadUrl = "http://test/jcc.exe",
                    Sha256 = "abc",
                    SizeBytes = 100
                }
            ]
        };
        var source = new MockUpdateSource(manifest);
        var service = new UpgradeService(_httpClient, _fs, updateSource: source);

        var entry = await service.GetUpdateEntryAsync();

        entry.Should().NotBeNull();
        entry!.Version.Should().Be("999.0.0");
    }

    [Fact]
    public async Task GetUpdateEntryAsync_NoNewerVersion_ReturnsNull()
    {
        var manifest = new UpdateManifest
        {
            LatestVersion = "0.0.1",
            Channel = "stable",
            Releases =
            [
                new UpdateManifestEntry
                {
                    Version = "0.0.1",
                    DownloadUrl = "http://test/jcc.exe",
                    Sha256 = "abc",
                    SizeBytes = 100
                }
            ]
        };
        var source = new MockUpdateSource(manifest);
        var service = new UpgradeService(_httpClient, _fs, updateSource: source);

        var entry = await service.GetUpdateEntryAsync();

        entry.Should().BeNull();
    }

    [Fact]
    public async Task GetUpdateEntryAsync_EmptyReleases_ReturnsNull()
    {
        var manifest = new UpdateManifest
        {
            LatestVersion = "1.0.0",
            Channel = "stable",
            Releases = []
        };
        var source = new MockUpdateSource(manifest);
        var service = new UpgradeService(_httpClient, _fs, updateSource: source);

        var entry = await service.GetUpdateEntryAsync();

        entry.Should().BeNull();
    }

    [Fact]
    public async Task DownloadUpdateAsync_NoUpdateSource_ReturnsFailed()
    {
        var service = new UpgradeService(_httpClient, _fs);
        var entry = new UpdateManifestEntry
        {
            Version = "1.0.0",
            DownloadUrl = "http://test/jcc.exe",
            Sha256 = "abc",
            SizeBytes = 100
        };

        var result = await service.DownloadUpdateAsync(entry);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("IUpdateSource");
    }

    [Fact]
    public async Task DownloadUpdateAsync_CorrectSha256_ReturnsSuccess()
    {
        var content = "Hello Update World!"u8.ToArray();
        var sha256 = await ComputeSha256Async(content);
        var manifest = new UpdateManifest
        {
            LatestVersion = "999.0.0",
            Channel = "stable",
            Releases = []
        };
        var source = new MockUpdateSource(manifest, content);
        var service = new UpgradeService(_httpClient, _fs, updateSource: source);
        var entry = new UpdateManifestEntry
        {
            Version = "999.0.0",
            DownloadUrl = "http://test/jcc.exe",
            Sha256 = sha256,
            SizeBytes = content.Length
        };

        var result = await service.DownloadUpdateAsync(entry);

        result.Success.Should().BeTrue();
        result.DownloadedPath.Should().NotBeNull();
        result.RequiresRestart.Should().BeFalse();

        if (result.DownloadedPath is not null && _fs.FileExists(result.DownloadedPath))
            _fs.DeleteFile(result.DownloadedPath);
    }

    [Fact]
    public async Task DownloadUpdateAsync_WrongSha256_ReturnsFailed()
    {
        var content = "Hello Update World!"u8.ToArray();
        var manifest = new UpdateManifest
        {
            LatestVersion = "999.0.0",
            Channel = "stable",
            Releases = []
        };
        var source = new MockUpdateSource(manifest, content);
        var service = new UpgradeService(_httpClient, _fs, updateSource: source);
        var entry = new UpdateManifestEntry
        {
            Version = "999.0.0",
            DownloadUrl = "http://test/jcc.exe",
            Sha256 = "0000000000000000000000000000000000000000000000000000000000000000",
            SizeBytes = content.Length
        };

        var result = await service.DownloadUpdateAsync(entry);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SHA256");
    }

    [Fact]
    public async Task DownloadUpdateAsync_WithProgress_ReportsProgress()
    {
        var content = new byte[81920 * 3 + 1000];
        Random.Shared.NextBytes(content);
        var sha256 = await ComputeSha256Async(content);
        var manifest = new UpdateManifest
        {
            LatestVersion = "999.0.0",
            Channel = "stable",
            Releases = []
        };
        var source = new MockUpdateSource(manifest, content);
        var service = new UpgradeService(_httpClient, _fs, updateSource: source);
        var entry = new UpdateManifestEntry
        {
            Version = "999.0.0",
            DownloadUrl = "http://test/jcc.exe",
            Sha256 = sha256,
            SizeBytes = content.Length
        };
        var progressReports = new List<UpdateDownloadProgress>();
        var progress = new Progress<UpdateDownloadProgress>(p => progressReports.Add(p));

        var result = await service.DownloadUpdateAsync(entry, progress);

        result.Success.Should().BeTrue();
        progressReports.Should().NotBeEmpty();
        progressReports[^1].BytesDownloaded.Should().Be(content.Length);

        if (result.DownloadedPath is not null && _fs.FileExists(result.DownloadedPath))
            _fs.DeleteFile(result.DownloadedPath);
    }

    [Fact]
    public async Task ApplyUpdateAsync_NonExistentFile_ReturnsFailed()
    {
        var service = new UpgradeService(_httpClient, _fs);

        var result = await service.ApplyUpdateAsync("/nonexistent/path/jcc.exe.new");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不存在");
    }

    private static async Task<string> ComputeSha256Async(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(new MemoryStream(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class MockUpdateSource : IUpdateSource
    {
        private readonly UpdateManifest _manifest;
        private readonly byte[]? _downloadContent;

        public MockUpdateSource(UpdateManifest manifest, byte[]? downloadContent = null)
        {
            _manifest = manifest;
            _downloadContent = downloadContent;
        }

        public UpdateSourceType Type => UpdateSourceType.Static;

        public Task<UpdateManifest?> GetManifestAsync(CancellationToken ct = default)
            => Task.FromResult<UpdateManifest?>(_manifest);

        public Task<Stream> DownloadAsync(
            UpdateManifestEntry entry,
            IProgress<UpdateDownloadProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (_downloadContent is null)
                throw new InvalidOperationException("MockUpdateSource: 未设置下载内容");
            return Task.FromResult<Stream>(new MemoryStream(_downloadContent));
        }
    }
}
