namespace Hands.Tests.Integration;

/// <summary>
/// 三种 UpdateSource 实现的单元测试 — LocalFile / HttpApi / GitHubMirror
/// > ADR: 0064
/// </summary>
public sealed class UpdateSourceTests
{
    private readonly IFileSystem _fs = new IO.FileSystem.PhysicalFileSystem();

    [Fact]
    public void LocalFile_Type_ReturnsLocalFile()
    {
        var source = new LocalFileUpdateSource("/tmp/manifest.json", _fs);
        source.Type.Should().Be(UpdateSourceType.LocalFile);
    }

    [Fact]
    public async Task LocalFile_GetManifestAsync_NonExistentFile_ReturnsNull()
    {
        var source = new LocalFileUpdateSource("/nonexistent/manifest.json", _fs);
        var manifest = await source.GetManifestAsync();
        manifest.Should().BeNull();
    }

    [Fact]
    public async Task LocalFile_GetManifestAsync_ValidFile_ReturnsManifest()
    {
        var tempPath = _fs.CombinePath(Path.GetTempPath(), $"test_manifest_{Guid.NewGuid():N}.json");
        var json = """{"latestVersion":"1.5.0","channel":"beta","releases":[{"version":"1.5.0","downloadUrl":"jcc.exe","sha256":"abc","sizeBytes":100}]}""";
        await _fs.WriteAllTextAsync(tempPath, json);

        try
        {
            var source = new LocalFileUpdateSource(tempPath, _fs);
            var manifest = await source.GetManifestAsync();

            manifest.Should().NotBeNull();
            manifest!.LatestVersion.Should().Be("1.5.0");
            manifest.Channel.Should().Be("beta");
            manifest.Releases.Should().HaveCount(1);
        }
        finally
        {
            if (_fs.FileExists(tempPath)) _fs.DeleteFile(tempPath);
        }
    }

    [Fact]
    public async Task LocalFile_DownloadAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        var source = new LocalFileUpdateSource("/nonexistent/manifest.json", _fs);
        var entry = new UpdateManifestEntry
        {
            Version = "1.0.0",
            DownloadUrl = "/nonexistent/jcc.exe",
            Sha256 = "abc"
        };

        var act = () => source.DownloadAsync(entry);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task LocalFile_DownloadAsync_ExistingFile_ReturnsStream()
    {
        var tempDir = _fs.CombinePath(Path.GetTempPath(), $"test_update_{Guid.NewGuid():N}");
        _fs.CreateDirectory(tempDir);
        var manifestPath = _fs.CombinePath(tempDir, "manifest.json");
        var exePath = _fs.CombinePath(tempDir, "jcc.exe");
        await _fs.WriteAllTextAsync(manifestPath, """{"latestVersion":"1.0.0","releases":[]}""");
        await _fs.WriteAllTextAsync(exePath, "fake exe content");

        try
        {
            var source = new LocalFileUpdateSource(manifestPath, _fs);
            var entry = new UpdateManifestEntry
            {
                Version = "1.0.0",
                DownloadUrl = "jcc.exe",
                Sha256 = "abc"
            };

            var stream = await source.DownloadAsync(entry);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();
            content.Should().Be("fake exe content");
        }
        finally
        {
            if (_fs.DirectoryExists(tempDir)) _fs.DeleteDirectory(tempDir, true);
        }
    }

    [Fact]
    public void HttpApi_Type_ReturnsHttpApi()
    {
        var source = new HttpApiUpdateSource(new HttpClient(), "http://test/api");
        source.Type.Should().Be(UpdateSourceType.HttpApi);
    }

    [Fact]
    public async Task HttpApi_GetManifestAsync_ValidResponse_ReturnsManifest()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"latestVersion":"2.0.0","channel":"stable","releases":[{"version":"2.0.0","downloadUrl":"url","sha256":"hash","sizeBytes":200}]}""")
        });
        var source = new HttpApiUpdateSource(new HttpClient(handler), "http://test/api");

        var manifest = await source.GetManifestAsync();

        manifest.Should().NotBeNull();
        manifest!.LatestVersion.Should().Be("2.0.0");
        manifest.Releases.Should().HaveCount(1);
    }

    [Fact]
    public async Task HttpApi_GetManifestAsync_HttpError_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var source = new HttpApiUpdateSource(new HttpClient(handler), "http://test/api");

        var manifest = await source.GetManifestAsync();

        manifest.Should().BeNull();
    }

    [Fact]
    public async Task HttpApi_GetManifestAsync_NetworkError_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse(_ => throw new HttpRequestException("network"));
        var source = new HttpApiUpdateSource(new HttpClient(handler), "http://test/api");

        var manifest = await source.GetManifestAsync();

        manifest.Should().BeNull();
    }

    [Fact]
    public void GitHubMirror_Type_ReturnsGitHubMirror()
    {
        var source = new GitHubMirrorUpdateSource(new HttpClient(), "http://mirror/api");
        source.Type.Should().Be(UpdateSourceType.GitHubMirror);
    }

    [Fact]
    public async Task GitHubMirror_GetManifestAsync_ValidRelease_ReturnsManifest()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "tag_name": "v3.0.0",
              "published_at": "2026-09-05T10:00:00Z",
              "body": "Release notes here",
              "assets": [
                {"name": "jcc.exe", "browser_download_url": "https://mirror/releases/v3.0.0/jcc.exe", "size": 50000000},
                {"name": "jcc.pdb", "browser_download_url": "https://mirror/releases/v3.0.0/jcc.pdb", "size": 1000}
              ]
            }
            """)
        });
        var source = new GitHubMirrorUpdateSource(new HttpClient(handler), "http://mirror/api");

        var manifest = await source.GetManifestAsync();

        manifest.Should().NotBeNull();
        manifest!.LatestVersion.Should().Be("3.0.0");
        manifest.Channel.Should().Be("stable");
        manifest.Releases.Should().HaveCount(1);
        manifest.Releases[0].Version.Should().Be("3.0.0");
        manifest.Releases[0].DownloadUrl.Should().Be("https://mirror/releases/v3.0.0/jcc.exe");
        manifest.Releases[0].SizeBytes.Should().Be(50000000);
        manifest.Releases[0].ReleaseNotes.Should().Be("Release notes here");
    }

    [Fact]
    public async Task GitHubMirror_GetManifestAsync_NoVPrefix_StripsCorrectly()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name":"4.1.0","assets":[{"name":"jcc.exe","browser_download_url":"url","size":100}]}""")
        });
        var source = new GitHubMirrorUpdateSource(new HttpClient(handler), "http://mirror/api");

        var manifest = await source.GetManifestAsync();

        manifest.Should().NotBeNull();
        manifest!.LatestVersion.Should().Be("4.1.0");
    }

    [Fact]
    public async Task GitHubMirror_GetManifestAsync_NoExeAssets_ReturnsEmptyReleases()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name":"v1.0.0","assets":[{"name":"readme.txt","browser_download_url":"url","size":100}]}""")
        });
        var source = new GitHubMirrorUpdateSource(new HttpClient(handler), "http://mirror/api");

        var manifest = await source.GetManifestAsync();

        manifest.Should().NotBeNull();
        manifest!.Releases.Should().BeEmpty();
    }

    [Fact]
    public async Task GitHubMirror_GetManifestAsync_HttpError_ReturnsNull()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var source = new GitHubMirrorUpdateSource(new HttpClient(handler), "http://mirror/api");

        var manifest = await source.GetManifestAsync();

        manifest.Should().BeNull();
    }

    [Fact]
    public async Task GitHubMirror_GetManifestAsync_NoAssets_ReturnsEmptyReleases()
    {
        var handler = new FakeHttpMessageHandler();
        handler.SetResponse(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"tag_name":"v1.0.0"}""")
        });
        var source = new GitHubMirrorUpdateSource(new HttpClient(handler), "http://mirror/api");

        var manifest = await source.GetManifestAsync();

        manifest.Should().NotBeNull();
        manifest!.Releases.Should().BeEmpty();
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private Func<HttpRequestMessage, HttpResponseMessage>? _responseFactory;

        public void SetResponse(Func<HttpRequestMessage, HttpResponseMessage> factory)
        {
            _responseFactory = factory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = _responseFactory?.Invoke(request)
                ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            return Task.FromResult(response);
        }
    }
}
