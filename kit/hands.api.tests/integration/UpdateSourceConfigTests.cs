namespace Hands.Tests.Integration;

/// <summary>
/// UpdateSourceConfig 单元测试 — 配置解析、环境变量回退
/// > ADR: 0064
/// </summary>
public sealed class UpdateSourceConfigTests
{
    [Fact]
    public void GetSourceType_ValidStatic_ReturnsStatic()
    {
        var config = new UpdateSourceConfig { SourceType = "static" };
        config.GetSourceType().Should().Be(UpdateSourceType.Static);
    }

    [Fact]
    public void GetSourceType_ValidApi_ReturnsHttpApi()
    {
        var config = new UpdateSourceConfig { SourceType = "api" };
        config.GetSourceType().Should().Be(UpdateSourceType.HttpApi);
    }

    [Fact]
    public void GetSourceType_ValidGitHubMirror_ReturnsGitHubMirror()
    {
        var config = new UpdateSourceConfig { SourceType = "github-mirror" };
        config.GetSourceType().Should().Be(UpdateSourceType.GitHubMirror);
    }

    [Fact]
    public void GetSourceType_ValidLocal_ReturnsLocalFile()
    {
        var config = new UpdateSourceConfig { SourceType = "local" };
        config.GetSourceType().Should().Be(UpdateSourceType.LocalFile);
    }

    [Fact]
    public void GetSourceType_Invalid_ReturnsStaticDefault()
    {
        var config = new UpdateSourceConfig { SourceType = "unknown" };
        config.GetSourceType().Should().Be(UpdateSourceType.Static);
    }

    [Fact]
    public void GetSourceType_Empty_ReturnsStaticDefault()
    {
        var config = new UpdateSourceConfig { SourceType = "" };
        config.GetSourceType().Should().Be(UpdateSourceType.Static);
    }

    [Fact]
    public void GetManifestUrl_ConfiguredValue_ReturnsConfigured()
    {
        var config = new UpdateSourceConfig { ManifestUrl = "https://my.server/manifest.json" };
        config.GetManifestUrl().Should().Be("https://my.server/manifest.json");
    }

    [Fact]
    public void GetManifestUrl_Null_FallsBackToResolver()
    {
        var config = new UpdateSourceConfig { ManifestUrl = null };
        var result = config.GetManifestUrl();
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetChannel_ConfiguredValue_ReturnsConfigured()
    {
        var config = new UpdateSourceConfig { Channel = "beta" };
        config.GetChannel().Should().Be("beta");
    }

    [Fact]
    public void GetChannel_Empty_FallsBackToResolver()
    {
        var config = new UpdateSourceConfig { Channel = "" };
        config.GetChannel().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new UpdateSourceConfig();
        config.SourceType.Should().Be("static");
        config.AutoUpdate.Should().BeFalse();
        config.CheckOnStartup.Should().BeTrue();
        config.CheckIntervalHours.Should().Be(24);
        config.Channel.Should().Be("stable");
    }
}
