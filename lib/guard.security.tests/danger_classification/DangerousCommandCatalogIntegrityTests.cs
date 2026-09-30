namespace Guard.Security.Tests;

/// <summary>
/// DangerousCommandCatalog 数据源完整性确定性测试 — 断言无重复、覆盖预期命令集、MergeLevels/InferLevel 正确。
/// </summary>
public class DangerousCommandCatalogIntegrityTests {
    #region Commands 完整性

    [Fact]
    public void Commands_Should_Contain_Expected_Dangerous_Commands() {
        // 核心危险命令必须登记
        var expectedDangerous = new[] { "rm", "del", "erase", "format", "mkfs", "fdisk", "shred", "dd", "shutdown" };
        foreach (var cmd in expectedDangerous) {
            DangerousCommandCatalog.Commands.Should().ContainKey(cmd,
                $"命令 '{cmd}' 必须在 catalog 中登记");
        }
    }

    [Fact]
    public void Commands_Should_Contain_Git_With_LightValidation() {
        DangerousCommandCatalog.Commands.Should().ContainKey("git");
        DangerousCommandCatalog.Commands["git"].Level.Should().Be(CommandDangerLevel.LightValidation);
    }

    [Fact]
    public void Commands_Should_Contain_Safe_Commands() {
        var expectedSafe = new[] { "ls", "cat", "grep", "pwd", "whoami", "echo" };
        foreach (var cmd in expectedSafe) {
            DangerousCommandCatalog.Commands.Should().ContainKey(cmd);
            DangerousCommandCatalog.Commands[cmd].Level.Should().Be(CommandDangerLevel.Safe,
                $"命令 '{cmd}' 应为 Safe 级别");
        }
    }

    [Fact]
    public void Commands_Should_Have_No_Duplicate_Keys() {
        // FrozenDictionary 已去重,验证 Keys 数量与预期一致即可
        DangerousCommandCatalog.Commands.Should().NotBeEmpty();
        var keys = DangerousCommandCatalog.Commands.Keys.ToList();
        keys.Distinct().Count().Should().Be(keys.Count);
    }

    [Fact]
    public void Commands_Each_Entry_Should_Have_NonEmpty_Name() {
        foreach (var entry in DangerousCommandCatalog.Commands.Values) {
            entry.CommandName.Should().NotBeNullOrEmpty();
            entry.Description.Should().NotBeNullOrEmpty();
        }
    }

    #endregion

    #region Flags 完整性

    [Fact]
    public void Flags_Should_Contain_Recursive_And_Force_Flags() {
        DangerousCommandCatalog.Flags.Should().ContainKey("-r");
        DangerousCommandCatalog.Flags.Should().ContainKey("-f");
        DangerousCommandCatalog.Flags.Should().ContainKey("-force");
        DangerousCommandCatalog.Flags.Should().ContainKey("/s");
    }

    [Fact]
    public void Flags_Should_Contain_Mir_And_Purge_As_Dangerous() {
        DangerousCommandCatalog.Flags["/MIR"].Level.Should().Be(CommandDangerLevel.Dangerous);
        DangerousCommandCatalog.Flags["/PURGE"].Level.Should().Be(CommandDangerLevel.Dangerous);
    }

    [Fact]
    public void Flags_Recursive_Should_Be_Execution() {
        DangerousCommandCatalog.Flags["-r"].Level.Should().Be(CommandDangerLevel.Execution);
        DangerousCommandCatalog.Flags["-R"].Level.Should().Be(CommandDangerLevel.Execution);
        DangerousCommandCatalog.Flags["/s"].Level.Should().Be(CommandDangerLevel.Execution);
    }

    [Fact]
    public void Flags_RootPath_Should_Be_Dangerous() {
        DangerousCommandCatalog.Flags["/"].Level.Should().Be(CommandDangerLevel.Dangerous);
        DangerousCommandCatalog.Flags["C:\\"].Level.Should().Be(CommandDangerLevel.Dangerous);
    }

    #endregion

    #region Combinations 完整性

    [Fact]
    public void Combinations_Should_Contain_RmRf_Combination() {
        var rmRf = DangerousCommandCatalog.Combinations.FirstOrDefault(c =>
            c.LowerPatterns.Contains("rm") && c.LowerPatterns.Contains("-rf"));
        rmRf.Should().NotBeNull();
        rmRf!.Level.Should().Be(CommandDangerLevel.Execution);
    }

    [Fact]
    public void Combinations_Should_Contain_Format_C_Combination() {
        var formatC = DangerousCommandCatalog.Combinations.FirstOrDefault(c =>
            c.LowerPatterns.Contains("format") && c.LowerPatterns.Contains("c:"));
        formatC.Should().NotBeNull();
        formatC!.Level.Should().Be(CommandDangerLevel.Dangerous);
    }

    [Fact]
    public void Combinations_Should_Contain_GitInjection_Combinations() {
        // git -c / --exec-path / --config-env 注入防护
        DangerousCommandCatalog.Combinations.Should().Contain(c =>
            c.LowerPatterns.Contains("git") && c.LowerPatterns.Contains("-c"));
        DangerousCommandCatalog.Combinations.Should().Contain(c =>
            c.LowerPatterns.Contains("git") && c.LowerPatterns.Contains("--exec-path"));
        DangerousCommandCatalog.Combinations.Should().Contain(c =>
            c.LowerPatterns.Contains("git") && c.LowerPatterns.Contains("--config-env"));
    }

    [Fact]
    public void Combinations_Should_Contain_PipeToInterpreter_Combinations() {
        // 管道到解释器组合
        DangerousCommandCatalog.Combinations.Should().Contain(c =>
            c.LowerPatterns[0] == "|" && c.LowerPatterns.Contains("bash"));
        DangerousCommandCatalog.Combinations.Should().Contain(c =>
            c.LowerPatterns[0] == "|" && c.LowerPatterns.Contains("python"));
    }

    [Fact]
    public void Combinations_Each_Should_Have_NonEmpty_Patterns() {
        foreach (var combo in DangerousCommandCatalog.Combinations) {
            combo.LowerPatterns.Should().NotBeEmpty();
            combo.Description.Should().NotBeNullOrEmpty();
        }
    }

    #endregion

    #region DangerousPaths 完整性

    [Fact]
    public void DangerousPaths_Should_Contain_Root_Paths_As_Dangerous() {
        DangerousCommandCatalog.DangerousPaths["/"].Should().Be(CommandDangerLevel.Dangerous);
        DangerousCommandCatalog.DangerousPaths["C:\\"].Should().Be(CommandDangerLevel.Dangerous);
        DangerousCommandCatalog.DangerousPaths["/root"].Should().Be(CommandDangerLevel.Dangerous);
    }

    [Fact]
    public void DangerousPaths_Should_Contain_SystemDirs_As_Execution() {
        DangerousCommandCatalog.DangerousPaths["/etc"].Should().Be(CommandDangerLevel.Execution);
        DangerousCommandCatalog.DangerousPaths["/usr"].Should().Be(CommandDangerLevel.Execution);
        DangerousCommandCatalog.DangerousPaths["/var"].Should().Be(CommandDangerLevel.Execution);
    }

    [Fact]
    public void DangerousPaths_Should_Contain_Tilde_As_LightValidation() {
        DangerousCommandCatalog.DangerousPaths["~"].Should().Be(CommandDangerLevel.LightValidation);
        DangerousCommandCatalog.DangerousPaths[".."].Should().Be(CommandDangerLevel.LightValidation);
    }

    #endregion

    #region MergeLevels

    [Theory]
    [InlineData(CommandDangerLevel.Safe, CommandDangerLevel.Safe, CommandDangerLevel.Safe)]
    [InlineData(CommandDangerLevel.Safe, CommandDangerLevel.Execution, CommandDangerLevel.Execution)]
    [InlineData(CommandDangerLevel.LightValidation, CommandDangerLevel.Execution, CommandDangerLevel.Execution)]
    [InlineData(CommandDangerLevel.Execution, CommandDangerLevel.Dangerous, CommandDangerLevel.Dangerous)]
    [InlineData(CommandDangerLevel.Unknown, CommandDangerLevel.Safe, CommandDangerLevel.Unknown)]
    public void MergeLevels_Should_Return_Highest(CommandDangerLevel a, CommandDangerLevel b, CommandDangerLevel expected) {
        DangerousCommandCatalog.MergeLevels(a, b).Should().Be(expected);
    }

    [Fact]
    public void MergeLevels_Empty_Should_Return_Safe() {
        DangerousCommandCatalog.MergeLevels().Should().Be(CommandDangerLevel.Safe);
    }

    [Fact]
    public void MergeLevels_Single_Should_Return_That_Level() {
        DangerousCommandCatalog.MergeLevels(CommandDangerLevel.Execution).Should().Be(CommandDangerLevel.Execution);
    }

    [Fact]
    public void MergeLevels_All_Levels_Should_Return_Dangerous() {
        var result = DangerousCommandCatalog.MergeLevels(
            CommandDangerLevel.Safe, CommandDangerLevel.Unknown, CommandDangerLevel.LightValidation,
            CommandDangerLevel.Execution, CommandDangerLevel.Dangerous);
        result.Should().Be(CommandDangerLevel.Dangerous);
    }

    #endregion

    #region InferLevel

    [Theory]
    [InlineData(CommandRisk.None, CommandDangerLevel.Safe)]
    [InlineData(CommandRisk.FileDeletion, CommandDangerLevel.Execution)]
    [InlineData(CommandRisk.PathEscape, CommandDangerLevel.Dangerous)]
    [InlineData(CommandRisk.RecursiveOperation, CommandDangerLevel.Execution)]
    [InlineData(CommandRisk.ForceOperation, CommandDangerLevel.Execution)]
    [InlineData(CommandRisk.RemoteExecution, CommandDangerLevel.Execution)]
    [InlineData(CommandRisk.ExcessiveSearchScope, CommandDangerLevel.LightValidation)]
    public void InferLevel_Should_Map_Correctly(CommandRisk risk, CommandDangerLevel expected) {
        DangerousCommandCatalog.InferLevel(risk).Should().Be(expected);
    }

    #endregion

    #region InterpreterCommands 完整性

    [Fact]
    public void InterpreterCommands_Should_Contain_Common_Interpreters() {
        var expected = new[] { "bash", "sh", "python", "python3", "perl", "ruby", "node", "powershell", "pwsh", "cmd" };
        foreach (var interp in expected) {
            DangerousCommandCatalog.InterpreterCommands.Should().Contain(interp);
        }
    }

    [Fact]
    public void InterpreterCommands_Should_Be_CaseInsensitive() {
        DangerousCommandCatalog.InterpreterCommands.Contains("BASH").Should().BeTrue();
        DangerousCommandCatalog.InterpreterCommands.Contains("Python").Should().BeTrue();
    }

    #endregion

    #region RiskPriority / SelectPrimaryRisk 统一数据源

    [Fact]
    public void RiskPriority_Should_Start_With_PathEscape() {
        // PathEscape 对应 Dangerous(黑灯直接拒绝),优先级必须最高
        DangerousCommandCatalog.RiskPriority[0].Should().Be(CommandRisk.PathEscape);
    }

    [Fact]
    public void RiskPriority_Should_Not_Contain_None() {
        // None 不是真实风险,不应出现在优先级表
        DangerousCommandCatalog.RiskPriority.Should().NotContain(CommandRisk.None);
    }

    [Fact]
    public void RiskPriority_Should_Not_Contain_Duplicates() {
        var distinct = DangerousCommandCatalog.RiskPriority.Distinct().Count();
        distinct.Should().Be(DangerousCommandCatalog.RiskPriority.Length,
            "RiskPriority 不应有重复元素");
    }

    [Fact]
    public void SelectPrimaryRisk_Empty_Should_Return_None() {
        DangerousCommandCatalog.SelectPrimaryRisk(new List<CommandRisk>()).Should().Be(CommandRisk.None);
    }

    [Fact]
    public void SelectPrimaryRisk_Single_Should_Return_That_Risk() {
        DangerousCommandCatalog.SelectPrimaryRisk(new List<CommandRisk> { CommandRisk.FileDeletion })
            .Should().Be(CommandRisk.FileDeletion);
    }

    [Fact]
    public void SelectPrimaryRisk_PathEscape_Should_Be_Highest_Priority() {
        // PathEscape 必须优先于 FileDeletion/DirectoryDeletion 等
        var risks = new List<CommandRisk> {
            CommandRisk.FileDeletion, CommandRisk.DirectoryDeletion, CommandRisk.PathEscape
        };
        DangerousCommandCatalog.SelectPrimaryRisk(risks).Should().Be(CommandRisk.PathEscape);
    }

    [Fact]
    public void SelectPrimaryRisk_UnknownRisk_Should_Return_First() {
        // ExcessiveSearchScope 不在优先级表,返回列表第一个
        DangerousCommandCatalog.SelectPrimaryRisk(new List<CommandRisk> { CommandRisk.ExcessiveSearchScope })
            .Should().Be(CommandRisk.ExcessiveSearchScope);
    }

    [Fact]
    public void SelectPrimaryRisk_MixedKnownAndUnknown_Should_Prefer_Known_High_Priority() {
        // 已知高优先级风险应优先于未知风险
        var risks = new List<CommandRisk> {
            CommandRisk.ExcessiveSearchScope, CommandRisk.FileDeletion
        };
        DangerousCommandCatalog.SelectPrimaryRisk(risks).Should().Be(CommandRisk.FileDeletion);
    }

    [Fact]
    public void SelectPrimaryRisk_CommandDangerClassifier_Delegates_To_Catalog() {
        // 验证 CommandDangerClassifier.SelectPrimaryRisk 与 catalog 行为一致(委托)
        var risks = new List<CommandRisk> {
            CommandRisk.DirectoryDeletion, CommandRisk.PathEscape, CommandRisk.ForceOperation
        };
        CommandDangerClassifier.SelectPrimaryRisk(risks)
            .Should().Be(DangerousCommandCatalog.SelectPrimaryRisk(risks));
    }

    #endregion

    #region FileDeletionCommands / AllDeletionCommands 派生属性

    [Fact]
    [Trait("Category", "Deterministic")]
    public void FileDeletionCommands_Should_Contain_Only_FileDeletion_RiskType() {
        // FileDeletionCommands 应包含 rm/del/erase/Remove-Item(4个,FileDeletion)
        var expected = new[] { "rm", "del", "erase", "Remove-Item" };
        foreach (var cmd in expected) {
            DangerousCommandCatalog.FileDeletionCommands.Should().Contain(cmd,
                $"文件删除命令 '{cmd}' 必须在 FileDeletionCommands 中");
        }
        DangerousCommandCatalog.FileDeletionCommands.Count.Should().Be(expected.Length,
            "FileDeletionCommands 应仅包含 FileDeletion 风险类型的命令(4个)");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void FileDeletionCommands_Should_Not_Contain_DirectoryDeletion_Commands() {
        // FileDeletionCommands 不应包含 rmdir/rd(DirectoryDeletion)
        DangerousCommandCatalog.FileDeletionCommands.Should().NotContain("rmdir");
        DangerousCommandCatalog.FileDeletionCommands.Should().NotContain("rd");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AllDeletionCommands_Should_Contain_FileAndDirectoryDeletion() {
        // AllDeletionCommands 应包含 rm/del/erase/Remove-Item/rmdir/rd(6个)
        var expected = new[] { "rm", "del", "erase", "Remove-Item", "rmdir", "rd" };
        foreach (var cmd in expected) {
            DangerousCommandCatalog.AllDeletionCommands.Should().Contain(cmd,
                $"删除命令 '{cmd}' 必须在 AllDeletionCommands 中");
        }
        DangerousCommandCatalog.AllDeletionCommands.Count.Should().Be(expected.Length,
            "AllDeletionCommands 应包含 FileDeletion + DirectoryDeletion 风险类型的命令(6个)");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AllDeletionCommands_Should_Be_Superset_Of_FileDeletionCommands() {
        // AllDeletionCommands 应是 FileDeletionCommands 的超集
        foreach (var cmd in DangerousCommandCatalog.FileDeletionCommands) {
            DangerousCommandCatalog.AllDeletionCommands.Should().Contain(cmd);
        }
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void FileDeletionCommands_Should_Use_OrdinalIgnoreCase_Comparer() {
        // 验证大小写不敏感
        DangerousCommandCatalog.FileDeletionCommands.Contains("RM").Should().BeTrue();
        DangerousCommandCatalog.FileDeletionCommands.Contains("DEL").Should().BeTrue();
        DangerousCommandCatalog.FileDeletionCommands.Contains("remove-item").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AllDeletionCommands_Should_Use_OrdinalIgnoreCase_Comparer() {
        // 验证大小写不敏感
        DangerousCommandCatalog.AllDeletionCommands.Contains("RMDIR").Should().BeTrue();
        DangerousCommandCatalog.AllDeletionCommands.Contains("RD").Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void CheckRecurseForceCombination_Should_Delegate_To_FileDeletionCommands() {
        // 验证 CommandDangerClassifier.CheckRecurseForceCombination 委托 FileDeletionCommands
        // rm/Remove-Item 应触发检查(返回非 Safe),rmdir/rd 不应触发(返回 Safe)
        var rmCmd = ShellCommand.Parse("rm -rf /");
        CommandDangerClassifier.CheckRecurseForceCombination(rmCmd)
            .Should().NotBe(CommandDangerLevel.Safe, "rm 在 FileDeletionCommands 中,应触发递归强制检查");

        var rmdirCmd = ShellCommand.Parse("rmdir -rf /");
        CommandDangerClassifier.CheckRecurseForceCombination(rmdirCmd)
            .Should().Be(CommandDangerLevel.Safe, "rmdir 不在 FileDeletionCommands 中,不应触发递归强制检查");

        var rdCmd = ShellCommand.Parse("rd -rf /");
        CommandDangerClassifier.CheckRecurseForceCombination(rdCmd)
            .Should().Be(CommandDangerLevel.Safe, "rd 不在 FileDeletionCommands 中,不应触发递归强制检查");
    }

    #endregion
}
