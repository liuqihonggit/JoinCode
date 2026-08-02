namespace Core.Tests.ToolHandlers;

public sealed class SupportedSettingsTests
{
    [Theory]
    [InlineData("theme", true)]
    [InlineData("model", true)]
    [InlineData("unknown", false)]
    public void IsSupported_ShouldReturnExpected(string key, bool expected)
    {
        SupportedSettings.IsSupported(key).Should().Be(expected);
    }

    [Theory]
    [InlineData("theme")]
    [InlineData("model")]
    [InlineData("permissions.defaultMode")]
    public void GetConfig_ExistingKeys_ShouldReturnConfig(string key)
    {
        var config = SupportedSettings.GetConfig(key);

        config.Should().NotBeNull();
        config!.Type.Should().NotBeNullOrEmpty();
        config.Description.Should().NotBeNullOrEmpty();
        config.Source.Should().BeOneOf("global", "settings");
    }

    [Fact]
    public void GetConfig_UnknownKey_ShouldReturnNull()
    {
        var config = SupportedSettings.GetConfig("not-a-real-key");

        config.Should().BeNull();
    }

    [Fact]
    public void GetAllKeys_ShouldReturnAllSupportedKeys()
    {
        var keys = SupportedSettings.GetAllKeys();

        keys.Should().Contain("theme");
        keys.Should().Contain("model");
        keys.Should().Contain("permissions.defaultMode");
    }

    [Fact]
    public void GetOptionsForSetting_WithStaticOptions_ShouldReturnOptions()
    {
        var options = SupportedSettings.GetOptionsForSetting("theme");

        options.Should().NotBeNull();
        options.Should().Contain("dark");
        options.Should().Contain("light");
    }

    [Fact]
    public void GetOptionsForSetting_WithDynamicOptions_ShouldReturnOptions()
    {
        var options = SupportedSettings.GetOptionsForSetting("model");

        options.Should().NotBeNull();
        options.Should().Contain("sonnet");
    }

    [Fact]
    public void GetOptionsForSetting_WithoutOptions_ShouldReturnNull()
    {
        var options = SupportedSettings.GetOptionsForSetting("language");

        options.Should().BeNull();
    }

    [Fact]
    public void GetOptionsForSetting_UnknownKey_ShouldReturnNull()
    {
        var options = SupportedSettings.GetOptionsForSetting("unknown");

        options.Should().BeNull();
    }

    [Fact]
    public void GetPath_WithExplicitPath_ShouldReturnPath()
    {
        var path = SupportedSettings.GetPath("permissions.defaultMode");

        path.Should().ContainInOrder("permissions", "defaultMode");
    }

    [Fact]
    public void GetPath_WithoutExplicitPath_ShouldSplitKey()
    {
        var path = SupportedSettings.GetPath("autoCompactEnabled");

        path.Should().ContainSingle("autoCompactEnabled");
    }

    [Fact]
    public void All_ShouldContainExpectedSettings()
    {
        SupportedSettings.All.Should().ContainKey("theme");
        SupportedSettings.All.Should().ContainKey("model");
        SupportedSettings.All["theme"].Type.Should().Be("string");
        SupportedSettings.All["verbose"].Type.Should().Be("boolean");
    }
}
