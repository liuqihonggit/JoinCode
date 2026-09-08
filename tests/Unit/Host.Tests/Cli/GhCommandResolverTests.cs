namespace Host.Tests.Cli;

/// <summary>
/// jcc gh 子命令解析与参数绑定测试 — 纯函数红绿循环，无需建 Host。
/// <para>ADR: 0090 — 扁平元动词 gh &lt;group&gt; &lt;action&gt;，位置参数按 schema required 顺序绑定。</para>
/// </summary>
public sealed class GhCommandResolverTests
{
    /// <summary>jcc gh pr view 123 → gh_pr_view，位置参数 123</summary>
    [Fact]
    public void Resolve_PrView_ShouldMapToolNameAndTail()
    {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "pr", "view", "123" }, out var error);

        error.Should().BeNull();
        resolved.Should().NotBeNull();
        resolved!.ToolName.Should().Be("gh_pr_view");
        resolved.Group.Should().Be("pr");
        resolved.Action.Should().Be("view");
        resolved.Tail.Should().BeEquivalentTo(new[] { "123" });
        resolved.Json.Should().BeFalse();
    }

    /// <summary>jcc gh api &lt;path&gt; 是单级命令 → gh_api，无 action</summary>
    [Fact]
    public void Resolve_Api_ShouldMapToGhApiWithoutAction()
    {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "api", "repos/o/r/issues" }, out var error);

        error.Should().BeNull();
        resolved!.ToolName.Should().Be("gh_api");
        resolved.Action.Should().BeNull();
        resolved.Tail.Should().BeEquivalentTo(new[] { "repos/o/r/issues" });
    }

    /// <summary>--json 可在任意位置出现，且不进入待绑定参数</summary>
    [Fact]
    public void Resolve_JsonFlag_ShouldBeStrippedFromTail()
    {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "pr", "list", "--json", "--limit", "3" }, out var error);

        error.Should().BeNull();
        resolved!.Json.Should().BeTrue();
        resolved.Tail.Should().BeEquivalentTo(new[] { "--limit", "3" });
    }

    /// <summary>只有 jcc gh 时缺少分组，应报错并给出用法</summary>
    [Fact]
    public void Resolve_MissingGroup_ShouldReturnUsageError()
    {
        var resolved = GhCommandResolver.Resolve(new[] { "gh" }, out var error);

        resolved.Should().BeNull();
        error.Should().NotBeNull();
        error.Should().Contain("用法");
    }

    /// <summary>jcc gh pr 缺少 action，应提示该分组用法</summary>
    [Fact]
    public void Resolve_MissingAction_ShouldReturnActionHint()
    {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "pr" }, out var error);

        resolved.Should().BeNull();
        error.Should().Contain("jcc gh pr <action>");
    }

    /// <summary>未知分组应列出可用分组</summary>
    [Fact]
    public void Resolve_UnknownGroup_ShouldListKnownGroups()
    {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "bogus", "view" }, out var error);

        resolved.Should().NotBeNull();
        resolved!.ToolName.Should().Be("gh_bogus_view");
        _ = error;
    }

    /// <summary>位置参数按 required 声明顺序绑定（issue_number, body）</summary>
    [Fact]
    public void Bind_Positionals_ShouldFillRequiredInOrder()
    {
        var parameters = new List<GhParam>
        {
            new("issue_number", IsRequired: true, IsBoolean: false),
            new("body", IsRequired: true, IsBoolean: false),
            new("repo", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "12", "hello" }, parameters, "gh_issue_comment", out var error);

        error.Should().BeNull();
        bound!["issue_number"].Should().Be("12");
        bound["body"].Should().Be("hello");
    }

    /// <summary>布尔参数支持无值 flag 形式（--log → true）</summary>
    [Fact]
    public void Bind_BooleanFlag_ShouldSetTrueWithoutValue()
    {
        var parameters = new List<GhParam>
        {
            new("run_id", IsRequired: true, IsBoolean: false),
            new("log", IsRequired: false, IsBoolean: true),
            new("max_lines", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "123", "--log" }, parameters, "gh_run_view", out var error);

        error.Should().BeNull();
        bound!["run_id"].Should().Be("123");
        bound["log"].Should().Be("true");
    }

    /// <summary>--key value 与 --key=value 两种形式都应支持</summary>
    [Theory]
    [InlineData("--limit", "3")]
    [InlineData("--limit=3", null)]
    public void Bind_OptionForms_ShouldBothWork(string first, string? second)
    {
        var parameters = new List<GhParam> { new("limit", IsRequired: false, IsBoolean: false) };
        var tail = second is null ? new[] { first } : new[] { first, second };

        var bound = GhArgsBinder.Bind(tail, parameters, "gh_pr_list", out var error);

        error.Should().BeNull();
        bound!["limit"].Should().Be("3");
    }

    /// <summary>位置参数多于 required 槽位时，应报错并列出接受的参数</summary>
    [Fact]
    public void Bind_TooManyPositional_ShouldReportAcceptedParams()
    {
        var parameters = new List<GhParam>
        {
            new("pr_number", IsRequired: true, IsBoolean: false),
            new("repo", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "1", "2" }, parameters, "gh_pr_view", out var error);

        bound.Should().BeNull();
        error.Should().Contain("多余的位置参数");
        error.Should().Contain("pr_number");
    }

    /// <summary>缺少必填位置参数时，应给出正确用法</summary>
    [Fact]
    public void Bind_MissingRequired_ShouldReportUsage()
    {
        var parameters = new List<GhParam> { new("pr_number", IsRequired: true, IsBoolean: false) };

        var bound = GhArgsBinder.Bind([], parameters, "gh_pr_checks", out var error);

        bound.Should().BeNull();
        error.Should().Contain("pr_number");
    }

    /// <summary>未知选项应报错并列出可用选项</summary>
    [Fact]
    public void Bind_UnknownOption_ShouldReportAvailableOptions()
    {
        var parameters = new List<GhParam> { new("limit", IsRequired: false, IsBoolean: false) };

        var bound = GhArgsBinder.Bind(new[] { "--bogus", "1" }, parameters, "gh_pr_list", out var error);

        bound.Should().BeNull();
        error.Should().Contain("--bogus");
        error.Should().Contain("--limit");
    }

    /// <summary>非布尔选项缺值时，应提示正确写法</summary>
    [Fact]
    public void Bind_OptionMissingValue_ShouldReportHint()
    {
        var parameters = new List<GhParam> { new("limit", IsRequired: false, IsBoolean: false) };

        var bound = GhArgsBinder.Bind(new[] { "--limit" }, parameters, "gh_pr_list", out var error);

        bound.Should().BeNull();
        error.Should().Contain("--limit <值>");
    }

    /// <summary>必填参数保持 required 声明顺序，布尔类型从 schema type 推断</summary>
    [Fact]
    public void ParseSchema_ShouldKeepRequiredOrderAndBooleanType()
    {
        var schema = new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["repo"] = new ToolSchemaProperty { Type = "string" },
                ["pr_number"] = new ToolSchemaProperty { Type = "string" },
                ["delete_branch"] = new ToolSchemaProperty { Type = "boolean" },
            },
            Required = ["pr_number"],
        };

        var parameters = GhParamSchemaParser.Parse(schema);

        parameters[0].Name.Should().Be("pr_number");
        parameters[0].IsRequired.Should().BeTrue();
        parameters.Should().Contain(p => p.Name == "delete_branch" && p.IsBoolean);
        parameters.Should().Contain(p => p.Name == "repo" && !p.IsRequired && !p.IsBoolean);
    }
}
