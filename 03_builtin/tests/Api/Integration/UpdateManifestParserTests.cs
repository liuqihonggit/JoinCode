namespace Hands.Tests.Integration;

/// <summary>
/// StaticFileUpdateSource.ParseManifest 单元测试 — manifest.json 解析
/// > ADR: 0064
/// </summary>
public sealed class UpdateManifestParserTests
{
    [Fact]
    public void ParseManifest_ValidJson_ReturnsManifest()
    {
        var json = """
        {
          "latestVersion": "1.2.0",
          "channel": "stable",
          "releases": [
            {
              "version": "1.2.0",
              "downloadUrl": "releases/1.2.0/jcc.exe",
              "sha256": "abc123",
              "sizeBytes": 45000000,
              "releaseNotes": "test",
              "publishedAt": "2026-09-05T10:00:00Z"
            }
          ]
        }
        """;
        var manifest = StaticFileUpdateSource.ParseManifest(json);

        manifest.LatestVersion.Should().Be("1.2.0");
        manifest.Channel.Should().Be("stable");
        manifest.Releases.Should().HaveCount(1);
        manifest.Releases[0].Version.Should().Be("1.2.0");
        manifest.Releases[0].DownloadUrl.Should().Be("releases/1.2.0/jcc.exe");
        manifest.Releases[0].Sha256.Should().Be("abc123");
        manifest.Releases[0].SizeBytes.Should().Be(45000000);
    }

    [Fact]
    public void ParseManifest_MultipleReleases_ReturnsAll()
    {
        var json = """
        {
          "latestVersion": "2.0.0",
          "channel": "beta",
          "releases": [
            {"version": "2.0.0", "downloadUrl": "url2", "sha256": "hash2", "sizeBytes": 1001},
            {"version": "1.9.0", "downloadUrl": "url1", "sha256": "hash1", "sizeBytes": 1000}
          ]
        }
        """;
        var manifest = StaticFileUpdateSource.ParseManifest(json);

        manifest.Releases.Should().HaveCount(2);
        manifest.Releases[0].Version.Should().Be("2.0.0");
        manifest.Releases[1].Version.Should().Be("1.9.0");
    }

    [Fact]
    public void ParseManifest_NoReleases_ReturnsEmptyList()
    {
        var json = """{"latestVersion": "1.0.0", "channel": "stable", "releases": []}""";
        var manifest = StaticFileUpdateSource.ParseManifest(json);

        manifest.LatestVersion.Should().Be("1.0.0");
        manifest.Releases.Should().BeEmpty();
    }

    [Fact]
    public void ParseManifest_MissingChannel_DefaultsToStable()
    {
        var json = """{"latestVersion": "1.0.0", "releases": []}""";
        var manifest = StaticFileUpdateSource.ParseManifest(json);

        manifest.Channel.Should().Be("stable");
    }

    [Fact]
    public void ParseManifest_OptionalFields_NullWhenMissing()
    {
        var json = """
        {
          "latestVersion": "1.0.0",
          "releases": [{"version": "1.0.0", "downloadUrl": "url", "sha256": "hash"}]
        }
        """;
        var manifest = StaticFileUpdateSource.ParseManifest(json);

        manifest.Releases[0].ReleaseNotes.Should().BeNull();
        manifest.Releases[0].MinUpgradeFrom.Should().BeNull();
        manifest.Releases[0].SizeBytes.Should().Be(0);
    }
}
