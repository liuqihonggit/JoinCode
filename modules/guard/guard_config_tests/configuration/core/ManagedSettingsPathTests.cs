// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace Guard.Tests.Configuration;

/// <summary>
/// managed-settings.json 路径解析测试 — 对齐 TS managedPath.ts 分平台安全目录：
/// Windows C:\Program Files\jcc\（写需管理员，防普通用户进程伪造策略提升权限）、
/// macOS /Library/Application Support/jcc/、Linux /etc/jcc/。
/// <para>AppDataConstants 全局状态需串行 — 与 SettingsLoaderTests 同 Collection 约定。</para>
/// <para>JCC_APP_DATA_FOLDER 绝对路径透传保留（测试隔离约定，与三层 settings 解析一致）。</para>
/// </summary>
[Collection("AppDataConstantsCollection")]
public class ManagedSettingsPathTests : IDisposable {
    private readonly EnvVarScope _envScope;
    private bool _disposed;

    public ManagedSettingsPathTests() {
        _envScope = EnvVarScope.Set(JccEnvVarEnumConstants.AppDataFolder, null);
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _envScope.DisposeSafe();
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();
    }

    [Fact]
    public void GetManagedSettingsPath_默认_Windows指向ProgramFiles安全目录() {
        if (!OperatingSystem.IsWindows()) return; // 平台条件断言

        var path = SettingsLoader.GetManagedSettingsPath();

        path.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "jcc", "managed-settings.json"));
    }

    [Fact]
    public void GetManagedSettingsPath_AppDataFolder为绝对路径_透传测试隔离() {
        _envScope.Add(JccEnvVarEnumConstants.AppDataFolder, "/test/managed_iso");
        AppDataConstants.Paths = AppDataPaths.FromEnvironment();

        var path = SettingsLoader.GetManagedSettingsPath();

        path.Should().Be(Path.Combine("/test/managed_iso", "managed-settings.json"));
    }
}
