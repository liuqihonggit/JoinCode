// JCC9001 抑制: 源码级回归测试需直接读取文件系统验证 ctor_scanner 源码常量
// JCC11003 抑制: 存量代码可空抑制
#pragma warning disable JCC9001, JCC11003
namespace Host.Tests.Cli;

/// <summary>
/// BUG-4 回归测试: ctor_scanner 扫描目录和输出路径修正。
/// <para>修复: ScanDirs 从 ["lib","kit","server","llm","app"] 改为 ["modules"],
/// 输出路径从 tool/ 改为 modules/tools/ctor_scanner/。</para>
/// <para>验证方式: 源码级回归测试,读取 Program.cs 验证常量未被改回旧值。</para>
/// </summary>
public sealed class CtorScannerConfigTests {
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string CtorScannerProgramPath = System.IO.Path.Combine(
        RepoRoot, "modules", "tools", "ctor_scanner", "Program.cs");

    /// <summary>
    /// 定位仓库根目录 — 向上查找含 .git 的目录
    /// </summary>
    private static string FindRepoRoot() {
        var dir = AppContext.BaseDirectory;
        while (dir is not null) {
            var gitPath = System.IO.Path.Combine(dir, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return dir;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// BUG-4: ScanDirs 应为 ["modules"],不应包含旧值 ["lib","kit","server","llm","app"]
    /// </summary>
    [Fact]
    public void ScanDirs_Should_Be_Modules_Only() {
        var source = File.ReadAllText(CtorScannerProgramPath);

        // 修复后: ScanDirs = ["modules"]
        source.Should().Contain("""ScanDirs = ["modules"]""",
            "BUG-4 修复: ScanDirs 应为 [\"modules\"] 而非旧的 [\"lib\",\"kit\",\"server\",\"llm\",\"app\"]");

        // 不应包含旧值
        source.Should().NotContain("\"lib\"", "BUG-4 修复: 不应扫描旧 lib 目录");
        source.Should().NotContain("\"kit\"", "BUG-4 修复: 不应扫描旧 kit 目录");
        source.Should().NotContain("\"server\"", "BUG-4 修复: 不应扫描旧 server 目录");
        source.Should().NotContain("\"llm\"", "BUG-4 修复: 不应扫描旧 llm 目录");
        source.Should().NotContain("\"app\"", "BUG-4 修复: 不应扫描旧 app 目录");
    }

    /// <summary>
    /// BUG-4: 输出路径应使用 modules/tools/ctor_scanner/,不应使用旧 tool/ 路径
    /// </summary>
    [Fact]
    public void OutputPath_Should_Use_Modules_Tools_Directory() {
        var source = File.ReadAllText(CtorScannerProgramPath);

        // 修复后: Path.Combine(repoRoot, "modules", "tools", "ctor_scanner", ...)
        source.Should().Contain("\"modules\"", "BUG-4 修复: 输出路径应包含 modules");
        source.Should().Contain("\"tools\"", "BUG-4 修复: 输出路径应包含 tools");
        source.Should().Contain("\"ctor_scanner\"", "BUG-4 修复: 输出路径应包含 ctor_scanner");
        source.Should().Contain("ctor_scan_report.json", "输出文件名应为 ctor_scan_report.json");

        // 不应包含旧的 tool/ 单段路径(排除 "tools" 的误匹配,检查旧模式 "tool/" 或 Path.Combine(repoRoot, "tool")
        source.Should().NotContain("""Path.Combine(repoRoot, "tool")""",
            "BUG-4 修复: 不应使用旧 tool/ 输出路径");
    }

    /// <summary>
    /// BUG-4: ctor_scanner 源文件应存在
    /// </summary>
    [Fact]
    public void CtorScanner_Program_File_Should_Exist() {
        File.Exists(CtorScannerProgramPath).Should().BeTrue(
            $"ctor_scanner 源文件应存在于: {CtorScannerProgramPath}");
    }

    /// <summary>
    /// BUG-4: modules 目录应存在(扫描目标)
    /// </summary>
    [Fact]
    public void Modules_Directory_Should_Exist() {
        var modulesPath = System.IO.Path.Combine(RepoRoot, "modules");

        Directory.Exists(modulesPath).Should().BeTrue(
            "BUG-4 修复: ScanDirs 指向 modules/,该目录应存在");
    }
}
