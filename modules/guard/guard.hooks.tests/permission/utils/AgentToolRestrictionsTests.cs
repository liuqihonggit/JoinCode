
namespace Guard.Tests.Permission.Utils;

public sealed class AgentToolRestrictionsTests {
    private readonly AgentToolRestrictions _sut;

    public AgentToolRestrictionsTests() {
        _sut = new AgentToolRestrictions();
    }

    [Fact]
    public void GetAllowedTools_AutoMode_ShouldContainReadWriteTools() {
        var allowed = _sut.GetAllowedTools(PermissionMode.Auto);

        allowed.Should().Contain(FileToolNameEnumConstants.FileRead);
        allowed.Should().Contain(FileToolNameEnumConstants.FileWrite);
        allowed.Should().Contain(FileToolNameEnumConstants.FileEdit);
        allowed.Should().Contain(SearchToolNameEnumConstants.Glob);
        allowed.Should().Contain(SearchToolNameEnumConstants.Grep);
        allowed.Should().Contain(WebToolNameEnumConstants.WebFetch);
        allowed.Should().Contain(CodeToolNameEnumConstants.CodeIndexSearch);
    }

    /// <summary>
    /// 验证 TodoWrite 在 Auto 模式下被允许 — 用户省钱刚需: E2E 测试中发现 TodoWrite 被误拒
    /// </summary>
    [Fact]
    public void GetAllowedTools_AutoMode_ShouldContainTodoWrite() {
        var allowed = _sut.GetAllowedTools(PermissionMode.Auto);

        allowed.Should().Contain(TodoToolNameEnumConstants.TodoWrite);
        allowed.Should().Contain(TodoToolNameEnumConstants.TodoList);
    }

    /// <summary>
    /// 验证 TodoWrite 在 Plan 模式下也被允许 — Plan 模式需要记录计划任务
    /// </summary>
    [Fact]
    public void GetAllowedTools_PlanMode_ShouldContainTodoWrite() {
        var allowed = _sut.GetAllowedTools(PermissionMode.Plan);

        allowed.Should().Contain(TodoToolNameEnumConstants.TodoWrite);
        allowed.Should().Contain(TodoToolNameEnumConstants.TodoList);
    }

    [Fact]
    public void GetAllowedTools_AutoMode_ShouldNotContainShellTools() {
        var allowed = _sut.GetAllowedTools(PermissionMode.Auto);

        allowed.Should().NotContain(ShellToolNameEnumConstants.Bash);
        allowed.Should().NotContain(ShellToolNameEnumConstants.Powershell);
    }

    [Fact]
    public void GetAllowedTools_PlanMode_ShouldContainReadOnlyTools() {
        var allowed = _sut.GetAllowedTools(PermissionMode.Plan);

        allowed.Should().Contain(FileToolNameEnumConstants.FileRead);
        allowed.Should().Contain(SearchToolNameEnumConstants.Glob);
        allowed.Should().Contain(SearchToolNameEnumConstants.Grep);
        allowed.Should().Contain(WebToolNameEnumConstants.WebFetch);
    }

    [Fact]
    public void GetAllowedTools_PlanMode_ShouldNotContainWriteTools() {
        var allowed = _sut.GetAllowedTools(PermissionMode.Plan);

        allowed.Should().NotContain(FileToolNameEnumConstants.FileWrite);
        allowed.Should().NotContain(FileToolNameEnumConstants.FileEdit);
        allowed.Should().NotContain(ShellToolNameEnumConstants.Bash);
    }

    [Fact]
    public void GetAllowedTools_AskMode_ShouldContainAllTools() {
        var allowed = _sut.GetAllowedTools(PermissionMode.Ask);

        allowed.Should().Contain(FileToolNameEnumConstants.FileRead);
        allowed.Should().Contain(FileToolNameEnumConstants.FileWrite);
        allowed.Should().Contain(FileToolNameEnumConstants.FileEdit);
        allowed.Should().Contain(ShellToolNameEnumConstants.Bash);
        allowed.Should().Contain(ShellToolNameEnumConstants.Powershell);
    }

    [Fact]
    public void GetAllowedTools_BypassMode_ShouldReturnAutoAllowedTools() {
        var allowed = _sut.GetAllowedTools(PermissionMode.Bypass);

        allowed.Should().NotBeEmpty();
    }

    [Fact]
    public void GetDeniedTools_BypassMode_ShouldBeEmpty() {
        var denied = _sut.GetDeniedTools(PermissionMode.Bypass);

        denied.Should().BeEmpty();
    }

    [Fact]
    public void GetDeniedTools_PlanMode_ShouldContainWriteAndDangerousTools() {
        var denied = _sut.GetDeniedTools(PermissionMode.Plan);

        denied.Should().Contain(FileToolNameEnumConstants.FileWrite);
        denied.Should().Contain(FileToolNameEnumConstants.FileEdit);
        denied.Should().Contain(FileToolNameEnumConstants.FileDelete);
        denied.Should().Contain(ShellToolNameEnumConstants.Bash);
        denied.Should().Contain(GitToolNameEnumConstants.GitPush);
    }

    [Fact]
    public void GetDeniedTools_AskMode_ShouldBeEmpty() {
        var denied = _sut.GetDeniedTools(PermissionMode.Ask);

        denied.Should().BeEmpty();
    }

    [Theory]
    [InlineData(FileToolNameEnumConstants.FileRead, PermissionMode.Auto, true)]
    [InlineData(FileToolNameEnumConstants.FileWrite, PermissionMode.Auto, true)]
    [InlineData(TodoToolNameEnumConstants.TodoWrite, PermissionMode.Auto, true)]
    [InlineData(TodoToolNameEnumConstants.TodoList, PermissionMode.Auto, true)]
    [InlineData(ShellToolNameEnumConstants.Bash, PermissionMode.Auto, true)]
    [InlineData(FileToolNameEnumConstants.FileDelete, PermissionMode.Auto, true)]
    [InlineData(FileToolNameEnumConstants.FileRead, PermissionMode.Plan, true)]
    [InlineData(TodoToolNameEnumConstants.TodoWrite, PermissionMode.Plan, true)]
    [InlineData(FileToolNameEnumConstants.FileWrite, PermissionMode.Plan, false)]
    [InlineData(ShellToolNameEnumConstants.Bash, PermissionMode.Plan, false)]
    [InlineData(FileToolNameEnumConstants.FileRead, PermissionMode.Ask, true)]
    [InlineData(ShellToolNameEnumConstants.Bash, PermissionMode.Ask, true)]
    [InlineData(FileToolNameEnumConstants.FileWrite, PermissionMode.Ask, true)]
    public void IsToolAllowedForMode_ShouldReturnExpectedResult(
        string toolName, PermissionMode mode, bool expected) {
        var result = _sut.IsToolAllowedForMode(toolName, mode);

        result.Should().Be(expected);
    }

    [Fact]
    public void IsToolAllowedForMode_BypassMode_AnyToolShouldBeTrue() {
        _sut.IsToolAllowedForMode("any_tool", PermissionMode.Bypass).Should().BeTrue();
    }

    /// <summary>
    /// 验证 code_index_search_comprehensive (rg+AST 综合检索) 在 Auto/Plan/Ask 三个模式都被允许
    /// 该工具是只读检索(不修改状态),应与 code_index_search 同等权限
    /// </summary>
    [Theory]
    [InlineData(PermissionMode.Auto)]
    [InlineData(PermissionMode.Plan)]
    [InlineData(PermissionMode.Ask)]
    public void IsToolAllowedForMode_CodeIndexSearchComprehensive_ShouldBeAllowedInReadWriteModes(PermissionMode mode) {
        _sut.IsToolAllowedForMode(CodeToolNameEnumConstants.CodeIndexSearchComprehensive, mode).Should().BeTrue();
    }

    [Fact]
    public void IsToolAllowedForMode_AutoMode_SensitiveToolsAllowedButNeedConfirmation() {
        _sut.IsToolAllowedForMode(GitToolNameEnumConstants.GitCommit, PermissionMode.Auto).Should().BeTrue();
        _sut.IsToolAllowedForMode(GitToolNameEnumConstants.GitPush, PermissionMode.Auto).Should().BeTrue();
        _sut.IsToolAllowedForMode(ShellToolNameEnumConstants.Bash, PermissionMode.Auto).Should().BeTrue();
    }

    [Fact]
    public void IsToolAllowedForMode_PlanMode_ReadOnlyToolShouldBeTrue() {
        _sut.IsToolAllowedForMode(SearchToolNameEnumConstants.Glob, PermissionMode.Plan).Should().BeTrue();
        _sut.IsToolAllowedForMode(SearchToolNameEnumConstants.SearchCode, PermissionMode.Plan).Should().BeTrue();
        _sut.IsToolAllowedForMode(WebToolNameEnumConstants.WebSearch, PermissionMode.Plan).Should().BeTrue();
    }

    [Fact]
    public void IsToolAllowedForMode_BypassMode_AlwaysTrue() {
        _sut.IsToolAllowedForMode(FileToolNameEnumConstants.FileRead, PermissionMode.Bypass).Should().BeTrue();
        _sut.IsToolAllowedForMode("safe_tool", PermissionMode.Bypass).Should().BeTrue();
    }

    [Fact]
    public void IsToolAllowedForMode_ExactCaseMatch_ShouldWork() {
        _sut.IsToolAllowedForMode(FileToolNameEnumConstants.FileRead, PermissionMode.Auto).Should().BeTrue();
        _sut.IsToolAllowedForMode(ShellToolNameEnumConstants.Bash, PermissionMode.Auto).Should().BeTrue();
    }

    [Theory]
    [InlineData("read", PermissionMode.Auto, true)]
    [InlineData("READ", PermissionMode.Auto, true)]
    [InlineData("Read", PermissionMode.Auto, true)]
    [InlineData("write", PermissionMode.Auto, true)]
    [InlineData("edit", PermissionMode.Auto, true)]
    [InlineData("glob", PermissionMode.Auto, true)]
    [InlineData("grep", PermissionMode.Auto, true)]
    [InlineData("shell_execute", PermissionMode.Auto, true)]
    [InlineData("file_delete", PermissionMode.Auto, true)]
    public void IsToolAllowedForMode_CaseInsensitive_ShouldMatch(string toolName, PermissionMode mode, bool expected) {
        // LLM 返回的工具名可能是任意大小写（如 read/Read/READ）
        // 权限检查应大小写不敏感匹配，避免误拒
        var result = _sut.IsToolAllowedForMode(toolName, mode);

        result.Should().Be(expected);
    }

    [Fact]
    public void GetAllowedTools_AutoMode_ShouldContainExpectedCount() {
        var allowed = _sut.GetAllowedTools(PermissionMode.Auto);

        allowed.Count.Should().BeGreaterThan(5);
    }
}