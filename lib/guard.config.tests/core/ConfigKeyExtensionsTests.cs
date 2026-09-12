namespace Host.Tests.ChatCommands;

/// <summary>
/// ConfigKey 枚举扩展方法测试
/// 15 个枚举值 (Profile/Theme/EditorMode/DebugLog/AutoCompactEnabled/AutoMemoryEnabled/FileCheckpointingEnabled/ShowTurnDuration/AlwaysThinkingEnabled/PermissionsDefaultMode/Language/FastMode/EffortLevel/OutputStyle/ApiKey)
/// </summary>
public sealed class ConfigKeyExtensionsTests
{
    [Fact]
    public void ToValue_Profile_Should_Return_profile()
    {
        ConfigKey.Profile.ToValue().Should().Be("profile");
    }

    [Fact]
    public void ToValue_Theme_Should_Return_theme()
    {
        ConfigKey.Theme.ToValue().Should().Be("theme");
    }

    [Fact]
    public void ToValue_EditorMode_Should_Return_editorMode()
    {
        ConfigKey.EditorMode.ToValue().Should().Be("editorMode");
    }

    [Fact]
    public void ToValue_DebugLog_Should_Return_debuglog()
    {
        ConfigKey.DebugLog.ToValue().Should().Be("debuglog");
    }

    [Fact]
    public void ToValue_EffortLevel_Should_Return_effortLevel()
    {
        ConfigKey.EffortLevel.ToValue().Should().Be("effortLevel");
    }

    [Fact]
    public void ToValue_ApiKey_Should_Return_apiKey()
    {
        ConfigKey.ApiKey.ToValue().Should().Be("apiKey");
    }

    [Theory]
    [InlineData("profile", ConfigKey.Profile)]
    [InlineData("theme", ConfigKey.Theme)]
    [InlineData("editorMode", ConfigKey.EditorMode)]
    [InlineData("debuglog", ConfigKey.DebugLog)]
    [InlineData("effortLevel", ConfigKey.EffortLevel)]
    [InlineData("apiKey", ConfigKey.ApiKey)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, ConfigKey expected)
    {
        ConfigKeyExtensions.FromValue(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("THEME", ConfigKey.Theme)]
    [InlineData("Theme", ConfigKey.Theme)]
    [InlineData("PROFILE", ConfigKey.Profile)]
    [InlineData("Profile", ConfigKey.Profile)]
    public void FromValue_CaseInsensitive_Should_Return_CorrectEnum(string input, ConfigKey expected)
    {
        ConfigKeyExtensions.FromValue(input).Should().Be(expected);
    }

    [Fact]
    public void FromValue_InvalidString_Should_Return_Null()
    {
        ConfigKeyExtensions.FromValue("invalid-key-name").Should().BeNull();
    }

    [Fact]
    public void FromValue_Null_Should_Return_Null()
    {
        ConfigKeyExtensions.FromValue(null).Should().BeNull();
    }

    [Fact]
    public void Constants_Profile_Should_Be_profile()
    {
        ConfigKeyConstants.Profile.Should().Be("profile");
    }

    [Fact]
    public void Constants_Theme_Should_Be_theme()
    {
        ConfigKeyConstants.Theme.Should().Be("theme");
    }

    [Theory]
    [InlineData(ConfigKey.Profile)]
    [InlineData(ConfigKey.Theme)]
    [InlineData(ConfigKey.EditorMode)]
    [InlineData(ConfigKey.DebugLog)]
    [InlineData(ConfigKey.EffortLevel)]
    [InlineData(ConfigKey.ApiKey)]
    public void ToValue_FromValue_RoundTrip_Should_Be_Consistent(ConfigKey value)
    {
        var str = value.ToValue();
        ConfigKeyExtensions.FromValue(str).Should().Be(value);
    }
}
