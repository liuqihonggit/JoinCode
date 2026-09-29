namespace Abs.Tests.Configuration;

/// <summary>
/// SettingsEditValidator 确定性单元测试 — 覆盖 ValidateSettingsContent(internal) + ValidateEdit(public) + IsJccSettingsPath(public)。
/// 纯 JSON 结构验证,不依赖时序/IO。
/// </summary>
public sealed class SettingsEditValidatorTests {

    // ── ValidateSettingsContent: 内容验证 ──

    [Fact]
    public void Validate_ValidJson_ReturnsValid() {
        var r = SettingsEditValidator.ValidateSettingsContent("""{"model":"gpt-4"}""");
        r.IsValid.Should().BeTrue();
        r.Error.Should().BeNull();
    }

    [Fact]
    public void Validate_EmptyContent_ReturnsInvalid() {
        var r = SettingsEditValidator.ValidateSettingsContent("");
        r.IsValid.Should().BeFalse();
        r.Error.Should().Contain("空");
    }

    [Fact]
    public void Validate_WhitespaceOnly_ReturnsInvalid() {
        var r = SettingsEditValidator.ValidateSettingsContent("   ");
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_InvalidJson_ReturnsInvalid() {
        var r = SettingsEditValidator.ValidateSettingsContent("not json at all");
        r.IsValid.Should().BeFalse();
        r.Error.Should().Contain("JSON");
    }

    [Fact]
    public void Validate_RootNotObject_ReturnsInvalid() {
        var r = SettingsEditValidator.ValidateSettingsContent("[1,2,3]");
        r.IsValid.Should().BeFalse();
        r.Error.Should().Contain("根元素");
    }

    [Fact]
    public void Validate_ModelNotString_ReturnsInvalid() {
        var r = SettingsEditValidator.ValidateSettingsContent("""{"model":1}""");
        r.IsValid.Should().BeFalse();
        r.Error.Should().Contain("model");
    }

    [Fact]
    public void Validate_EnvValueNotString_ReturnsInvalid() {
        var r = SettingsEditValidator.ValidateSettingsContent("""{"env":{"A":1}}""");
        r.IsValid.Should().BeFalse();
        r.Error.Should().Contain("env.A");
    }

    [Fact]
    public void Validate_EnvValueString_ReturnsValid() {
        var r = SettingsEditValidator.ValidateSettingsContent("""{"env":{"A":"value"}}""");
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_PermissionsDefaultModeInvalid_ReturnsInvalid() {
        var r = SettingsEditValidator.ValidateSettingsContent("""{"permissions":{"defaultMode":"xxx"}}""");
        r.IsValid.Should().BeFalse();
        r.Error.Should().Contain("defaultMode");
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("auto")]
    [InlineData("ask")]
    [InlineData("bypass")]
    [InlineData("unattended")]
    public void Validate_PermissionsDefaultModeValid_ReturnsValid(string mode) {
        var r = SettingsEditValidator.ValidateSettingsContent("{\"permissions\":{\"defaultMode\":\"" + mode + "\"}}");
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AvailableModelsNotArray_ReturnsInvalid() {
        var r = SettingsEditValidator.ValidateSettingsContent("""{"availableModels":"x"}""");
        r.IsValid.Should().BeFalse();
        r.Error.Should().Contain("availableModels");
    }

    [Fact]
    public void Validate_AvailableModelsArray_ReturnsValid() {
        var r = SettingsEditValidator.ValidateSettingsContent("""{"availableModels":["a","b"]}""");
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_CleanupPeriodDaysNotNumber_ReturnsInvalid() {
        var r = SettingsEditValidator.ValidateSettingsContent("""{"cleanupPeriodDays":"x"}""");
        r.IsValid.Should().BeFalse();
        r.Error.Should().Contain("cleanupPeriodDays");
    }

    [Fact]
    public void Validate_StrictPluginNotBool_ReturnsInvalid() {
        var r = SettingsEditValidator.ValidateSettingsContent("""{"strictPluginOnlyCustomization":"x"}""");
        r.IsValid.Should().BeFalse();
        r.Error.Should().Contain("strictPluginOnlyCustomization");
    }

    [Fact]
    public void Validate_ComplexValid_ReturnsValid() {
        var json = """
        {
            "model": "gpt-4",
            "env": { "API_KEY": "xxx" },
            "permissions": { "defaultMode": "plan" },
            "availableModels": ["a", "b"],
            "cleanupPeriodDays": 30,
            "strictPluginOnlyCustomization": true
        }
        """;
        var r = SettingsEditValidator.ValidateSettingsContent(json);
        r.IsValid.Should().BeTrue();
    }

    // ── IsJccSettingsPath: 路径识别 ──

    [Theory]
    [InlineData("C:\\repo\\.jcc\\settings.json")]
    [InlineData("C:\\repo\\.jcc\\settings.local.json")]
    [InlineData("/home/u/repo/.jcc/settings.json")]
    [InlineData("/home/u/repo/.jcc/settings.local.json")]
    public void IsJccSettingsPath_ValidPaths_ReturnsTrue(string path) {
        SettingsEditValidator.IsJccSettingsPath(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("C:\\repo\\settings.json")]
    [InlineData("C:\\repo\\.jcc\\other.json")]
    [InlineData("")]
    public void IsJccSettingsPath_InvalidPaths_ReturnsFalse(string path) {
        SettingsEditValidator.IsJccSettingsPath(path).Should().BeFalse();
    }

    // ── ValidateEdit: 编辑校验 ──

    [Fact]
    public void ValidateEdit_NonSettingsPath_ReturnsNull() {
        // 非 settings 路径 → 不校验,返回 null(允许)
        var r = SettingsEditValidator.ValidateEdit("C:\\repo\\other.json", """{"a":1}""", "broken");
        r.Should().BeNull();
    }

    [Fact]
    public void ValidateEdit_ValidToValid_ReturnsNull() {
        var path = "C:\\repo\\.jcc\\settings.json";
        var r = SettingsEditValidator.ValidateEdit(path, """{"model":"a"}""", """{"model":"b"}""");
        r.Should().BeNull();
    }

    [Fact]
    public void ValidateEdit_ValidToInvalid_ReturnsError() {
        var path = "C:\\repo\\.jcc\\settings.json";
        var r = SettingsEditValidator.ValidateEdit(path, """{"model":"a"}""", "broken json");
        r.Should().NotBeNull();
        r.Should().Contain("验证失败");
    }

    [Fact]
    public void ValidateEdit_InvalidToValid_ReturnsNull() {
        // 鼓励修复: 编辑前无效 → 允许编辑
        var path = "C:\\repo\\.jcc\\settings.json";
        var r = SettingsEditValidator.ValidateEdit(path, "broken", """{"model":"a"}""");
        r.Should().BeNull();
    }
}
