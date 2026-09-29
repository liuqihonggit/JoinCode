namespace Abs.Tests.Configuration;

/// <summary>
/// AppDataPaths 确定性单元测试 — 覆盖路径组合逻辑(CreateForTest + 计算属性)。
/// 不测试 FromEnvironment(依赖环境变量,非确定性)。
/// 路径组合关系是确定性的,断言属性间关系而非绝对路径。
/// </summary>
public sealed class AppDataPathsTests {

    // 构造完全确定的实例(不依赖 Default/Env)
    private static AppDataPaths Make(string folder = ".jcc") => new(
        AppDataFolder: folder,
        CredentialsFileName: "credentials.json",
        AuthFileName: "auth.json",
        SettingsFileName: "settings.json",
        GlobalConfigFileName: "global.json",
        RulesFolderName: "rules",
        ProjectRulesFileName: "project_rules.md",
        ScheduledTasksFileName: "scheduled_tasks.json",
        TeamsFolderName: "teams",
        TasksFolderName: "tasks",
        WorktreesFolderName: "worktrees",
        AgentsFolderName: "agents",
        ThemeFileName: "theme.json",
        TrustedFoldersFileName: "trusted_folders.json",
        SessionsFolderName: "sessions",
        SessionMetaFileName: "session.meta.json",
        CommandsFolderName: "commands",
        MailboxFolderName: "mailbox",
        FileHistoryFolderName: "file-history",
        PlansFolderName: "plans",
        ToolResultsFolderName: "tool-results",
        McpFolderName: "mcp",
        McpConnectionsFileName: "connections.json",
        McpAuthFileName: "auth.json"
    );

    [Fact]
    public void AppDataFolder_PreservedFromConstructor() {
        var p = Make(".custom");
        p.AppDataFolder.Should().Be(".custom");
    }

    [Fact]
    public void ProjectConfigFolderName_EqualsAppDataFolder() {
        var p = Make(".jcc");
        p.ProjectConfigFolderName.Should().Be(p.AppDataFolder);
    }

    [Fact]
    public void LocalSettingsRelativePath_UsesAppDataFolder() {
        var p = Make(".jcc");
        p.LocalSettingsRelativePath.Should().Be(".jcc/settings.local.json");
    }

    [Fact]
    public void WorktreeFolderName_EqualsWorktreesFolderName() {
        var p = Make();
        p.WorktreeFolderName.Should().Be(p.WorktreesFolderName);
    }

    [Fact]
    public void SettingsFilePath_CombinesJccDirectoryAndSettingsFileName() {
        var p = Make();
        p.SettingsFilePath.Should().Be(Path.Combine(p.JccDirectory, p.SettingsFileName));
    }

    [Fact]
    public void AuthFilePath_CombinesJccDirectoryAndAuthFileName() {
        var p = Make();
        p.AuthFilePath.Should().Be(Path.Combine(p.JccDirectory, p.AuthFileName));
    }

    [Fact]
    public void GlobalConfigFilePath_CombinesJccDirectoryAndGlobalConfigFileName() {
        var p = Make();
        p.GlobalConfigFilePath.Should().Be(Path.Combine(p.JccDirectory, p.GlobalConfigFileName));
    }

    [Fact]
    public void TokensDirectory_CombinesJccDirectoryAndTokens() {
        var p = Make();
        p.TokensDirectory.Should().Be(Path.Combine(p.JccDirectory, "tokens"));
    }

    [Fact]
    public void CronTasksDirectory_CombinesJccDirectoryAndCronTasks() {
        var p = Make();
        p.CronTasksDirectory.Should().Be(Path.Combine(p.JccDirectory, "cron-tasks"));
    }

    [Fact]
    public void MemdirDirectory_CombinesJccDirectoryAndMemdir() {
        var p = Make();
        p.MemdirDirectory.Should().Be(Path.Combine(p.JccDirectory, "memdir"));
    }

    [Fact]
    public void CostTrackingFilePath_CombinesJccDirectoryAndCostTracking() {
        var p = Make();
        p.CostTrackingFilePath.Should().Be(Path.Combine(p.JccDirectory, "cost-tracking.json"));
    }

    [Fact]
    public void SessionsDirectory_CombinesJccDirectoryAndSessionsFolderName() {
        var p = Make();
        p.SessionsDirectory.Should().Be(Path.Combine(p.JccDirectory, p.SessionsFolderName));
    }

    [Fact]
    public void PlansDirectory_CombinesJccDirectoryAndPlansFolderName() {
        var p = Make();
        p.PlansDirectory.Should().Be(Path.Combine(p.JccDirectory, p.PlansFolderName));
    }

    [Fact]
    public void McpDirectory_CombinesJccDirectoryAndMcpFolderName() {
        var p = Make();
        p.McpDirectory.Should().Be(Path.Combine(p.JccDirectory, p.McpFolderName));
    }

    [Fact]
    public void AgentsDirectory_CombinesJccDirectoryAndAgentsFolderName() {
        var p = Make();
        p.AgentsDirectory.Should().Be(Path.Combine(p.JccDirectory, p.AgentsFolderName));
    }

    [Fact]
    public void JccDirectory_WithRootedAppDataFolder_ReturnsAppDataFolder() {
        // 根路径直接返回,不拼接 UserProfile
        var rooted = Make("/tmp/jcc-test");
        rooted.JccDirectory.Should().Be("/tmp/jcc-test");
    }

    [Fact]
    public void JccDirectory_WithRelativeAppDataFolder_CombinesWithUserProfile() {
        var p = Make(".jcc");
        if (Path.IsPathRooted(p.AppDataFolder)) {
            // 极端环境:AppDataFolder 被解析为根路径 → JccDirectory == AppDataFolder
            p.JccDirectory.Should().Be(p.AppDataFolder);
        } else {
            p.JccDirectory.Should().Be(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                p.AppDataFolder));
        }
    }

    [Fact]
    public void ProjectJccDirectory_CombinesCurrentDirectoryAndAppDataFolder() {
        var p = Make(".jcc");
        p.ProjectJccDirectory.Should().Be(Path.Combine(Environment.CurrentDirectory, p.AppDataFolder));
    }

    [Fact]
    public void WorkflowStatesDirectory_CombinesProjectJccAndWorkflowStates() {
        var p = Make();
        p.WorkflowStatesDirectory.Should().Be(Path.Combine(p.ProjectJccDirectory, "workflow-states"));
    }

    [Fact]
    public void RuntimeDirectory_CombinesBaseDirectoryAndRuntime() {
        var p = Make();
        p.RuntimeDirectory.Should().Be(Path.Combine(AppContext.BaseDirectory, "runtime"));
    }

    [Fact]
    public void UserRuntimeErrorLogPath_CombinesUserRuntimeAndLog() {
        var p = Make();
        p.UserRuntimeErrorLogPath.Should().Be(Path.Combine(p.UserRuntimeDirectory, "jcc_error.log"));
    }

    [Fact]
    public void CreateForTest_OverridesSpecifiedFields() {
        var p = AppDataPaths.CreateForTest(appDataFolder: ".test", settingsFileName: "s.json", authFileName: "a.json");
        p.AppDataFolder.Should().Be(".test");
        p.SettingsFileName.Should().Be("s.json");
        p.AuthFileName.Should().Be("a.json");
    }

    [Fact]
    public void CreateForTest_DefaultsPreservedForUnspecified() {
        var p = AppDataPaths.CreateForTest();
        // 未指定的字段使用 Default 值(非空)
        p.CredentialsFileName.Should().NotBeEmpty();
        p.RulesFolderName.Should().NotBeEmpty();
        p.McpFolderName.Should().NotBeEmpty();
    }

    [Fact]
    public void RecordEquality_HoldsForSameArgs() {
        var p1 = Make();
        var p2 = Make();
        p1.Should().Be(p2);
    }

    [Fact]
    public void RecordInequality_DiffersForDifferentFolder() {
        var p1 = Make(".a");
        var p2 = Make(".b");
        p1.Should().NotBe(p2);
    }

    // 注:FromEnvironment() 依赖 Environment.GetEnvironmentVariable,非确定性,跳过测试。
    // Default 静态属性触发 FromEnvironment(),CreateForTest 内部访问 Default,
    // 但仅读取有默认值的 Env 变量,不会抛异常,组合关系断言不依赖具体 Env 值。
}
