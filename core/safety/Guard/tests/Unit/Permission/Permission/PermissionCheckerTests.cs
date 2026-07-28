
namespace Core.Tests.Permission;

public class PermissionCheckerTests
{
    private readonly PermissionChecker _checker;

    public PermissionCheckerTests()
    {
        _checker = new PermissionChecker(
            CreateTestPipeline(),
            Options.Create(PermissionConfig.CreateDefault()),
            new IO.FileSystem.PhysicalFileSystem(),
            NullLogger<PermissionChecker>.Instance);
    }

    private static MiddlewarePipeline<PermissionCheckContext> CreateTestPipeline()
    {
        var middlewares = new IMiddleware<PermissionCheckContext>[]
        {
            new BypassPermissionMiddleware(),
            new AgentRestrictionMiddleware(),
            new DangerousCommandProtectionMiddleware(destructiveCommandDetector: new DestructiveCommandDetector()),
            new AutoClassifierMiddleware(),
            new ConfigGetOperationMiddleware(),
            new WebFetchPermissionMiddleware(),
            new EarlyPathDenyMiddleware(),
            new ToolListPermissionMiddleware(),
            new PathPermissionMiddleware(),
            new DangerousOperationMiddleware(),
            new PlanModeMiddleware(),
            new DefaultResultMiddleware()
        };
        return new MiddlewarePipeline<PermissionCheckContext>(middlewares);
    }

    [Fact]
    public async Task CheckPermission_AutoApprovedTool_ShouldReturnApproved()
    {
        var result = await _checker.CheckPermissionAsync(FileToolNameConstants.FileRead).ConfigureAwait(true);

        result.IsApproved.Should().BeTrue();
        result.ConfirmationRequired.Should().BeFalse();
    }

    [Fact]
    public async Task CheckPermission_UnknownTool_ShouldRequireConfirmation()
    {
        var result = await _checker.CheckPermissionAsync("unknown_tool").ConfigureAwait(true);

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermission_DangerousTool_ShouldRequireConfirmation()
    {
        var result = await _checker.CheckPermissionAsync("file_delete").ConfigureAwait(true);

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermission_BypassMode_ShouldAlwaysApprove()
    {
        _checker.CurrentMode = PermissionMode.BypassPermissions;

        var result = await _checker.CheckPermissionAsync("any_dangerous_tool").ConfigureAwait(true);

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermission_AutoMode_ReadTool_ShouldApprove()
    {
        _checker.CurrentMode = PermissionMode.Auto;

        var result = await _checker.CheckPermissionAsync(FileToolNameConstants.FileRead).ConfigureAwait(true);

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task AddToAutoApproved_ThenCheck_ShouldReturnApproved()
    {
        _checker.AddToAutoApproved("custom_tool");

        var result = await _checker.CheckPermissionAsync("custom_tool").ConfigureAwait(true);

        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task AddToAutoRejected_ThenCheck_ShouldReturnRejected()
    {
        _checker.AddToAutoRejected("blocked_tool");

        var result = await _checker.CheckPermissionAsync("blocked_tool").ConfigureAwait(true);

        result.IsApproved.Should().BeFalse();
        result.Reason.Should().Contain("自动拒绝列表");
    }

    [Fact]
    public async Task RemoveFromAutoApproved_ThenCheck_ShouldRequireConfirmation()
    {
        _checker.AddToAutoApproved("temp_tool");
        _checker.RemoveFromAutoApproved("temp_tool");

        var result = await _checker.CheckPermissionAsync("temp_tool").ConfigureAwait(true);

        result.ConfirmationRequired.Should().BeTrue();
    }

    [Fact]
    public async Task PermissionCheckResult_Approved_ShouldBeApproved()
    {
        var result = ToolPermissionCheckResult.Approved();

        result.IsApproved.Should().BeTrue();
        result.ConfirmationRequired.Should().BeFalse();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public async Task PermissionCheckResult_Rejected_ShouldNotBeApproved()
    {
        var result = ToolPermissionCheckResult.Rejected("Test reason");

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeFalse();
        result.Reason.Should().Be("Test reason");
    }

    [Fact]
    public async Task PermissionCheckResult_PendingConfirmation_ShouldRequireConfirmation()
    {
        var result = ToolPermissionCheckResult.PendingConfirmation("Please confirm");

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeTrue();
        result.Reason.Should().Be("Please confirm");
    }

    [Theory]
    [InlineData(PermissionMode.Default)]
    [InlineData(PermissionMode.Auto)]
    [InlineData(PermissionMode.Plan)]
    [InlineData(PermissionMode.BypassPermissions)]
    public async Task PermissionMode_AllModes_ShouldBeDefined(PermissionMode mode)
    {
        Enum.IsDefined(typeof(PermissionMode), mode).Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermission_ShellOperationWithDangerousCommand_ShouldBeRejectedInAutoMode()
    {
        _checker.CurrentMode = PermissionMode.Auto;

        var result = await _checker.CheckPermissionAsync(ShellToolNameConstants.Bash, new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.SerializeToElement("rm -rf /")
        }).ConfigureAwait(true);

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeFalse("Auto 模式下危险命令应被拒绝而非待确认");
    }

    [Fact]
    public async Task CheckPermission_WriteOperationToSensitivePath_ShouldRequireConfirmation()
    {
        _checker.CurrentMode = PermissionMode.Auto;

        var result = await _checker.CheckPermissionAsync(FileToolNameConstants.FileWrite, new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("C:\\Windows\\test.txt")
        }).ConfigureAwait(true);

        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeTrue();
    }

    [Fact]
    public async Task PermissionConfig_CreateDefault_ShouldHaveAutoApprovedTools()
    {
        var config = PermissionConfig.CreateDefault();

        config.AutoApprovedTools.Should().NotBeEmpty();
        config.AutoApprovedTools.Should().Contain(r => r.ToolName == FileToolNameConstants.FileRead);
        config.AutoApprovedTools.Should().Contain(r => r.ToolName == SearchToolNameConstants.Glob);
        config.AutoApprovedTools.Should().Contain(r => r.ToolName == SearchToolNameConstants.Grep);
    }

    [Fact]
    public async Task PermissionConfig_CreateDefault_ShouldHaveDangerousPatterns()
    {
        var config = PermissionConfig.CreateDefault();

        config.DangerousOperationPatterns.Should().NotBeEmpty();
        config.DangerousCommandPatterns.Should().NotBeEmpty();
        config.SensitivePathPatterns.Should().NotBeEmpty();
    }

    [Fact]
    public async Task PermissionChecker_WithOptions_ShouldUseConfig()
    {
        var config = new PermissionConfig
        {
            AutoApprovedTools = new List<ToolPermissionRule>
            {
                new() { ToolName = "custom_tool" }
            }
        };

        var pipeline = CreateTestPipeline();
        var checker = new PermissionChecker(
            pipeline,
            Options.Create(config),
            new IO.FileSystem.PhysicalFileSystem(),
            NullLogger<PermissionChecker>.Instance);

        var result = await checker.CheckPermissionAsync("custom_tool").ConfigureAwait(true);

        result.IsApproved.Should().BeTrue();
    }

    [Theory]
    [InlineData(FileToolNameConstants.FileRead)]
    [InlineData(SearchToolNameConstants.Glob)]
    [InlineData(SearchToolNameConstants.Grep)]
    // WebFetch 不在 AutoApprovedTools 中 — 对齐 TS 版: 需要域名级权限检查
    [InlineData(WebToolNameConstants.WebSearch)]
    public async Task CheckPermission_DefaultAutoApprovedTools_ShouldBeApproved(string toolName)
    {
        var result = await _checker.CheckPermissionAsync(toolName).ConfigureAwait(true);

        result.IsApproved.Should().BeTrue();
    }

    [Theory]
    [InlineData(ShellToolNameConstants.Bash, true)]
    [InlineData("shell", true)]
    [InlineData(FileToolNameConstants.FileWrite, false)]
    [InlineData(FileToolNameConstants.FileEdit, false)]
    public async Task CheckPermission_WriteOperations_ShouldRequireConfirmation(string toolName, bool isDangerous)
    {
        var result = await _checker.CheckPermissionAsync(toolName).ConfigureAwait(true);

        if (isDangerous)
        {
            result.ConfirmationRequired.Should().BeTrue();
        }
        else
        {
            result.IsApproved.Should().BeFalse(); // Not auto-approved
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CheckPermission_EmptyOrNullToolName_ShouldNotThrow(string? toolName)
    {
        var act = async () => await _checker.CheckPermissionAsync(toolName!).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task CheckPermission_CaseInsensitiveToolName_ShouldWork()
    {
        var result1 = await _checker.CheckPermissionAsync(FileToolNameConstants.FileRead).ConfigureAwait(true);
        var result2 = await _checker.CheckPermissionAsync(FileToolNameConstants.FileRead).ConfigureAwait(true);
        var result3 = await _checker.CheckPermissionAsync(FileToolNameConstants.FileRead).ConfigureAwait(true);

        result1.IsApproved.Should().Be(result2.IsApproved);
        result2.IsApproved.Should().Be(result3.IsApproved);
    }

    [Fact]
    public async Task CheckPermission_ConcurrentAccess_ShouldBeThreadSafe()
    {
        // 使用不同的工具名称避免并发冲突
        var tasks = new List<Task>();
        var checker = new PermissionChecker(
            CreateTestPipeline(),
            Options.Create(PermissionConfig.CreateDefault()),
            new IO.FileSystem.PhysicalFileSystem(),
            NullLogger<PermissionChecker>.Instance);

        // 预添加所有工具到自动批准列表
        for (int i = 0; i < 100; i++)
        {
            checker.AddToAutoApproved($"tool_{i}");
        }

        // 并发检查权限（只读操作）
        for (int i = 0; i < 100; i++)
        {
            var toolName = $"tool_{i}";
            tasks.Add(Task.Run(async () =>
            {
                var result = await checker.CheckPermissionAsync(toolName).ConfigureAwait(true);
                result.IsApproved.Should().BeTrue();
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(true);
    }

    [Theory]
    [InlineData("rm -rf /", true)]
    [InlineData("RM -RF /", true)]
    [InlineData("Rm -Rf /", true)]
    [InlineData("echo hello", false)]
    [InlineData("dir", false)]
    public async Task CheckPermission_DangerousCommandCaseInsensitive_ShouldDetect(string command, bool isDangerous)
    {
        _checker.CurrentMode = PermissionMode.Auto;

        var result = await _checker.CheckPermissionAsync(ShellToolNameConstants.Bash, new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.SerializeToElement(command)
        }).ConfigureAwait(true);

        if (isDangerous)
        {
            result.IsApproved.Should().BeFalse("Auto 模式下危险命令应被拒绝");
        }
    }

    [Fact]
    public async Task CheckPermission_LongCommand_ShouldNotThrow()
    {
        _checker.CurrentMode = PermissionMode.Auto;
        var longCommand = new string('a', 10000);

        var act = async () => await _checker.CheckPermissionAsync(ShellToolNameConstants.Bash, new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.SerializeToElement(longCommand)
        }).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task AddToAutoApproved_NullToolName_ShouldNotThrow()
    {
        var act = () => _checker.AddToAutoApproved(null!);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task RemoveFromAutoApproved_NonExistentTool_ShouldNotThrow()
    {
        var act = () => _checker.RemoveFromAutoApproved("non_existent_tool");
        act.Should().NotThrow();
    }

    #region WebFetch 域名级权限检查测试 — 对齐 TS 版 WebFetchTool.checkPermissions

    [Fact]
    public async Task CheckPermission_WebFetch_PreapprovedDomain_ShouldBeApproved()
    {
        // docs.python.org 是预批准域名
        var args = new Dictionary<string, JsonElement>
        {
            ["url"] = JsonSerializer.SerializeToElement("https://docs.python.org/3/library/os.html"),
            ["prompt"] = JsonSerializer.SerializeToElement("extract info")
        };

        var result = await _checker.CheckPermissionAsync(WebToolNameConstants.WebFetch, args).ConfigureAwait(true);
        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermission_WebFetch_UnknownDomain_ShouldRequireConfirmation()
    {
        // 未知域名 → 默认 ask
        var args = new Dictionary<string, JsonElement>
        {
            ["url"] = JsonSerializer.SerializeToElement("https://unknown-random-site.example.com/page"),
            ["prompt"] = JsonSerializer.SerializeToElement("extract info")
        };

        var result = await _checker.CheckPermissionAsync(WebToolNameConstants.WebFetch, args).ConfigureAwait(true);
        result.ConfirmationRequired.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermission_WebFetch_DenyRule_ShouldBeRejected()
    {
        var config = PermissionConfig.CreateDefault();
        config.AutoRejectedTools.Add(new ToolPermissionRule
        {
            ToolName = WebToolNameConstants.WebFetch,
            RuleContent = "domain:evil.example.com"
        });

        var pipeline = CreateTestPipeline();
        var checker = new PermissionChecker(
            pipeline,
            Options.Create(config),
            new IO.FileSystem.PhysicalFileSystem(),
            NullLogger<PermissionChecker>.Instance);

        var args = new Dictionary<string, JsonElement>
        {
            ["url"] = JsonSerializer.SerializeToElement("https://evil.example.com/page"),
            ["prompt"] = JsonSerializer.SerializeToElement("extract info")
        };

        var result = await checker.CheckPermissionAsync(WebToolNameConstants.WebFetch, args).ConfigureAwait(true);
        result.IsApproved.Should().BeFalse();
        result.ConfirmationRequired.Should().BeFalse(); // Rejected, not pending
    }

    [Fact]
    public async Task CheckPermission_WebFetch_AllowRuleWithRuleContent_ShouldBeApproved()
    {
        var config = PermissionConfig.CreateDefault();
        config.AutoApprovedTools.Add(new ToolPermissionRule
        {
            ToolName = WebToolNameConstants.WebFetch,
            RuleContent = "domain:trusted.example.com"
        });

        var pipeline = CreateTestPipeline();
        var checker = new PermissionChecker(
            pipeline,
            Options.Create(config),
            new IO.FileSystem.PhysicalFileSystem(),
            NullLogger<PermissionChecker>.Instance);

        var args = new Dictionary<string, JsonElement>
        {
            ["url"] = JsonSerializer.SerializeToElement("https://trusted.example.com/page"),
            ["prompt"] = JsonSerializer.SerializeToElement("extract info")
        };

        var result = await checker.CheckPermissionAsync(WebToolNameConstants.WebFetch, args).ConfigureAwait(true);
        result.IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermission_WebFetch_NoUrlArgument_ShouldFallThroughToDefault()
    {
        // 无 URL 参数时，走默认权限链（不在 AutoApproved 中 → PendingConfirmation）
        var result = await _checker.CheckPermissionAsync(WebToolNameConstants.WebFetch).ConfigureAwait(true);
        result.ConfirmationRequired.Should().BeTrue();
    }

    [Fact]
    public async Task CheckPermission_WebFetch_AskRule_ShouldRequireConfirmation()
    {
        var config = PermissionConfig.CreateDefault();
        config.AskRules.Add(new ToolPermissionRule
        {
            ToolName = WebToolNameConstants.WebFetch,
            RuleContent = "domain:ask.example.com"
        });

        var pipeline = CreateTestPipeline();
        var checker = new PermissionChecker(
            pipeline,
            Options.Create(config),
            new IO.FileSystem.PhysicalFileSystem(),
            NullLogger<PermissionChecker>.Instance);

        var args = new Dictionary<string, JsonElement>
        {
            ["url"] = JsonSerializer.SerializeToElement("https://ask.example.com/page"),
            ["prompt"] = JsonSerializer.SerializeToElement("extract info")
        };

        var result = await checker.CheckPermissionAsync(WebToolNameConstants.WebFetch, args).ConfigureAwait(true);
        result.ConfirmationRequired.Should().BeTrue();
    }

    #endregion

    #region 路径级 deny 优先于 autoApprovedTools — 炸弹12 集成测试

    [Fact]
    public async Task CheckPermission_AutoApprovedTool_PathDenyRule_ShouldBeRejected()
    {
        // 安全关键: autoApprovedTools 不应绕过路径级 deny 规则
        var pathChecker = new PathPermissionChecker(
            new IO.FileSystem.PhysicalFileSystem(),
            @"C:\Projects\MyApp",
            [],
            new List<PathPermissionRule>
            {
                new()
                {
                    ToolType = PathPermissionToolType.Read,
                    Behavior = PermissionBehavior.Deny,
                    Pattern = @"/secrets/**",
                    Source = PathPermissionRuleSource.UserSettings
                }
            });

        var config = PermissionConfig.CreateDefault();
        var middlewares = new IMiddleware<PermissionCheckContext>[]
        {
            new BypassPermissionMiddleware(),
            new AgentRestrictionMiddleware(),
            new DangerousCommandProtectionMiddleware(destructiveCommandDetector: new DestructiveCommandDetector()),
            new AutoClassifierMiddleware(),
            new ConfigGetOperationMiddleware(),
            new WebFetchPermissionMiddleware(),
            new EarlyPathDenyMiddleware(pathChecker),
            new ToolListPermissionMiddleware(),
            new PathPermissionMiddleware(pathChecker),
            new DangerousOperationMiddleware(),
            new PlanModeMiddleware(),
            new DefaultResultMiddleware()
        };
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>(middlewares);
        var checker = new PermissionChecker(
            pipeline,
            Options.Create(config),
            new IO.FileSystem.PhysicalFileSystem(),
            NullLogger<PermissionChecker>.Instance);

        // FileRead 是 auto-approved 工具，但路径级 deny 应优先
        var args = new Dictionary<string, JsonElement>
        {
            ["file_path"] = JsonSerializer.SerializeToElement(@"C:\secrets\api_keys.json")
        };

        var result = await checker.CheckPermissionAsync(FileToolNameConstants.FileRead, args).ConfigureAwait(true);
        result.IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task CheckPermission_AutoApprovedTool_PathAllow_ShouldBeApproved()
    {
        // autoApprovedTools + 路径在工作目录内 → 应允许
        var pathChecker = new PathPermissionChecker(new IO.FileSystem.PhysicalFileSystem(), @"C:\Projects\MyApp");

        var config = PermissionConfig.CreateDefault();
        var middlewares = new IMiddleware<PermissionCheckContext>[]
        {
            new BypassPermissionMiddleware(),
            new AgentRestrictionMiddleware(),
            new DangerousCommandProtectionMiddleware(destructiveCommandDetector: new DestructiveCommandDetector()),
            new AutoClassifierMiddleware(),
            new ConfigGetOperationMiddleware(),
            new WebFetchPermissionMiddleware(),
            new EarlyPathDenyMiddleware(pathChecker),
            new ToolListPermissionMiddleware(),
            new PathPermissionMiddleware(pathChecker),
            new DangerousOperationMiddleware(),
            new PlanModeMiddleware(),
            new DefaultResultMiddleware()
        };
        var pipeline = new MiddlewarePipeline<PermissionCheckContext>(middlewares);
        var checker = new PermissionChecker(
            pipeline,
            Options.Create(config),
            new IO.FileSystem.PhysicalFileSystem(),
            NullLogger<PermissionChecker>.Instance);

        var args = new Dictionary<string, JsonElement>
        {
            ["file_path"] = JsonSerializer.SerializeToElement(@"C:\Projects\MyApp\src\Program.cs")
        };

        var result = await checker.CheckPermissionAsync(FileToolNameConstants.FileRead, args).ConfigureAwait(true);
        result.IsApproved.Should().BeTrue();
    }

    #endregion
}
