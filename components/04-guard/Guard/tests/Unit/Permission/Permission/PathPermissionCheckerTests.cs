namespace Core.Tests.Permission;

public class PathPermissionCheckerTests
{
    private readonly string _workingDir;

    public PathPermissionCheckerTests()
    {
        _workingDir = Path.GetFullPath(@"C:\Projects\MyApp");
    }

    #region CheckReadPermission — 对齐 TS checkReadPermissionForTool 9步决策链

    [Fact]
    public void CheckReadPermission_UncPath_ShouldReturnAsk()
    {
        var checker = CreateChecker();

        var result = checker.CheckReadPermission(@"\\server\share\file.txt");

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().Contain("UNC");
    }

    [Fact]
    public void CheckReadPermission_UncPathForwardSlash_ShouldReturnAsk()
    {
        var checker = CreateChecker();

        var result = checker.CheckReadPermission("//server/share/file.txt");

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().Contain("UNC");
    }

    [Fact]
    public void CheckReadPermission_SuspiciousWindowsPath_ShouldReturnAsk()
    {
        var checker = CreateChecker();

        // NTFS ADS 路径
        var result = checker.CheckReadPermission(@"C:\test\file.txt:Zone.Identifier");

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().Contain("可疑");
    }

    [Fact]
    public void CheckReadPermission_DenyRule_ShouldReturnDeny()
    {
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Deny,
                Pattern = @"/secrets/**",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckReadPermission(@"C:\secrets\api_keys.json");

        result.Decision.Should().Be(PermissionBehavior.Deny);
        result.MatchedRule.Should().NotBeNull();
    }

    [Fact]
    public void CheckReadPermission_AskRule_OutsideWorkingDir_ShouldReturnAsk()
    {
        // ask 规则匹配的路径必须在工作目录外，否则步骤5（编辑权限隐含读取）会先匹配
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Ask,
                Pattern = ".env",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckReadPermission(@"C:\OtherProject\.env");

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.MatchedRule.Should().NotBeNull();
    }

    [Fact]
    public void CheckReadPermission_EditAllowImpliesRead_ShouldReturnAllow()
    {
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Edit,
                Behavior = PermissionBehavior.Allow,
                Pattern = @"/Projects/MyApp/**",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckReadPermission(@"C:\Projects\MyApp\src\Program.cs");

        result.Decision.Should().Be(PermissionBehavior.Allow);
        result.Reason.Should().Contain("编辑权限隐含");
    }

    [Fact]
    public void CheckReadPermission_InWorkingDirectory_ShouldReturnAllow()
    {
        var checker = CreateChecker();

        var result = checker.CheckReadPermission(@"C:\Projects\MyApp\src\Program.cs");

        result.Decision.Should().Be(PermissionBehavior.Allow);
        // 步骤5（编辑权限隐含读取）在步骤6（工作目录）之前，工作目录内的路径可能由步骤5或步骤6匹配
    }

    [Fact]
    public void CheckReadPermission_InAdditionalDirectory_ShouldReturnAllow()
    {
        var additionalDirs = new List<string> { @"C:\SharedLibs" };
        var checker = CreateChecker(additionalDirectories: additionalDirs);

        var result = checker.CheckReadPermission(@"C:\SharedLibs\Utils.cs");

        result.Decision.Should().Be(PermissionBehavior.Allow);
        // 步骤5（编辑权限隐含读取）在步骤6（工作目录）之前，额外目录内的路径可能由步骤5或步骤6匹配
    }

    [Fact]
    public void CheckReadPermission_InternalPath_Tasks_ShouldReturnAllow()
    {
        var checker = CreateChecker();
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tasksPath = Path.Combine(homeDir, AppDataConstants.AppDataFolder, AppDataConstants.TasksFolderName, "task1.json");

        var result = checker.CheckReadPermission(tasksPath);

        result.Decision.Should().Be(PermissionBehavior.Allow);
        result.Reason.Should().Contain("任务");
    }

    [Fact]
    public void CheckReadPermission_InternalPath_Teams_ShouldReturnAllow()
    {
        var checker = CreateChecker();
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var teamsPath = Path.Combine(homeDir, AppDataConstants.AppDataFolder, AppDataConstants.TeamsFolderName, "team1.json");

        var result = checker.CheckReadPermission(teamsPath);

        result.Decision.Should().Be(PermissionBehavior.Allow);
        result.Reason.Should().Contain("团队");
    }

    [Fact]
    public void CheckReadPermission_AllowRule_ShouldReturnAllow()
    {
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Allow,
                Pattern = @"/ExternalData/**",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckReadPermission(@"C:\ExternalData\report.csv");

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    [Fact]
    public void CheckReadPermission_OutsideWorkingDir_NoRules_ShouldReturnAsk()
    {
        var checker = CreateChecker();

        var result = checker.CheckReadPermission(@"C:\OtherProject\file.txt");

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().Contain("工作目录之外");
    }

    [Fact]
    public void CheckReadPermission_DenyRuleBeforeEditAllow_ShouldReturnDeny()
    {
        // 安全关键: deny 规则必须在编辑权限隐含读取之前
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Deny,
                Pattern = @"/secrets/**",
                Source = PathPermissionRuleSource.UserSettings
            },
            new()
            {
                ToolType = PathPermissionToolType.Edit,
                Behavior = PermissionBehavior.Allow,
                Pattern = @"/secrets/**",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckReadPermission(@"C:\secrets\config.json");

        // deny 必须优先于 edit allow
        result.Decision.Should().Be(PermissionBehavior.Deny);
    }

    [Fact]
    public void CheckReadPermission_AskRuleBeforeEditAllow_ShouldReturnAsk()
    {
        // 安全关键: ask 规则必须在编辑权限隐含读取之前
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Ask,
                Pattern = @"/sensitive/**",
                Source = PathPermissionRuleSource.UserSettings
            },
            new()
            {
                ToolType = PathPermissionToolType.Edit,
                Behavior = PermissionBehavior.Allow,
                Pattern = @"/sensitive/**",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckReadPermission(@"C:\sensitive\data.json");

        // ask 必须优先于 edit allow
        result.Decision.Should().Be(PermissionBehavior.Ask);
    }

    #endregion

    #region CheckWritePermission

    [Fact]
    public void CheckWritePermission_InWorkingDirectory_ShouldReturnAllow()
    {
        var checker = CreateChecker();

        var result = checker.CheckWritePermission(@"C:\Projects\MyApp\src\Program.cs");

        result.Decision.Should().Be(PermissionBehavior.Allow);
        result.Reason.Should().Contain("工作目录");
    }

    [Fact]
    public void CheckWritePermission_DenyRule_ShouldReturnDeny()
    {
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Edit,
                Behavior = PermissionBehavior.Deny,
                Pattern = @"/protected/**",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckWritePermission(@"C:\protected\file.cs");

        result.Decision.Should().Be(PermissionBehavior.Deny);
    }

    [Fact]
    public void CheckWritePermission_OutsideWorkingDir_ShouldReturnAsk()
    {
        var checker = CreateChecker();

        var result = checker.CheckWritePermission(@"C:\OtherProject\file.txt");

        result.Decision.Should().Be(PermissionBehavior.Ask);
        result.Reason.Should().Contain("工作目录之外");
    }

    [Fact]
    public void CheckWritePermission_UncPath_ShouldReturnAsk()
    {
        var checker = CreateChecker();

        var result = checker.CheckWritePermission(@"\\server\share\file.txt");

        result.Decision.Should().Be(PermissionBehavior.Ask);
    }

    #endregion

    #region 通配符匹配

    [Fact]
    public void CheckReadPermission_DoubleStarPattern_ShouldMatchSubdirectories()
    {
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Allow,
                Pattern = @"/ExternalData/**",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckReadPermission(@"C:\ExternalData\sub\dir\file.csv");

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    [Fact]
    public void CheckReadPermission_ExtensionPattern_ShouldMatchExtension()
    {
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Deny,
                Pattern = "*.pem",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckReadPermission(@"C:\Projects\MyApp\cert.pem");

        result.Decision.Should().Be(PermissionBehavior.Deny);
    }

    #endregion

    #region 边界条件

    [Fact]
    public void CheckReadPermission_NullPath_ShouldThrow()
    {
        var checker = CreateChecker();

        var act = () => checker.CheckReadPermission(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CheckReadPermission_EmptyPath_ShouldThrow()
    {
        var checker = CreateChecker();

        var act = () => checker.CheckReadPermission("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CheckReadPermission_RelativePath_ShouldResolveAndCheck()
    {
        var checker = CreateChecker();

        // 相对路径应被 Path.GetFullPath 解析
        // 解析后可能不在 _workingDir 内（取决于当前工作目录）
        // 但不应抛异常
        var result = checker.CheckReadPermission("src/Program.cs");

        // 结果取决于当前工作目录，只需验证不抛异常
        result.Should().NotBeNull();
    }

    #endregion

    #region 安全边界测试 — 炸弹15: 路径段绕过、路径遍历、Contains 误匹配

    [Fact]
    public void CheckReadPermission_PathSegmentBypass_ShouldNotAllowSiblingDirectory()
    {
        // 安全关键: C:\Projects\MyAppSecret 不应绕过 C:\Projects\MyApp 的工作目录检查
        var checker = CreateChecker();

        var result = checker.CheckReadPermission(@"C:\Projects\MyAppSecret\file.txt");

        result.Decision.Should().Be(PermissionBehavior.Ask);
    }

    [Fact]
    public void CheckReadPermission_PathTraversal_ShouldResolveAndDeny()
    {
        // 安全关键: C:\Projects\MyApp\..\OtherProject\file.txt 应解析为 C:\Projects\OtherProject\file.txt
        var checker = CreateChecker();

        var result = checker.CheckReadPermission(@"C:\Projects\MyApp\..\OtherProject\file.txt");

        // Path.GetFullPath 会解析 .. → C:\Projects\OtherProject\file.txt，不在工作目录内
        result.Decision.Should().Be(PermissionBehavior.Ask);
    }

    [Fact]
    public void CheckReadPermission_ExtensionPattern_ShouldOnlyMatchFilename()
    {
        // 安全关键: *.pem 不应匹配路径中包含 .pem 的目录名
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Deny,
                Pattern = "*.pem",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        // 目录名含 .pem 但文件扩展名是 .txt — 不应匹配
        var result = checker.CheckReadPermission(@"C:\Projects\MyApp\pem_folder\readme.txt");

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    [Fact]
    public void CheckReadPermission_DenyRuleBeforeWorkingDirectory_ShouldReturnDeny()
    {
        // 安全关键: deny 规则必须优先于工作目录隐式 allow
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Deny,
                Pattern = @"/Projects/MyApp/secrets/**",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckReadPermission(@"C:\Projects\MyApp\secrets\api_keys.json");

        result.Decision.Should().Be(PermissionBehavior.Deny);
    }

    [Fact]
    public void CheckReadPermission_WorkingDirectoryItself_ShouldReturnAllow()
    {
        var checker = CreateChecker();

        // 工作目录本身也应允许读取
        var result = checker.CheckReadPermission(@"C:\Projects\MyApp");

        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    [Fact]
    public void CheckReadPermission_AdditionalDirectorySegmentBypass_ShouldNotAllow()
    {
        // 安全关键: C:\SharedLibsExtra 不应绕过 C:\SharedLibs 的额外目录检查
        var additionalDirs = new List<string> { @"C:\SharedLibs" };
        var checker = CreateChecker(additionalDirectories: additionalDirs);

        var result = checker.CheckReadPermission(@"C:\SharedLibsExtra\file.txt");

        result.Decision.Should().Be(PermissionBehavior.Ask);
    }

    [Fact]
    public void CheckReadPermission_FilenamePattern_ShouldMatchExactFilename()
    {
        // 纯文件名模式 ".env" 应匹配任何路径下的 .env 文件
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Deny,
                Pattern = ".env",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        var result = checker.CheckReadPermission(@"C:\Projects\MyApp\.env");

        result.Decision.Should().Be(PermissionBehavior.Deny);
    }

    [Fact]
    public void CheckReadPermission_FilenamePattern_ShouldNotMatchPartialFilename()
    {
        // 纯文件名模式 ".env" 不应匹配 ".envrc" 或 "test.env.backup"
        var rules = new List<PathPermissionRule>
        {
            new()
            {
                ToolType = PathPermissionToolType.Read,
                Behavior = PermissionBehavior.Deny,
                Pattern = ".env",
                Source = PathPermissionRuleSource.UserSettings
            }
        };
        var checker = CreateChecker(rules: rules);

        // .envrc 不等于 .env
        var result = checker.CheckReadPermission(@"C:\Projects\MyApp\.envrc");

        // .envrc 不匹配 .env，但路径在工作目录内，所以 Allow
        result.Decision.Should().Be(PermissionBehavior.Allow);
    }

    #endregion

    private PathPermissionChecker CreateChecker(
        IReadOnlyList<string>? additionalDirectories = null,
        IReadOnlyList<PathPermissionRule>? rules = null)
    {
        return new PathPermissionChecker(
            new IO.FileSystem.PhysicalFileSystem(),
            _workingDir,
            additionalDirectories ?? [],
            rules ?? [],
            NullLogger<PathPermissionChecker>.Instance);
    }

    #region GetReadDenyPatterns — 对齐 TS getFileReadIgnorePatterns

    [Fact]
    public void GetReadDenyPatterns_NoRules_ReturnsEmpty()
    {
        var checker = CreateChecker();

        var patterns = checker.GetReadDenyPatterns();

        patterns.Should().BeEmpty();
    }

    [Fact]
    public void GetReadDenyPatterns_WithDenyRules_ReturnsNormalizedPatterns()
    {
        var rules = new List<PathPermissionRule>
        {
            new() { ToolType = PathPermissionToolType.Read, Behavior = PermissionBehavior.Deny, Pattern = "secrets/**", Source = PathPermissionRuleSource.UserSettings },
            new() { ToolType = PathPermissionToolType.Read, Behavior = PermissionBehavior.Deny, Pattern = ".env", Source = PathPermissionRuleSource.UserSettings },
        };

        var checker = CreateChecker(rules: rules);

        var patterns = checker.GetReadDenyPatterns();

        patterns.Should().HaveCount(2);
        patterns.Should().Contain("secrets");
        patterns.Should().Contain(".env");
    }

    [Fact]
    public void GetReadDenyPatterns_IgnoresNonDenyRules()
    {
        var rules = new List<PathPermissionRule>
        {
            new() { ToolType = PathPermissionToolType.Read, Behavior = PermissionBehavior.Allow, Pattern = "src/**", Source = PathPermissionRuleSource.UserSettings },
            new() { ToolType = PathPermissionToolType.Read, Behavior = PermissionBehavior.Ask, Pattern = "config/**", Source = PathPermissionRuleSource.UserSettings },
            new() { ToolType = PathPermissionToolType.Read, Behavior = PermissionBehavior.Deny, Pattern = "secrets/**", Source = PathPermissionRuleSource.UserSettings },
        };

        var checker = CreateChecker(rules: rules);

        var patterns = checker.GetReadDenyPatterns();

        patterns.Should().HaveCount(1);
        patterns[0].Should().Be("secrets");
    }

    [Fact]
    public void GetReadDenyPatterns_AbsolutePathInWorkDir_ConvertsToRelative()
    {
        var rules = new List<PathPermissionRule>
        {
            new() { ToolType = PathPermissionToolType.Read, Behavior = PermissionBehavior.Deny, Pattern = @"C:\Projects\MyApp\secrets\**", Source = PathPermissionRuleSource.UserSettings },
        };

        var checker = CreateChecker(rules: rules);

        var patterns = checker.GetReadDenyPatterns();

        patterns.Should().HaveCount(1);
        patterns[0].Should().Be("secrets");
    }

    [Fact]
    public void GetReadDenyPatterns_AbsolutePathOutsideWorkDir_SkipsPattern()
    {
        var rules = new List<PathPermissionRule>
        {
            new() { ToolType = PathPermissionToolType.Read, Behavior = PermissionBehavior.Deny, Pattern = @"C:\OtherProject\secrets\**", Source = PathPermissionRuleSource.UserSettings },
        };

        var checker = CreateChecker(rules: rules);

        var patterns = checker.GetReadDenyPatterns();

        patterns.Should().BeEmpty();
    }

    [Fact]
    public void GetReadDenyPatterns_IgnoresEditDenyRules()
    {
        var rules = new List<PathPermissionRule>
        {
            new() { ToolType = PathPermissionToolType.Edit, Behavior = PermissionBehavior.Deny, Pattern = "secrets/**", Source = PathPermissionRuleSource.UserSettings },
        };

        var checker = CreateChecker(rules: rules);

        var patterns = checker.GetReadDenyPatterns();

        patterns.Should().BeEmpty();
    }

    #endregion
}
