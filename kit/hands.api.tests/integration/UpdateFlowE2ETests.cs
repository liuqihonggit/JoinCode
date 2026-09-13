namespace Hands.Tests.Integration;

/// <summary>
/// 更新流程 E2E 集成测试 — 启动 UpdateServer + UpgradeService 完整更新链路
/// > ADR: 0064
/// </summary>
[Trait("Category", "Integration")]
public sealed class UpdateFlowE2ETests
{
    [Fact]
    public async Task FullUpdateFlow_ServerToDownload_Succeeds()
    {
        var fs = new IO.FileSystem.PhysicalFileSystem();
        var exeContent = "fake jcc exe content for e2e test"u8.ToArray();
        var sha256 = await ComputeSha256Async(exeContent);

        var contentRoot = fs.CombinePath(Path.GetTempPath(), $"update_e2e_{Guid.NewGuid():N}");
        var server = new UpdateServer(fs, port: 0, contentRoot: contentRoot);
        server.GenerateContent("999.0.0", sha256, exeContent);

        try
        {
            await server.StartAsync();
            await Task.Delay(500);

            var httpClient = new HttpClient();
            var manifestUrl = $"{server.Url}/manifest.json";
            var updateSource = new StaticFileUpdateSource(httpClient, manifestUrl);
            var service = new UpgradeService(httpClient, fs, updateSource: updateSource);

            var isAvailable = await service.IsUpdateAvailableAsync();
            isAvailable.Should().BeTrue("999.0.0 应大于当前版本");

            var entry = await service.GetUpdateEntryAsync();
            entry.Should().NotBeNull();
            entry!.Version.Should().Be("999.0.0");
            entry.Sha256.Should().Be(sha256);

            var result = await service.DownloadUpdateAsync(entry);
            result.Success.Should().BeTrue(result.ErrorMessage ?? "no error");
            result.DownloadedPath.Should().NotBeNull();
            result.RequiresRestart.Should().BeFalse();

            var downloadedContent = await fs.ReadAllBytesAsync(result.DownloadedPath!);
            downloadedContent.Should().Equal(exeContent);

            if (result.DownloadedPath is not null && fs.FileExists(result.DownloadedPath))
                fs.DeleteFile(result.DownloadedPath);
        }
        finally
        {
            await server.StopAsync();
            if (fs.DirectoryExists(contentRoot)) fs.DeleteDirectory(contentRoot, true);
        }
    }

    [Fact]
    public async Task FullUpdateFlow_WrongSha256_DownloadFails()
    {
        var fs = new IO.FileSystem.PhysicalFileSystem();
        var exeContent = "fake jcc exe content for sha256 mismatch"u8.ToArray();
        var wrongSha256 = "0000000000000000000000000000000000000000000000000000000000000000";

        var contentRoot = fs.CombinePath(Path.GetTempPath(), $"update_e2e_{Guid.NewGuid():N}");
        var server = new UpdateServer(fs, port: 0, contentRoot: contentRoot);
        server.GenerateContent("999.0.0", wrongSha256, exeContent);

        try
        {
            await server.StartAsync();
            await Task.Delay(500);

            var httpClient = new HttpClient();
            var manifestUrl = $"{server.Url}/manifest.json";
            var updateSource = new StaticFileUpdateSource(httpClient, manifestUrl);
            var service = new UpgradeService(httpClient, fs, updateSource: updateSource);

            var entry = await service.GetUpdateEntryAsync();
            entry.Should().NotBeNull();

            var result = await service.DownloadUpdateAsync(entry!);
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("SHA256");
        }
        finally
        {
            await server.StopAsync();
            if (fs.DirectoryExists(contentRoot)) fs.DeleteDirectory(contentRoot, true);
        }
    }

    [Fact]
    public async Task FullUpdateFlow_HealthCheck_ServerResponds()
    {
        var fs = new IO.FileSystem.PhysicalFileSystem();
        var contentRoot = fs.CombinePath(Path.GetTempPath(), $"update_e2e_{Guid.NewGuid():N}");
        var server = new UpdateServer(fs, port: 0, contentRoot: contentRoot);
        server.GenerateContent("1.0.0", "abc", "exe"u8.ToArray());

        try
        {
            await server.StartAsync();
            await Task.Delay(500);

            var response = await new HttpClient().GetAsync($"{server.Url}/health");
            response.IsSuccessStatusCode.Should().BeTrue();
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain("ok");
        }
        finally
        {
            await server.StopAsync();
            if (fs.DirectoryExists(contentRoot)) fs.DeleteDirectory(contentRoot, true);
        }
    }

    [Fact]
    public async Task FullUpdateFlow_ManifestEndpoint_ReturnsJson()
    {
        var fs = new IO.FileSystem.PhysicalFileSystem();
        var exeContent = "test exe"u8.ToArray();
        var sha256 = await ComputeSha256Async(exeContent);
        var contentRoot = fs.CombinePath(Path.GetTempPath(), $"update_e2e_{Guid.NewGuid():N}");
        var server = new UpdateServer(fs, port: 0, contentRoot: contentRoot);
        server.GenerateContent("2.0.0", sha256, exeContent);

        try
        {
            await server.StartAsync();
            await Task.Delay(500);

            var response = await new HttpClient().GetAsync($"{server.Url}/manifest.json");
            response.IsSuccessStatusCode.Should().BeTrue();
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

            var json = await response.Content.ReadAsStringAsync();
            var manifest = StaticFileUpdateSource.ParseManifest(json);
            manifest.LatestVersion.Should().Be("2.0.0");
            manifest.Releases.Should().HaveCount(1);
            manifest.Releases[0].Sha256.Should().Be(sha256);
        }
        finally
        {
            await server.StopAsync();
            if (fs.DirectoryExists(contentRoot)) fs.DeleteDirectory(contentRoot, true);
        }
    }

    private static async Task<string> ComputeSha256Async(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(new MemoryStream(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
