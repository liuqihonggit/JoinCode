using JoinCode.Abstractions.Configuration.Settings;

namespace Infrastructure.Tests.Services;

public sealed class SettingsEditValidatorTests
{
    #region IsJccSettingsPath

    [Fact]
    public void IsJccSettingsPath_SettingsJson_ReturnsTrue()
    {
        SettingsEditValidator.IsJccSettingsPath("/home/user/.jcc/settings.json").Should().BeTrue();
    }

    [Fact]
    public void IsJccSettingsPath_SettingsLocalJson_ReturnsTrue()
    {
        SettingsEditValidator.IsJccSettingsPath("/home/user/.jcc/settings.local.json").Should().BeTrue();
    }

    [Fact]
    public void IsJccSettingsPath_WindowsPath_ReturnsTrue()
    {
        SettingsEditValidator.IsJccSettingsPath("C:\\Users\\user\\.jcc\\settings.json").Should().BeTrue();
    }

    [Fact]
    public void IsJccSettingsPath_CaseInsensitive_ReturnsTrue()
    {
        SettingsEditValidator.IsJccSettingsPath("/home/user/.JCC/Settings.json").Should().BeTrue();
    }

    [Fact]
    public void IsJccSettingsPath_OtherFile_ReturnsFalse()
    {
        SettingsEditValidator.IsJccSettingsPath("/home/user/config.json").Should().BeFalse();
    }

    [Fact]
    public void IsJccSettingsPath_NullOrEmpty_ReturnsFalse()
    {
        SettingsEditValidator.IsJccSettingsPath(null!).Should().BeFalse();
        SettingsEditValidator.IsJccSettingsPath("").Should().BeFalse();
    }

    #endregion

    #region ValidateEdit

    [Fact]
    public void ValidateEdit_NonSettingsFile_AllowsEdit()
    {
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/config.json",
            "{\"key\": \"value\"}",
            "{\"key\": \"new_value\"}");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateEdit_ValidToValid_AllowsEdit()
    {
        var original = "{\"model\": \"gpt-4o\"}";
        var updated = "{\"model\": \"claude-3\"}";
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/.jcc/settings.json",
            original, updated);
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateEdit_InvalidToAnything_AllowsEdit()
    {
        // 对齐 TS: 如果编辑前内容无效，允许编辑（鼓励修复）
        var original = "not valid json {{{";
        var updated = "{\"model\": \"gpt-4o\"}";
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/.jcc/settings.json",
            original, updated);
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateEdit_ValidToInvalid_RejectsEdit()
    {
        // 对齐 TS: 只阻止"从合法变非法"的降级编辑
        var original = "{\"model\": \"gpt-4o\"}";
        var updated = "broken json {{{";
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/.jcc/settings.json",
            original, updated);
        result.Should().NotBeNull();
        result.Should().Contain("验证失败");
    }

    [Fact]
    public void ValidateEdit_ValidToInvalidType_RejectsEdit()
    {
        // model 字段从字符串改为数字
        var original = "{\"model\": \"gpt-4o\"}";
        var updated = "{\"model\": 123}";
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/.jcc/settings.json",
            original, updated);
        result.Should().NotBeNull();
        result.Should().Contain("model");
    }

    [Fact]
    public void ValidateEdit_InvalidDefaultMode_RejectsEdit()
    {
        var original = "{\"permissions\": {\"defaultMode\": \"default\"}}";
        var updated = "{\"permissions\": {\"defaultMode\": \"invalid_mode\"}}";
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/.jcc/settings.json",
            original, updated);
        result.Should().NotBeNull();
        result.Should().Contain("defaultMode");
    }

    [Fact]
    public void ValidateEdit_ValidDefaultMode_AllowsEdit()
    {
        var original = "{\"permissions\": {\"defaultMode\": \"default\"}}";
        var updated = "{\"permissions\": {\"defaultMode\": \"acceptEdits\"}}";
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/.jcc/settings.json",
            original, updated);
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateEdit_EnvNonStringValue_RejectsEdit()
    {
        var original = "{\"env\": {\"KEY\": \"value\"}}";
        var updated = "{\"env\": {\"KEY\": 123}}";
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/.jcc/settings.json",
            original, updated);
        result.Should().NotBeNull();
        result.Should().Contain("env.KEY");
    }

    [Fact]
    public void ValidateEdit_EmptyContent_InvalidBefore_AllowsEdit()
    {
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/.jcc/settings.json",
            "", "{\"model\": \"gpt-4o\"}");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateEdit_RootNotObject_InvalidBefore_AllowsEdit()
    {
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/.jcc/settings.json",
            "[1,2,3]", "{\"model\": \"gpt-4o\"}");
        result.Should().BeNull();
    }

    [Fact]
    public void ValidateEdit_ValidToValidComplex_AllowsEdit()
    {
        var original = "{\"model\": \"gpt-4o\", \"env\": {\"KEY\": \"val\"}, \"permissions\": {\"defaultMode\": \"default\"}}";
        var updated = "{\"model\": \"claude-3\", \"env\": {\"KEY\": \"new_val\"}, \"permissions\": {\"defaultMode\": \"plan\"}}";
        var result = SettingsEditValidator.ValidateEdit(
            "/home/user/.jcc/settings.json",
            original, updated);
        result.Should().BeNull();
    }

    #endregion
}
