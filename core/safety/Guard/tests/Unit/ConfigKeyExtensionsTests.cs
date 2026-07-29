namespace Host.Tests.ChatCommands;

/// <summary>
/// ConfigKey 枚举扩展方法测试 — 验证 EnumMetadata.Generator 产出正确
/// 覆盖:ToValue / FromValue / IsDefined / ConfigKeyConstants 常量值 / 大小写不敏感
/// 17 个枚举值 (Theme/EditorMode/Verbose/AutoCompactEnabled/AutoMemoryEnabled/FileCheckpointingEnabled/ShowTurnDuration/Model/AlwaysThinkingEnabled/PermissionsDefaultMode/Language/FastMode/EffortLevel/OutputStyle/Provider/Endpoint/ApiKey)
/// </summary>
public sealed class ConfigKeyExtensionsTests
{
    // ===== ToValue 测试 =====

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
    public void ToValue_Verbose_Should_Return_verbose()
    {
        ConfigKey.Verbose.ToValue().Should().Be("verbose");
    }

    [Fact]
    public void ToValue_AutoCompactEnabled_Should_Return_autoCompactEnabled()
    {
        ConfigKey.AutoCompactEnabled.ToValue().Should().Be("autoCompactEnabled");
    }

    [Fact]
    public void ToValue_AutoMemoryEnabled_Should_Return_autoMemoryEnabled()
    {
        ConfigKey.AutoMemoryEnabled.ToValue().Should().Be("autoMemoryEnabled");
    }

    [Fact]
    public void ToValue_FileCheckpointingEnabled_Should_Return_fileCheckpointingEnabled()
    {
        ConfigKey.FileCheckpointingEnabled.ToValue().Should().Be("fileCheckpointingEnabled");
    }

    [Fact]
    public void ToValue_ShowTurnDuration_Should_Return_showTurnDuration()
    {
        ConfigKey.ShowTurnDuration.ToValue().Should().Be("showTurnDuration");
    }

    [Fact]
    public void ToValue_Model_Should_Return_model()
    {
        ConfigKey.Model.ToValue().Should().Be("model");
    }

    [Fact]
    public void ToValue_AlwaysThinkingEnabled_Should_Return_alwaysThinkingEnabled()
    {
        ConfigKey.AlwaysThinkingEnabled.ToValue().Should().Be("alwaysThinkingEnabled");
    }

    [Fact]
    public void ToValue_PermissionsDefaultMode_Should_Return_permissions_defaultMode()
    {
        ConfigKey.PermissionsDefaultMode.ToValue().Should().Be("permissions.defaultMode");
    }

    [Fact]
    public void ToValue_Language_Should_Return_language()
    {
        ConfigKey.Language.ToValue().Should().Be("language");
    }

    [Fact]
    public void ToValue_FastMode_Should_Return_fastMode()
    {
        ConfigKey.FastMode.ToValue().Should().Be("fastMode");
    }

    [Fact]
    public void ToValue_EffortLevel_Should_Return_effortLevel()
    {
        ConfigKey.EffortLevel.ToValue().Should().Be("effortLevel");
    }

    [Fact]
    public void ToValue_OutputStyle_Should_Return_outputStyle()
    {
        ConfigKey.OutputStyle.ToValue().Should().Be("outputStyle");
    }

    [Fact]
    public void ToValue_Provider_Should_Return_provider()
    {
        ConfigKey.Provider.ToValue().Should().Be("provider");
    }

    [Fact]
    public void ToValue_Endpoint_Should_Return_endpoint()
    {
        ConfigKey.Endpoint.ToValue().Should().Be("endpoint");
    }

    [Fact]
    public void ToValue_ApiKey_Should_Return_apiKey()
    {
        ConfigKey.ApiKey.ToValue().Should().Be("apiKey");
    }

    // ===== FromValue 测试 =====

    [Theory]
    [InlineData("theme", ConfigKey.Theme)]
    [InlineData("editorMode", ConfigKey.EditorMode)]
    [InlineData("verbose", ConfigKey.Verbose)]
    [InlineData("autoCompactEnabled", ConfigKey.AutoCompactEnabled)]
    [InlineData("autoMemoryEnabled", ConfigKey.AutoMemoryEnabled)]
    [InlineData("fileCheckpointingEnabled", ConfigKey.FileCheckpointingEnabled)]
    [InlineData("showTurnDuration", ConfigKey.ShowTurnDuration)]
    [InlineData("model", ConfigKey.Model)]
    [InlineData("alwaysThinkingEnabled", ConfigKey.AlwaysThinkingEnabled)]
    [InlineData("permissions.defaultMode", ConfigKey.PermissionsDefaultMode)]
    [InlineData("language", ConfigKey.Language)]
    [InlineData("fastMode", ConfigKey.FastMode)]
    [InlineData("effortLevel", ConfigKey.EffortLevel)]
    [InlineData("outputStyle", ConfigKey.OutputStyle)]
    [InlineData("provider", ConfigKey.Provider)]
    [InlineData("endpoint", ConfigKey.Endpoint)]
    [InlineData("apiKey", ConfigKey.ApiKey)]
    public void FromValue_ValidString_Should_Return_CorrectEnum(string input, ConfigKey expected)
    {
        ConfigKeyExtensions.FromValue(input).Should().Be(expected);
    }

    // ===== 大小写不敏感测试 =====

    [Theory]
    [InlineData("THEME", ConfigKey.Theme)]
    [InlineData("Theme", ConfigKey.Theme)]
    [InlineData("EDITORMODE", ConfigKey.EditorMode)]
    [InlineData("Model", ConfigKey.Model)]
    [InlineData("PROVIDER", ConfigKey.Provider)]
    [InlineData("EFFORTLEVEL", ConfigKey.EffortLevel)]
    public void FromValue_CaseInsensitive_Should_Return_CorrectEnum(string input, ConfigKey expected)
    {
        ConfigKeyExtensions.FromValue(input).Should().Be(expected);
    }

    // ===== 无效输入测试 =====

    [Fact]
    public void FromValue_InvalidString_Should_Return_Null()
    {
        ConfigKeyExtensions.FromValue("invalid-key-name").Should().BeNull();
    }

    [Fact]
    public void FromValue_EmptyString_Should_Return_Null()
    {
        ConfigKeyExtensions.FromValue(string.Empty).Should().BeNull();
    }

    [Fact]
    public void FromValue_Null_Should_Return_Null()
    {
        ConfigKeyExtensions.FromValue(null).Should().BeNull();
    }

    // ===== IsDefined 测试 =====

    [Theory]
    [InlineData(ConfigKey.Theme, true)]
    [InlineData(ConfigKey.EditorMode, true)]
    [InlineData(ConfigKey.Verbose, true)]
    [InlineData(ConfigKey.AutoCompactEnabled, true)]
    [InlineData(ConfigKey.AutoMemoryEnabled, true)]
    [InlineData(ConfigKey.FileCheckpointingEnabled, true)]
    [InlineData(ConfigKey.ShowTurnDuration, true)]
    [InlineData(ConfigKey.Model, true)]
    [InlineData(ConfigKey.AlwaysThinkingEnabled, true)]
    [InlineData(ConfigKey.PermissionsDefaultMode, true)]
    [InlineData(ConfigKey.Language, true)]
    [InlineData(ConfigKey.FastMode, true)]
    [InlineData(ConfigKey.EffortLevel, true)]
    [InlineData(ConfigKey.OutputStyle, true)]
    [InlineData(ConfigKey.Provider, true)]
    [InlineData(ConfigKey.Endpoint, true)]
    [InlineData(ConfigKey.ApiKey, true)]
    public void IsDefined_AllValidValues_Should_Return_True(ConfigKey value, bool expected)
    {
        ConfigKeyExtensions.IsDefined(value).Should().Be(expected);
    }

    // ===== ConfigKeyConstants 测试 =====

    [Fact]
    public void Constants_Should_Match_ToValue_For_All_Values()
    {
        ConfigKeyConstants.Theme.Should().Be("theme");
        ConfigKeyConstants.EditorMode.Should().Be("editorMode");
        ConfigKeyConstants.Verbose.Should().Be("verbose");
        ConfigKeyConstants.AutoCompactEnabled.Should().Be("autoCompactEnabled");
        ConfigKeyConstants.AutoMemoryEnabled.Should().Be("autoMemoryEnabled");
        ConfigKeyConstants.FileCheckpointingEnabled.Should().Be("fileCheckpointingEnabled");
        ConfigKeyConstants.ShowTurnDuration.Should().Be("showTurnDuration");
        ConfigKeyConstants.Model.Should().Be("model");
        ConfigKeyConstants.AlwaysThinkingEnabled.Should().Be("alwaysThinkingEnabled");
        ConfigKeyConstants.PermissionsDefaultMode.Should().Be("permissions.defaultMode");
        ConfigKeyConstants.Language.Should().Be("language");
        ConfigKeyConstants.FastMode.Should().Be("fastMode");
        ConfigKeyConstants.EffortLevel.Should().Be("effortLevel");
        ConfigKeyConstants.OutputStyle.Should().Be("outputStyle");
        ConfigKeyConstants.Provider.Should().Be("provider");
        ConfigKeyConstants.Endpoint.Should().Be("endpoint");
        ConfigKeyConstants.ApiKey.Should().Be("apiKey");
    }

    // ===== RoundTrip 测试 =====

    [Theory]
    [InlineData(ConfigKey.Theme)]
    [InlineData(ConfigKey.EditorMode)]
    [InlineData(ConfigKey.Verbose)]
    [InlineData(ConfigKey.AutoCompactEnabled)]
    [InlineData(ConfigKey.AutoMemoryEnabled)]
    [InlineData(ConfigKey.FileCheckpointingEnabled)]
    [InlineData(ConfigKey.ShowTurnDuration)]
    [InlineData(ConfigKey.Model)]
    [InlineData(ConfigKey.AlwaysThinkingEnabled)]
    [InlineData(ConfigKey.PermissionsDefaultMode)]
    [InlineData(ConfigKey.Language)]
    [InlineData(ConfigKey.FastMode)]
    [InlineData(ConfigKey.EffortLevel)]
    [InlineData(ConfigKey.OutputStyle)]
    [InlineData(ConfigKey.Provider)]
    [InlineData(ConfigKey.Endpoint)]
    [InlineData(ConfigKey.ApiKey)]
    public void ToValue_FromValue_RoundTrip_Should_Be_Consistent(ConfigKey value)
    {
        var str = value.ToValue();
        ConfigKeyExtensions.FromValue(str).Should().Be(value);
    }
}
