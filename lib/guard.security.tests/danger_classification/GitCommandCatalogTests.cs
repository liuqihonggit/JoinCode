namespace Guard.Security.Tests;

/// <summary>
/// GitCommandCatalog 数据源完整性确定性测试 — 验证 Git 子命令唯一数据源的正确性 + 消费方委托一致性。
/// <para>
/// 三个语义不同的集合:
/// <list type="bullet">
/// <item>ReadOnlySubcommands — 纯只读(25个),供 CommandDangerClassifier.IsGitReadOnlySubcommand 委托</item>
/// <item>SafeSubcommands — 安全(只读+部分写入,48个),供 ReadOnlyCommandDetector.SafeGitSubcommands 委托</item>
/// <item>DangerousSubcommands — 危险(9个),供 ReadOnlyCommandDetector.DangerousGitSubcommands 委托</item>
/// </list>
/// </para>
/// </summary>
public class GitCommandCatalogTests {
    #region ReadOnlySubcommands 完整性

    [Fact]
    [Trait("Category", "Deterministic")]
    public void ReadOnlySubcommands_Should_Contain_Expected_ReadOnly_Commands() {
        // 纯只读子命令(25个) — 不修改仓库状态
        var expected = new[] {
            "status", "log", "diff", "show", "blame", "reflog", "describe", "shortlog",
            "ls-files", "ls-tree", "cat-file", "rev-parse", "rev-list", "name-rev",
            "cherry", "cherry-pick", "branch", "remote", "stash", "config",
            "fetch", "grep", "count-objects", "fsck", "gc", "help", "version", "var"
        };
        foreach (var cmd in expected) {
            GitCommandCatalog.ReadOnlySubcommands.Should().Contain(cmd,
                $"只读子命令 '{cmd}' 必须在 ReadOnlySubcommands 中");
        }
        GitCommandCatalog.ReadOnlySubcommands.Count.Should().Be(expected.Length,
            "ReadOnlySubcommands 应包含 25 个纯只读子命令");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void ReadOnlySubcommands_Should_Not_Contain_Write_Commands() {
        // 写入子命令不应在只读列表中
        var writeCommands = new[] { "add", "commit", "push", "reset", "rm", "clean", "mv", "checkout", "merge", "rebase" };
        foreach (var cmd in writeCommands) {
            GitCommandCatalog.ReadOnlySubcommands.Should().NotContain(cmd,
                $"写入子命令 '{cmd}' 不应在 ReadOnlySubcommands 中");
        }
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void ReadOnlySubcommands_Should_Use_OrdinalIgnoreCase_Comparer() {
        GitCommandCatalog.ReadOnlySubcommands.Contains("STATUS").Should().BeTrue();
        GitCommandCatalog.ReadOnlySubcommands.Contains("Log").Should().BeTrue();
    }

    #endregion

    #region SafeSubcommands 完整性

    [Fact]
    [Trait("Category", "Deterministic")]
    public void SafeSubcommands_Should_Contain_Expected_Safe_Commands() {
        // 安全子命令(52个) — 只读 + 部分写入,包含 ReadOnlySubcommands 全部只读命令
        var expected = new[] {
            "status", "log", "show", "diff", "branch", "tag", "remote", "config",
            "help", "version", "stash", "blame", "annotate", "describe",
            "shortlog", "reflog", "ls-files", "ls-tree", "ls-remote",
            "name-rev", "rev-parse", "rev-list", "merge-base",
            "cherry", "cherry-pick",
            "grep", "whatchanged", "show-branch", "verify-pack",
            "cat-file", "for-each-ref", "worktree",
            "count-objects", "fsck", "gc", "var",
            "add", "commit", "mv", "restore", "switch", "checkout",
            "fetch", "pull", "merge", "rebase",
            "init", "clone", "submodule", "am", "apply", "notes"
        };
        foreach (var cmd in expected) {
            GitCommandCatalog.SafeSubcommands.Should().Contain(cmd,
                $"安全子命令 '{cmd}' 必须在 SafeSubcommands 中");
        }
        GitCommandCatalog.SafeSubcommands.Count.Should().Be(expected.Length,
            "SafeSubcommands 应包含 52 个安全子命令(只读 + 部分写入)");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void SafeSubcommands_Should_Be_Superset_Of_ReadOnlySubcommands() {
        // SafeSubcommands 应是 ReadOnlySubcommands 的超集(安全 ⊃ 只读)
        foreach (var cmd in GitCommandCatalog.ReadOnlySubcommands) {
            GitCommandCatalog.SafeSubcommands.Should().Contain(cmd,
                $"只读子命令 '{cmd}' 应在 SafeSubcommands 中(安全 ⊃ 只读)");
        }
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void SafeSubcommands_Should_Use_OrdinalIgnoreCase_Comparer() {
        GitCommandCatalog.SafeSubcommands.Contains("ADD").Should().BeTrue();
        GitCommandCatalog.SafeSubcommands.Contains("Commit").Should().BeTrue();
    }

    #endregion

    #region DangerousSubcommands 完整性

    [Fact]
    [Trait("Category", "Deterministic")]
    public void DangerousSubcommands_Should_Contain_Expected_Dangerous_Commands() {
        // 危险子命令(9个) — 真正破坏性操作
        var expected = new[] {
            "push", "reset", "rm", "clean",
            "format-patch", "send-email", "filter-branch", "replace", "update-ref"
        };
        foreach (var cmd in expected) {
            GitCommandCatalog.DangerousSubcommands.Should().Contain(cmd,
                $"危险子命令 '{cmd}' 必须在 DangerousSubcommands 中");
        }
        GitCommandCatalog.DangerousSubcommands.Count.Should().Be(expected.Length,
            "DangerousSubcommands 应包含 9 个危险子命令");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void DangerousSubcommands_Should_Be_Disjoint_With_SafeSubcommands() {
        // 危险子命令与安全子命令不应相交
        foreach (var cmd in GitCommandCatalog.DangerousSubcommands) {
            GitCommandCatalog.SafeSubcommands.Should().NotContain(cmd,
                $"危险子命令 '{cmd}' 不应在 SafeSubcommands 中(危险 ∩ 安全 = ∅)");
        }
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void DangerousSubcommands_Should_Use_OrdinalIgnoreCase_Comparer() {
        GitCommandCatalog.DangerousSubcommands.Contains("PUSH").Should().BeTrue();
        GitCommandCatalog.DangerousSubcommands.Contains("Reset").Should().BeTrue();
    }

    #endregion

    #region 消费方委托一致性

    [Fact]
    [Trait("Category", "Deterministic")]
    public void IsGitReadOnlySubcommand_Should_Delegate_To_ReadOnlySubcommands() {
        // 验证 CommandDangerClassifier.IsGitReadOnlySubcommand 委托 GitCommandCatalog.ReadOnlySubcommands
        // 只读子命令应返回 true
        foreach (var subcommand in GitCommandCatalog.ReadOnlySubcommands) {
            var command = ShellCommand.Parse($"git {subcommand}");
            // branch/stash/config/fetch 有条件只读,跳过(它们有额外参数判断)
            if (subcommand is "branch" or "stash" or "config" or "fetch") {
                continue;
            }
            CommandDangerClassifier.IsGitReadOnlySubcommand(command).Should().BeTrue(
                $"git {subcommand} 应为只读(委托 ReadOnlySubcommands)");
        }
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void IsGitReadOnlySubcommand_DangerousCommands_Should_Be_False() {
        // 危险子命令不应被判定为只读
        foreach (var subcommand in GitCommandCatalog.DangerousSubcommands) {
            var command = ShellCommand.Parse($"git {subcommand}");
            CommandDangerClassifier.IsGitReadOnlySubcommand(command).Should().BeFalse(
                $"git {subcommand} 是危险子命令,不应为只读");
        }
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void ReadOnlyCommandDetector_SafeGitSubcommands_Should_Delegate_To_SafeSubcommands() {
        // 验证 CommandCatalog.SafeGitSubcommands 委托 GitCommandCatalog.SafeSubcommands
        // 通过反射获取 private static 字段,验证引用一致(单数据源委托)
        var field = typeof(CommandCatalog).GetField("SafeGitSubcommands",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field.Should().NotBeNull("SafeGitSubcommands 字段应存在");
        var value = field!.GetValue(null);
        value.Should().BeSameAs(GitCommandCatalog.SafeSubcommands,
            "SafeGitSubcommands 应委托 GitCommandCatalog.SafeSubcommands(同一引用)");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void ReadOnlyCommandDetector_DangerousGitSubcommands_Should_Delegate_To_DangerousSubcommands() {
        // 验证 CommandCatalog.DangerousGitSubcommands 委托 GitCommandCatalog.DangerousSubcommands
        // 通过反射获取 private static 字段,验证引用一致(单数据源委托)
        var field = typeof(CommandCatalog).GetField("DangerousGitSubcommands",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        field.Should().NotBeNull("DangerousGitSubcommands 字段应存在");
        var value = field!.GetValue(null);
        value.Should().BeSameAs(GitCommandCatalog.DangerousSubcommands,
            "DangerousGitSubcommands 应委托 GitCommandCatalog.DangerousSubcommands(同一引用)");
    }

    #endregion
}
