// JCC11003 抑制: 反射 Invoke 返回值需要 null-forgiving
#pragma warning disable JCC11003
namespace Host.Tests.Cli;

/// <summary>
/// BUG-3 回归测试: MSYS2/Git Bash 路径展开恢复。
/// <para>修复: Program.Main 入口添加 RestoreMsysPathExpansion,检测 /Git/xxx 模式恢复为 /xxx。</para>
/// <para>Git Bash 会将以 / 开头的斜杠命令参数视为 Unix 路径并展开为 Windows 路径,
/// 例: /compact → C:/Program Files/Git/compact → 恢复为 /compact</para>
/// </summary>
public sealed class MsysPathExpansionTests {
    private static readonly Type ProgramType = typeof(JoinCode.Program);
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>
    /// 调用 private static TryRestoreMsysPath(string) → string?
    /// </summary>
    private static string? TryRestoreMsysPath(string arg) {
        var method = ProgramType.GetMethod("TryRestoreMsysPath", PrivateStatic)
            ?? throw new InvalidOperationException("TryRestoreMsysPath 方法未找到");
        return (string?)method.Invoke(null, [arg]);
    }

    /// <summary>
    /// 调用 private static RestoreMsysPathExpansion(string[]) → string[]
    /// </summary>
    private static string[] RestoreMsysPathExpansion(string[] args) {
        var method = ProgramType.GetMethod("RestoreMsysPathExpansion", PrivateStatic)
            ?? throw new InvalidOperationException("RestoreMsysPathExpansion 方法未找到");
        return (string[])method.Invoke(null, [args])!;
    }

    // ===== TryRestoreMsysPath 单参数恢复测试 =====

    /// <summary>
    /// BUG-3 核心场景: C:/Program Files/Git/compact → /compact
    /// </summary>
    [Fact]
    public void TryRestoreMsysPath_GitBashExpandedPath_Should_Restore_To_SlashCommand() {
        var result = TryRestoreMsysPath("C:/Program Files/Git/compact");

        result.Should().Be("/compact");
    }

    /// <summary>
    /// BUG-3: 反斜杠路径也应恢复 C:\Program Files\Git\compact → /compact
    /// </summary>
    [Fact]
    public void TryRestoreMsysPath_BackslashGitPath_Should_Restore() {
        var result = TryRestoreMsysPath(@"C:\Program Files\Git\compact");

        result.Should().Be("/compact");
    }

    /// <summary>
    /// BUG-3: 多级 Git 路径 D:/tools/Git/help → /help
    /// </summary>
    [Fact]
    public void TryRestoreMsysPath_CustomGitLocation_Should_Restore() {
        var result = TryRestoreMsysPath("D:/tools/Git/help");

        result.Should().Be("/help");
    }

    /// <summary>
    /// BUG-3: 原始斜杠命令(未展开)不应被修改 — 不以 X: 开头直接返回 null
    /// </summary>
    [Theory]
    [InlineData("/compact")]
    [InlineData("/help")]
    [InlineData("--dry-run")]
    public void TryRestoreMsysPath_OriginalSlashCommand_Should_Return_Null(string arg) {
        var result = TryRestoreMsysPath(arg);

        result.Should().BeNull("原始斜杠命令不以盘符开头,无需恢复");
    }

    /// <summary>
    /// BUG-3: 不含 /Git/ 的普通 Windows 路径不应恢复
    /// </summary>
    [Fact]
    public void TryRestoreMsysPath_NormalWindowsPath_Should_Return_Null() {
        var result = TryRestoreMsysPath(@"C:\Users\test\file.txt");

        result.Should().BeNull("普通 Windows 路径不含 /Git/,无需恢复");
    }

    /// <summary>
    /// BUG-3: /Git/ 后为空(尾随分隔符)应返回 null — 避免恢复出空命令 "/"
    /// </summary>
    [Fact]
    public void TryRestoreMsysPath_GitPathWithTrailingSlash_Should_Return_Null() {
        var result = TryRestoreMsysPath("C:/Program Files/Git/");

        result.Should().BeNull("/Git/ 后为空,不应恢复出空命令");
    }

    /// <summary>
    /// BUG-3: /Git/ 后含子路径(foo/bar)应返回 null — 不是单段命令
    /// </summary>
    [Fact]
    public void TryRestoreMsysPath_GitPathWithSubdirectory_Should_Return_Null() {
        var result = TryRestoreMsysPath("C:/Program Files/Git/foo/bar");

        result.Should().BeNull("/Git/ 后含子路径,不是单段斜杠命令");
    }

    /// <summary>
    /// BUG-3: /Git/ 后含文件扩展名(foo.txt)应返回 null — 不是斜杠命令
    /// </summary>
    [Fact]
    public void TryRestoreMsysPath_GitPathWithFileExtension_Should_Return_Null() {
        var result = TryRestoreMsysPath("C:/Program Files/Git/foo.txt");

        result.Should().BeNull("/Git/ 后含文件扩展名,不是斜杠命令");
    }

    /// <summary>
    /// BUG-3: 过短参数(长度 < 6)应返回 null
    /// </summary>
    [Theory]
    [InlineData("C:/G")]
    [InlineData("C:")]
    [InlineData("ab")]
    public void TryRestoreMsysPath_TooShortArg_Should_Return_Null(string arg) {
        var result = TryRestoreMsysPath(arg);

        result.Should().BeNull("过短参数不可能是 Git Bash 展开路径");
    }

    // ===== RestoreMsysPathExpansion 数组批量恢复测试 =====

    /// <summary>
    /// BUG-3: 混合参数数组 — 展开路径恢复,普通参数不变
    /// </summary>
    [Fact]
    public void RestoreMsysPathExpansion_MixedArgs_Should_Restore_Only_GitPaths() {
        var args = new[] { "C:/Program Files/Git/compact", "--dry-run", "C:/Program Files/Git/help", "normal.txt" };

        var result = RestoreMsysPathExpansion(args);

        result.Should().HaveCount(4);
        result[0].Should().Be("/compact");
        result[1].Should().Be("--dry-run");
        result[2].Should().Be("/help");
        result[3].Should().Be("normal.txt");
    }

    /// <summary>
    /// BUG-3: 空数组应原样返回
    /// </summary>
    [Fact]
    public void RestoreMsysPathExpansion_EmptyArgs_Should_Return_Empty() {
        var result = RestoreMsysPathExpansion([]);

        result.Should().BeEmpty();
    }

    /// <summary>
    /// BUG-3: 全部普通参数应原样返回
    /// </summary>
    [Fact]
    public void RestoreMsysPathExpansion_AllNormalArgs_Should_Return_Unchanged() {
        var args = new[] { "--model", "gpt-4o", "--json" };

        var result = RestoreMsysPathExpansion(args);

        result.Should().BeEquivalentTo(args);
    }

    /// <summary>
    /// BUG-3: 全部 Git 展开路径应全部恢复
    /// </summary>
    [Fact]
    public void RestoreMsysPathExpansion_AllGitPaths_Should_Restore_All() {
        var args = new[] { "C:/Program Files/Git/compact", "C:/Program Files/Git/clear", "C:/Program Files/Git/help" };

        var result = RestoreMsysPathExpansion(args);

        result.Should().Equal("/compact", "/clear", "/help");
    }
}
