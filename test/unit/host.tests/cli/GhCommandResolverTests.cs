namespace Host.Tests.Cli;

/// <summary>
/// jcc gh 子命令解析与参数绑定测试 — 纯函数红绿循环，无需建 Host。
/// <para>ADR: 0090 — 扁平元动词 gh &lt;group&gt; &lt;action&gt;，位置参数按 schema required 顺序绑定。</para>
/// </summary>
public sealed class GhCommandResolverTests {
    /// <summary>jcc gh pr view 123 → gh_pr_view，位置参数 123</summary>
    [Fact]
    public void Resolve_PrView_ShouldMapToolNameAndTail() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "pr", "view", "123" }, out var error);

        error.Should().BeNull();
        resolved.Should().NotBeNull();
        resolved!.ToolName.Should().Be("gh_pr_view");
        resolved.Group.Should().Be("pr");
        resolved.Action.Should().Be("view");
        resolved.Tail.Should().BeEquivalentTo(new[] { "123" });
        resolved.Json.Should().BeTrue(); // 默认 JSON 输出 (ADR 0069 决策6)
    }

    /// <summary>jcc gh api &lt;path&gt; 是单级命令 → gh_api，无 action</summary>
    [Fact]
    public void Resolve_Api_ShouldMapToGhApiWithoutAction() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "api", "repos/o/r/issues" }, out var error);

        error.Should().BeNull();
        resolved!.ToolName.Should().Be("gh_api");
        resolved.Action.Should().BeNull();
        resolved.Tail.Should().BeEquivalentTo(new[] { "repos/o/r/issues" });
    }

    /// <summary>--json 可在任意位置出现，且不进入待绑定参数</summary>
    [Fact]
    public void Resolve_JsonFlag_ShouldBeStrippedFromTail() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "pr", "list", "--json", "--limit", "3" }, out var error);

        error.Should().BeNull();
        resolved!.Json.Should().BeTrue();
        resolved.Tail.Should().BeEquivalentTo(new[] { "--limit", "3" });
    }

    /// <summary>只有 jcc gh 时缺少分组，应报错并给出用法</summary>
    [Fact]
    public void Resolve_MissingGroup_ShouldReturnUsageError() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh" }, out var error);

        resolved.Should().BeNull();
        error.Should().NotBeNull();
        error.Should().Contain("用法");
    }

    /// <summary>jcc gh pr 缺少 action，应提示该分组用法</summary>
    [Fact]
    public void Resolve_MissingAction_ShouldReturnActionHint() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "pr" }, out var error);

        resolved.Should().BeNull();
        error.Should().Contain("jcc gh pr <action>");
    }

    /// <summary>未知分组应列出可用分组</summary>
    [Fact]
    public void Resolve_UnknownGroup_ShouldListKnownGroups() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "bogus", "view" }, out var error);

        resolved.Should().NotBeNull();
        resolved!.ToolName.Should().Be("gh_bogus_view");
        _ = error;
    }

    /// <summary>位置参数按 required 声明顺序绑定（issue_number, body）</summary>
    [Fact]
    public void Bind_Positionals_ShouldFillRequiredInOrder() {
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
    public void Bind_BooleanFlag_ShouldSetTrueWithoutValue() {
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
    public void Bind_OptionForms_ShouldBothWork(string first, string? second) {
        var parameters = new List<GhParam> { new("limit", IsRequired: false, IsBoolean: false) };
        var tail = second is null ? new[] { first } : new[] { first, second };

        var bound = GhArgsBinder.Bind(tail, parameters, "gh_pr_list", out var error);

        error.Should().BeNull();
        bound!["limit"].Should().Be("3");
    }

    /// <summary>位置参数多于 required 槽位时，应报错并列出接受的参数</summary>
    [Fact]
    public void Bind_TooManyPositional_ShouldReportAcceptedParams() {
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
    public void Bind_MissingRequired_ShouldReportUsage() {
        var parameters = new List<GhParam> { new("pr_number", IsRequired: true, IsBoolean: false) };

        var bound = GhArgsBinder.Bind([], parameters, "gh_pr_checks", out var error);

        bound.Should().BeNull();
        error.Should().Contain("pr_number");
    }

    /// <summary>未知选项应报错并列出可用选项</summary>
    [Fact]
    public void Bind_UnknownOption_ShouldReportAvailableOptions() {
        var parameters = new List<GhParam> { new("limit", IsRequired: false, IsBoolean: false) };

        var bound = GhArgsBinder.Bind(new[] { "--bogus", "1" }, parameters, "gh_pr_list", out var error);

        bound.Should().BeNull();
        error.Should().Contain("--bogus");
        error.Should().Contain("--limit");
    }

    /// <summary>非布尔选项缺值时，应提示正确写法</summary>
    [Fact]
    public void Bind_OptionMissingValue_ShouldReportHint() {
        var parameters = new List<GhParam> { new("limit", IsRequired: false, IsBoolean: false) };

        var bound = GhArgsBinder.Bind(new[] { "--limit" }, parameters, "gh_pr_list", out var error);

        bound.Should().BeNull();
        error.Should().Contain("--limit <值>");
    }

    /// <summary>系统 gh CLI 缩写 --auto 应映射到 auto_merge=true（AI 习惯用真实 gh CLI 语法）</summary>
    [Fact]
    public void Bind_GhCliAlias_Auto_ShouldMapToAutoMerge() {
        var parameters = new List<GhParam> {
            new("pr_number", IsRequired: true, IsBoolean: false),
            new("merge_method", IsRequired: false, IsBoolean: false),
            new("auto_merge", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "382", "--auto" }, parameters, "gh_pr_merge", out var error);

        error.Should().BeNull();
        bound!["auto_merge"].Should().Be("true");
    }

    /// <summary>系统 gh CLI 缩写 --squash 应映射到 merge_method=squash</summary>
    [Fact]
    public void Bind_GhCliAlias_Squash_ShouldMapToMergeMethod() {
        var parameters = new List<GhParam> {
            new("pr_number", IsRequired: true, IsBoolean: false),
            new("merge_method", IsRequired: false, IsBoolean: false),
            new("auto_merge", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "382", "--squash" }, parameters, "gh_pr_merge", out var error);

        error.Should().BeNull();
        bound!["merge_method"].Should().Be("squash");
    }

    /// <summary>--auto --squash 组合应同时映射（对应文件中 #4 的 jcc gh pr merge 382 --auto --squash）</summary>
    [Fact]
    public void Bind_GhCliAlias_AutoAndSquash_ShouldMapBoth() {
        var parameters = new List<GhParam> {
            new("pr_number", IsRequired: true, IsBoolean: false),
            new("merge_method", IsRequired: false, IsBoolean: false),
            new("auto_merge", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "382", "--auto", "--squash" }, parameters, "gh_pr_merge", out var error);

        error.Should().BeNull();
        bound!["auto_merge"].Should().Be("true");
        bound["merge_method"].Should().Be("squash");
    }

    /// <summary>系统 gh CLI 缩写 --failed 应映射到 failed_only=true（gh run rerun --failed）</summary>
    [Fact]
    public void Bind_GhCliAlias_Failed_ShouldMapToFailedOnly() {
        var parameters = new List<GhParam> {
            new("run_id", IsRequired: true, IsBoolean: false),
            new("failed_only", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "37560629113", "--failed" }, parameters, "gh_run_rerun", out var error);

        error.Should().BeNull();
        bound!["failed_only"].Should().Be("true");
    }

    /// <summary>系统 gh CLI 的 --job 应映射到 job_id（gh run view --job）</summary>
    [Fact]
    public void Bind_GhCliAlias_Job_ShouldMapToJobId() {
        var parameters = new List<GhParam> {
            new("run_id", IsRequired: true, IsBoolean: false),
            new("job_id", IsRequired: false, IsBoolean: false),
            new("log", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "123", "--job", "456", "--log" }, parameters, "gh_run_view", out var error);

        error.Should().BeNull();
        bound!["job_id"].Should().Be("456");
        bound["log"].Should().Be("true");
    }

    /// <summary>系统 gh CLI 的 --private/--public/--internal 应映射到 visibility（gh repo create）</summary>
    [Theory]
    [InlineData("private", "private")]
    [InlineData("public", "public")]
    [InlineData("internal", "internal")]
    public void Bind_GhCliAlias_VisibilityFlags_ShouldMapToVisibility(string flag, string expected) {
        var parameters = new List<GhParam> {
            new("name", IsRequired: true, IsBoolean: false),
            new("visibility", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "myrepo", $"--{flag}" }, parameters, "gh_repo_create", out var error);

        error.Should().BeNull();
        bound!["visibility"].Should().Be(expected);
    }

    /// <summary>系统 gh CLI 的 --duplicate 应映射到 duplicate_of（gh issue close --duplicate 42）</summary>
    [Fact]
    public void Bind_GhCliAlias_Duplicate_ShouldMapToDuplicateOf() {
        var parameters = new List<GhParam> {
            new("issue_number", IsRequired: true, IsBoolean: false),
            new("duplicate_of", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "12", "--duplicate", "42" }, parameters, "gh_issue_close", out var error);

        error.Should().BeNull();
        bound!["duplicate_of"].Should().Be("42");
    }

    /// <summary>系统 gh CLI 的 --completed/--not-planned 应映射到 reason（gh issue close）</summary>
    [Theory]
    [InlineData("completed", "completed")]
    [InlineData("not-planned", "not_planned")]
    public void Bind_GhCliAlias_ReasonFlags_ShouldMapToReason(string flag, string expected) {
        var parameters = new List<GhParam> {
            new("issue_number", IsRequired: true, IsBoolean: false),
            new("reason", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "12", $"--{flag}" }, parameters, "gh_issue_close", out var error);

        error.Should().BeNull();
        bound!["reason"].Should().Be(expected);
    }

    /// <summary>系统 gh CLI 缩写 --merge/--rebase 也应映射到 merge_method</summary>
    [Theory]
    [InlineData("merge", "merge")]
    [InlineData("rebase", "rebase")]
    public void Bind_GhCliAlias_MergeRebase_ShouldMapToMergeMethod(string alias, string expected) {
        var parameters = new List<GhParam> {
            new("pr_number", IsRequired: true, IsBoolean: false),
            new("merge_method", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "382", $"--{alias}" }, parameters, "gh_pr_merge", out var error);

        error.Should().BeNull();
        bound!["merge_method"].Should().Be(expected);
    }

    /// <summary>别名仅在 gh_pr_merge 生效，其他工具遇到 --auto 应报未知选项</summary>
    [Fact]
    public void Bind_GhCliAlias_NonMergeTool_ShouldNotApplyAlias() {
        var parameters = new List<GhParam> { new("limit", IsRequired: false, IsBoolean: false) };

        var bound = GhArgsBinder.Bind(new[] { "--auto" }, parameters, "gh_pr_list", out var error);

        bound.Should().BeNull();
        error.Should().Contain("--auto");
    }

    /// <summary>--json 被剥离后字段列表(含逗号)变位置参数,应提示 jcc 默认 JSON 输出</summary>
    [Fact]
    public void Bind_TooManyPositional_WithComma_ShouldHintJsonNotNeeded() {
        var parameters = new List<GhParam> { new("state", IsRequired: false, IsBoolean: false) };

        var bound = GhArgsBinder.Bind(new[] { "number,title,url" }, parameters, "gh_pr_list", out var error);

        bound.Should().BeNull();
        error.Should().Contain("默认 JSON");
        error.Should().Contain("--json");
    }

    /// <summary>未知选项应建议最接近的参数(前缀匹配: auto → auto_merge)</summary>
    [Fact]
    public void Bind_UnknownOption_ShouldSuggestClosestByPrefix() {
        var parameters = new List<GhParam> {
            new("limit", IsRequired: false, IsBoolean: false),
            new("auto_merge", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "--auto" }, parameters, "gh_pr_list", out var error);

        bound.Should().BeNull();
        error.Should().Contain("你是不是想用");
        error.Should().Contain("--auto_merge");
    }

    /// <summary>必填参数保持 required 声明顺序，布尔类型从 schema type 推断</summary>
    [Fact]
    public void ParseSchema_ShouldKeepRequiredOrderAndBooleanType() {
        var schema = new ToolSchema {
            Properties = new Dictionary<string, ToolSchemaProperty> {
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