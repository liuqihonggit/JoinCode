
namespace Core.Tests.Configuration;

/// <summary>
/// 嵌套键读写（TASK090）— <c>model</c> 住在 <c>vendor.{profile}.model</c>，
/// <c>permissions.defaultMode</c> 住在 <c>current.permissions.defaultMode</c>，
/// 两者都不是扁平键。原实现按扁平键读写，于是「改了不生效、读了说未设置」。
/// <para>与其它配置测试共享 <c>AppDataConstants</c> 全局状态，必须串行。</para>
/// </summary>
[Collection("AppDataConstantsCollection")]
public class SettingPathMapTests : IDisposable {
    private readonly string? _originalAppDataFolder;
    private readonly string _tempAppDataDir;
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private bool _disposed;

    /// <summary>准备隔离的 AppData 目录 — 不碰用户真实 ~/.jcc。</summary>
    public SettingPathMapTests() {
        _originalAppDataFolder = Environment.GetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder);

        _tempAppDataDir = $"/test/jcc-test-pathmap-{Guid.NewGuid():N}";
        _fs.CreateDirectory(_tempAppDataDir);
        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder, _tempAppDataDir);
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        Environment.SetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder, _originalAppDataFolder);
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();
    }

    private void WriteSettings(string json) =>
        _fs.WriteAllText(AppDataConstants.Paths.SettingsFilePath, json);

    private string ReadSettings() => _fs.ReadAllText(AppDataConstants.Paths.SettingsFilePath).AsTask().GetAwaiter().GetResult();

    private const string BaseSettings = """
        {"current":{"profile":"p1","language":"zh"},
         "vendor":{"p1":{"provider":"openai","model":"m1","models":[]}},
         "autoFetchModels":false}
        """;

    [Fact]
    public void PathMap_DeclaresModelAndNestedPermission() {
        SettingPathMap.PathOf("model").Should().Equal("vendor", "{profile}", "model");
        SettingPathMap.PathOf("permissions.defaultMode").Should().Equal("current", "permissions", "defaultMode");
        SettingPathMap.PathOf("theme").Should().BeNull("扁平键不声明路径");
    }

    [Fact]
    public async Task Load_Model_ReadsActiveProfileModel() {
        WriteSettings(BaseSettings);

        var value = await ConfigLoader.LoadSettingFromSettingsJsonAsync("model", _fs);

        value.Should().Be("m1", "config_get model 必须读运行时真正使用的 vendor.{profile}.model");
    }

    [Fact]
    public async Task Save_Model_WritesSameFieldItReads_AndKeepsUnknownFields() {
        WriteSettings(BaseSettings);

        await ConfigLoader.SaveSettingToSettingsJsonAsync("model", "m2", _fs);

        var value = await ConfigLoader.LoadSettingFromSettingsJsonAsync("model", _fs);
        value.Should().Be("m2", "写进去的字段必须就是读出来的字段 —— 否则写读不对称");

        var json = ReadSettings();
        json.Should().Contain("\"m2\"");
        json.Should().Contain("autoFetchModels", "定点改一个字段，不能把本类不认识的字段丢掉");
        json.Should().Contain("\"language\"");
    }

    [Fact]
    public async Task Save_ModelNull_RemovesField() {
        WriteSettings(BaseSettings);

        await ConfigLoader.SaveSettingToSettingsJsonAsync("model", null, _fs);

        var value = await ConfigLoader.LoadSettingFromSettingsJsonAsync("model", _fs);
        value.Should().BeNull();
        ReadSettings().Should().NotContain("\"m2\"");
    }

    [Fact]
    public async Task Save_NestedPermission_WritesUnderCurrentPermissions() {
        WriteSettings(BaseSettings);

        await ConfigLoader.SaveSettingToSettingsJsonAsync("permissions.defaultMode", "plan", _fs);

        var value = await ConfigLoader.LoadSettingFromSettingsJsonAsync("permissions.defaultMode", _fs);
        value.Should().Be("plan", "PermissionChecker 读的是 current.permissions.defaultMode");

        var json = ReadSettings();
        json.Should().Contain("\"plan\"");
        json.Should().Contain("\"profile\": \"p1\"");
    }

    [Fact]
    public async Task Save_ModelWithoutProfile_ThrowsInsteadOfSilentlyDoingNothing() {
        WriteSettings("""{"current":{"language":"zh"},"vendor":{}}""");

        var act = () => ConfigLoader.SaveSettingToSettingsJsonAsync("model", "m9", _fs);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*current.profile*");
        ReadSettings().Should().NotContain("m9", "抛错时不能留下半写状态");
    }

    [Fact]
    public async Task Load_ModelWithoutProfile_ReturnsNull() {
        WriteSettings("""{"current":{"language":"zh"},"vendor":{}}""");

        var value = await ConfigLoader.LoadSettingFromSettingsJsonAsync("model", _fs);

        value.Should().BeNull("没有当前供应商就没有「生效模型」，读侧不猜");
    }
}
