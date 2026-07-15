
namespace Guard.Tests.Permission.Utils;

public sealed class AgentToolRestrictionsTests
{
    private readonly AgentToolRestrictions _sut;

    public AgentToolRestrictionsTests()
    {
        _sut = new AgentToolRestrictions();
    }

    [Fact]
    public void GetAllowedTools_AutoMode_ShouldContainReadWriteTools()
    {
        var allowed = _sut.GetAllowedTools(PermissionMode.Auto);

        allowed.Should().Contain(FileToolNameConstants.FileRead);
        allowed.Should().Contain(FileToolNameConstants.FileWrite);
        allowed.Should().Contain(FileToolNameConstants.FileEdit);
        allowed.Should().Contain(SearchToolNameConstants.Glob);
        allowed.Should().Contain(SearchToolNameConstants.Grep);
        allowed.Should().Contain(WebToolNameConstants.WebFetch);
        allowed.Should().Contain(CodeToolNameConstants.CodeIndexSearch);
    }

    /// <summary>
    /// 验证 TodoWrite 在 Auto 模式下被允许 — 用户省钱刚需: E2E 测试中发现 TodoWrite 被误拒
    /// </summary>
    [Fact]
    public void GetAllowedTools_AutoMode_ShouldContainTodoWrite()
    {
        var allowed = _sut.GetAllowedTools(PermissionMode.Auto);

        allowed.Should().Contain(TodoToolNameConstants.TodoWrite);
        allowed.Should().Contain(TodoToolNameConstants.TodoList);
    }

    /// <summary>
    /// 验证 TodoWrite 在 Plan 模式下也被允许 — Plan 模式需要记录计划任务
    /// </summary>
    [Fact]
    public void GetAllowedTools_PlanMode_ShouldContainTodoWrite()
    {
        var allowed = _sut.GetAllowedTools(PermissionMode.Plan);

        allowed.Should().Contain(TodoToolNameConstants.TodoWrite);
        allowed.Should().Contain(TodoToolNameConstants.TodoList);
    }

    [Fact]
    public void GetAllowedTools_AutoMode_ShouldNotContainShellTools()
    {
        var allowed = _sut.GetAllowedTools(PermissionMode.Auto);

        allowed.Should().NotContain(ShellToolNameConstants.Bash);
        allowed.Should().NotContain(ShellToolNameConstants.Powershell);
    }

    [Fact]
    public void GetAllowedTools_PlanMode_ShouldContainReadOnlyTools()
    {
        var allowed = _sut.GetAllowedTools(PermissionMode.Plan);

        allowed.Should().Contain(FileToolNameConstants.FileRead);
        allowed.Should().Contain(SearchToolNameConstants.Glob);
        allowed.Should().Contain(SearchToolNameConstants.Grep);
        allowed.Should().Contain(WebToolNameConstants.WebFetch);
    }

    [Fact]
    public void GetAllowedTools_PlanMode_ShouldNotContainWriteTools()
    {
        var allowed = _sut.GetAllowedTools(PermissionMode.Plan);

        allowed.Should().NotContain(FileToolNameConstants.FileWrite);
        allowed.Should().NotContain(FileToolNameConstants.FileEdit);
        allowed.Should().NotContain(ShellToolNameConstants.Bash);
    }

    [Fact]
    public void GetAllowedTools_AskMode_ShouldContainAllTools()
    {
        var allowed = _sut.GetAllowedTools(PermissionMode.Ask);

        allowed.Should().Contain(FileToolNameConstants.FileRead);
        allowed.Should().Contain(FileToolNameConstants.FileWrite);
        allowed.Should().Contain(FileToolNameConstants.FileEdit);
        allowed.Should().Contain(ShellToolNameConstants.Bash);
        allowed.Should().Contain(ShellToolNameConstants.Powershell);
    }

    [Fact]
    public void GetAllowedTools_DenyMode_ShouldBeEmpty()
    {
        var allowed = _sut.GetAllowedTools(PermissionMode.Deny);

        allowed.Should().BeEmpty();
    }

    [Fact]
    public void GetDeniedTools_AutoMode_ShouldContainShellAndDangerousTools()
    {
        var denied = _sut.GetDeniedTools(PermissionMode.Auto);

        denied.Should().Contain(ShellToolNameConstants.Bash);
        denied.Should().Contain(ShellToolNameConstants.Powershell);
        denied.Should().Contain(FileToolNameConstants.FileDelete);
        denied.Should().Contain(GitToolNameConstants.GitCommit);
        denied.Should().Contain(GitToolNameConstants.GitPush);
    }

    [Fact]
    public void GetDeniedTools_PlanMode_ShouldContainWriteAndDangerousTools()
    {
        var denied = _sut.GetDeniedTools(PermissionMode.Plan);

        denied.Should().Contain(FileToolNameConstants.FileWrite);
        denied.Should().Contain(FileToolNameConstants.FileEdit);
        denied.Should().Contain(FileToolNameConstants.FileDelete);
        denied.Should().Contain(ShellToolNameConstants.Bash);
        denied.Should().Contain(GitToolNameConstants.GitPush);
    }

    [Fact]
    public void GetDeniedTools_AskMode_ShouldBeEmpty()
    {
        var denied = _sut.GetDeniedTools(PermissionMode.Ask);

        denied.Should().BeEmpty();
    }

    [Fact]
    public void GetDeniedTools_DenyMode_ShouldContainWildcard()
    {
        var denied = _sut.GetDeniedTools(PermissionMode.Deny);

        denied.Should().Contain("*");
    }

    [Theory]
    [InlineData(FileToolNameConstants.FileRead, PermissionMode.Auto, true)]
    [InlineData(FileToolNameConstants.FileWrite, PermissionMode.Auto, true)]
    [InlineData(TodoToolNameConstants.TodoWrite, PermissionMode.Auto, true)]
    [InlineData(TodoToolNameConstants.TodoList, PermissionMode.Auto, true)]
    [InlineData(ShellToolNameConstants.Bash, PermissionMode.Auto, false)]
    [InlineData(FileToolNameConstants.FileDelete, PermissionMode.Auto, false)]
    [InlineData(FileToolNameConstants.FileRead, PermissionMode.Plan, true)]
    [InlineData(TodoToolNameConstants.TodoWrite, PermissionMode.Plan, true)]
    [InlineData(FileToolNameConstants.FileWrite, PermissionMode.Plan, false)]
    [InlineData(ShellToolNameConstants.Bash, PermissionMode.Plan, false)]
    [InlineData(FileToolNameConstants.FileRead, PermissionMode.Ask, true)]
    [InlineData(ShellToolNameConstants.Bash, PermissionMode.Ask, true)]
    [InlineData(FileToolNameConstants.FileWrite, PermissionMode.Ask, true)]
    public void IsToolAllowedForMode_ShouldReturnExpectedResult(
        string toolName, PermissionMode mode, bool expected)
    {
        var result = _sut.IsToolAllowedForMode(toolName, mode);

        result.Should().Be(expected);
    }

    [Fact]
    public void IsToolAllowedForMode_DenyMode_AnyToolShouldBeFalse()
    {
        _sut.IsToolAllowedForMode("any_tool", PermissionMode.Deny).Should().BeFalse();
    }

    /// <summary>
    /// 验证 code_index_search_comprehensive (rg+AST 综合检索) 在 Auto/Plan/Ask 三个模式都被允许
    /// 该工具是只读检索(不修改状态),应与 code_index_search 同等权限
    /// </summary>
    [Theory]
    [InlineData(PermissionMode.Auto)]
    [InlineData(PermissionMode.Plan)]
    [InlineData(PermissionMode.Ask)]
    public void IsToolAllowedForMode_CodeIndexSearchComprehensive_ShouldBeAllowedInReadWriteModes(PermissionMode mode)
    {
        _sut.IsToolAllowedForMode(CodeToolNameConstants.CodeIndexSearchComprehensive, mode).Should().BeTrue();
    }

    [Fact]
    public void IsToolAllowedForMode_AutoMode_DeniedToolShouldBeFalse()
    {
        _sut.IsToolAllowedForMode(GitToolNameConstants.GitCommit, PermissionMode.Auto).Should().BeFalse();
        _sut.IsToolAllowedForMode(GitToolNameConstants.GitPush, PermissionMode.Auto).Should().BeFalse();
    }

    [Fact]
    public void IsToolAllowedForMode_PlanMode_ReadOnlyToolShouldBeTrue()
    {
        _sut.IsToolAllowedForMode(SearchToolNameConstants.Glob, PermissionMode.Plan).Should().BeTrue();
        _sut.IsToolAllowedForMode(SearchToolNameConstants.SearchCode, PermissionMode.Plan).Should().BeTrue();
        _sut.IsToolAllowedForMode(WebToolNameConstants.WebSearch, PermissionMode.Plan).Should().BeTrue();
    }

    [Fact]
    public void IsToolAllowedForMode_DenyMode_AlwaysFalse()
    {
        _sut.IsToolAllowedForMode(FileToolNameConstants.FileRead, PermissionMode.Deny).Should().BeFalse();
        _sut.IsToolAllowedForMode("safe_tool", PermissionMode.Deny).Should().BeFalse();
    }

    [Fact]
    public void IsToolAllowedForMode_ExactCaseMatch_ShouldWork()
    {
        _sut.IsToolAllowedForMode(FileToolNameConstants.FileRead, PermissionMode.Auto).Should().BeTrue();
        _sut.IsToolAllowedForMode(ShellToolNameConstants.Bash, PermissionMode.Auto).Should().BeFalse();
    }

    [Theory]
    [InlineData("read", PermissionMode.Auto, true)]
    [InlineData("READ", PermissionMode.Auto, true)]
    [InlineData("Read", PermissionMode.Auto, true)]
    [InlineData("write", PermissionMode.Auto, true)]
    [InlineData("edit", PermissionMode.Auto, true)]
    [InlineData("glob", PermissionMode.Auto, true)]
    [InlineData("grep", PermissionMode.Auto, true)]
    [InlineData("shell_execute", PermissionMode.Auto, false)]
    [InlineData("file_delete", PermissionMode.Auto, false)]
    public void IsToolAllowedForMode_CaseInsensitive_ShouldMatch(string toolName, PermissionMode mode, bool expected)
    {
        // LLM 返回的工具名可能是任意大小写（如 read/Read/READ）
        // 权限检查应大小写不敏感匹配，避免误拒
        var result = _sut.IsToolAllowedForMode(toolName, mode);

        result.Should().Be(expected);
    }

    [Fact]
    public void GetAllowedTools_AutoMode_ShouldContainExpectedCount()
    {
        var allowed = _sut.GetAllowedTools(PermissionMode.Auto);

        allowed.Count.Should().BeGreaterThan(5);
    }
}
