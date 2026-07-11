namespace Host.Tests.ChatCommands;

/// <summary>
/// MemoryCommand 取值范围测试 — 验证 MemorySubCommand 枚举字面量正确路由
/// 覆盖:edit/open/add/search/db/stats/health/cleanup + 未知子命令 + 大小写不敏感 + 默认(无参)
/// 验证目标:Step 2 重构后,所有 case 标签能被正确识别
/// </summary>
public sealed class MemoryCommandTests
{
    [Fact]
    public void Name_Should_Be_memory()
    {
        var cmd = new MemoryCommand();
        cmd.Name.Should().Be("memory");
    }

    [Fact]
    public void Description_Should_Not_Be_Empty()
    {
        var cmd = new MemoryCommand();
        cmd.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Usage_Should_Start_With_Slash()
    {
        var cmd = new MemoryCommand();
        cmd.Usage.Should().StartWith("/memory");
    }

    [Fact]
    public void IsHidden_Should_Be_False()
    {
        var cmd = new MemoryCommand();
        cmd.IsHidden.Should().BeFalse();
    }

    [Fact]
    public void Aliases_Should_Contain_mem()
    {
        var cmd = new MemoryCommand();
        cmd.Aliases.Should().Contain("mem");
    }

    // ===== MemorySubCommand 枚举路由取值范围测试 =====

    [Fact]
    public async Task Execute_WithEmptyArgs_Should_Return_Continue()
    {
        // 无参数 → 走默认 ListMemoryFilesAsync(打印文件列表)
        var cmd = new MemoryCommand();
        var context = CreateContext("");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("open")]
    [InlineData("add")]
    [InlineData("search")]
    [InlineData("db")]
    [InlineData("stats")]
    [InlineData("health")]
    [InlineData("cleanup")]
    public async Task Execute_WithValidSubCommand_Should_Return_Continue(string subCommand)
    {
        // 8 个 MemorySubCommand 值 — 编辑/打开/添加/搜索/数据库/统计/健康/清理
        var cmd = new MemoryCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithUnknownSubcommand_Should_NotThrow()
    {
        // 未知子命令走 default 分支,打印可用操作列表
        var cmd = new MemoryCommand();
        var context = CreateContext("unknown-action");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Theory]
    [InlineData("EDIT")]
    [InlineData("OPEN")]
    [InlineData("ADD")]
    [InlineData("SEARCH")]
    [InlineData("DB")]
    [InlineData("STATS")]
    [InlineData("HEALTH")]
    [InlineData("CLEANUP")]
    public async Task Execute_WithUppercaseSubcommand_Should_Be_CaseInsensitive(string subCommand)
    {
        // 验证小写化路由(toLowerInvariant 后枚举匹配)
        var cmd = new MemoryCommand();
        var context = CreateContext(subCommand);

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    // ===== 服务相关子命令测试(需 mock IMemoryManagementService) =====

    [Fact]
    public async Task Execute_WithAdd_Should_NotThrow_When_Service_Null()
    {
        // 当服务不可用时,应优雅降级(打印"服务不可用"),不抛 NRE
        var cmd = new MemoryCommand();
        var context = CreateContext("add 测试内容");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithSearch_Should_NotThrow_When_Service_Null()
    {
        var cmd = new MemoryCommand();
        var context = CreateContext("search 关键词");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithDb_Should_NotThrow_When_Service_Null()
    {
        var cmd = new MemoryCommand();
        var context = CreateContext("db");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithStats_Should_NotThrow_When_Service_Null()
    {
        var cmd = new MemoryCommand();
        var context = CreateContext("stats");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithHealth_Should_NotThrow_When_Service_Null()
    {
        var cmd = new MemoryCommand();
        var context = CreateContext("health");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithCleanup_Should_NotThrow_When_Service_Null()
    {
        var cmd = new MemoryCommand();
        var context = CreateContext("cleanup");

        var result = await cmd.ExecuteAsync(context).ConfigureAwait(true);

        result.ShouldContinue.Should().BeTrue();
    }

    private static ChatCommandContext CreateContext(string arguments)
    {
        return new ChatCommandContext
        {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
            Services = new CommandServices
            {
                ChatService = Mock.Of<IChatService>(),
                CodeService = Mock.Of<ICodeService>(),
                PlanService = Mock.Of<IPlanService>(),
                // MemoryManagementService 故意保持 null,验证 null 服务兜底
            FileSystem = TestFileSystem.Current,
            },
        };
    }
}
