namespace Core.Tests.Configuration;

/// <summary>
/// 配置文件写入的签名（BOM）不变量 — 原文件有 BOM 就保留，没有就不许新增。
/// <para>回归背景：<c>BclFileIO</c> 曾对无 BOM 文件返回 <c>Encoding.UTF8</c>（自带 preamble），
/// 于是每条读-改-写路径（config、文件编辑、patch、sed、hooks…）都给用户文件塞进 EF BB BF。</para>
/// <para>与其它配置测试共享 <c>AppDataConstants</c> 全局状态，必须串行。</para>
/// </summary>
[Collection("AppDataConstantsCollection")]
public class ConfigFileEncodingTests : IDisposable {
    private static readonly byte[] s_utf8Bom = [0xEF, 0xBB, 0xBF];

    private readonly string? _originalAppDataFolder;
    private readonly string _tempAppDataDir;
    private readonly IFileSystem _fs = TestFileSystem.Current;
    private bool _disposed;

    public ConfigFileEncodingTests() {
        _originalAppDataFolder = Environment.GetEnvironmentVariable(JccEnvVarEnumConstants.AppDataFolder);

        _tempAppDataDir = $"/test/jcc-test-bom-{Guid.NewGuid():N}";
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

    private const string BaseSettings = """
        {"current":{"profile":"p1","language":"zh"},
         "vendor":{"p1":{"provider":"openai","model":"m1","models":[]}},
         "autoFetchModels":false}
        """;

    private void WriteRawSettings(byte[] bytes) =>
        _fs.WriteAllBytes(AppDataConstants.Paths.SettingsFilePath, bytes).AsTask().GetAwaiter().GetResult();

    private byte[] ReadRawSettings() =>
        _fs.ReadAllBytes(AppDataConstants.Paths.SettingsFilePath).AsTask().GetAwaiter().GetResult();

    private static byte[] WithoutBom(string json) => System.Text.Encoding.UTF8.GetBytes(json);

    private static byte[] WithBom(string json) =>
        [.. s_utf8Bom, .. System.Text.Encoding.UTF8.GetBytes(json)];

    private static bool StartsWithBom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    [Fact]
    public async Task SaveFlatKey_NoBomFile_StaysWithoutBom() {
        WriteRawSettings(WithoutBom(BaseSettings));

        await ConfigLoader.SaveSettingToSettingsJsonAsync("theme", "dark", _fs);

        var written = ReadRawSettings();
        StartsWithBom(written).Should().BeFalse("无 BOM 的 settings.json 改写后不得凭空多出签名");
        System.Text.Encoding.UTF8.GetString(written).Should().Contain("\"theme\": \"dark\"");
    }

    [Fact]
    public async Task SaveNestedKey_NoBomFile_StaysWithoutBom() {
        WriteRawSettings(WithoutBom(BaseSettings));

        await ConfigLoader.SaveSettingToSettingsJsonAsync("permissions.defaultMode", "plan", _fs);

        StartsWithBom(ReadRawSettings()).Should().BeFalse("嵌套键写入同样不能引入签名");
    }

    [Fact]
    public async Task SaveFlatKey_Utf8BomFile_KeepsSingleBom() {
        WriteRawSettings(WithBom(BaseSettings));

        await ConfigLoader.SaveSettingToSettingsJsonAsync("theme", "light", _fs);

        var written = ReadRawSettings();
        StartsWithBom(written).Should().BeTrue("原来有 BOM 的文件必须保留 BOM");
        // 只能有一个 BOM：再剥一次前 3 字节，不应还是 BOM
        StartsWithBom(written[3..]).Should().BeFalse("BOM 不得重复写入");
    }

    [Fact]
    public async Task SaveFlatKey_Repeatedly_WritesByteIdenticalPrefix() {
        WriteRawSettings(WithoutBom(BaseSettings));

        await ConfigLoader.SaveSettingToSettingsJsonAsync("theme", "dark", _fs);
        var first = ReadRawSettings();

        await ConfigLoader.SaveSettingToSettingsJsonAsync("theme", "light", _fs);
        var second = ReadRawSettings();

        StartsWithBom(first).Should().BeFalse();
        StartsWithBom(second).Should().BeFalse("重复保存不能累积签名");
    }
}
