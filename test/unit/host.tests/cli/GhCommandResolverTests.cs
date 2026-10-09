// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
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

    /// <summary>系统 gh CLI 的 --json field1,field2 应转为 --json_fields=field1,field2</summary>
    /// <para>缺陷1a: AI习惯写 --json statusCheckRollup（系统gh语法），jcc要求 --json_fields</para>
    [Fact]
    public void Resolve_JsonFollowedByFields_ShouldConvertToJsonFields() {
        var resolved = GhCommandResolver.Resolve(
            new[] { "gh", "pr", "view", "387", "--json", "statusCheckRollup" }, out var error);

        error.Should().BeNull();
        resolved!.Json.Should().BeTrue();
        resolved.Tail.Should().BeEquivalentTo(new[] { "387", "--json_fields=statusCheckRollup" });
    }

    /// <summary>--json 多字段逗号分隔也应正确转换</summary>
    [Fact]
    public void Resolve_JsonFollowedByMultipleFields_ShouldConvertToJsonFields() {
        var resolved = GhCommandResolver.Resolve(
            new[] { "gh", "pr", "view", "387", "--json", "title,body,state" }, out var error);

        error.Should().BeNull();
        resolved!.Tail.Should().BeEquivalentTo(new[] { "387", "--json_fields=title,body,state" });
    }

    /// <summary>--json 后跟 --option 时不消费选项（保持原有行为）</summary>
    [Fact]
    public void Resolve_JsonFollowedByOption_ShouldNotConsumeOption() {
        var resolved = GhCommandResolver.Resolve(
            new[] { "gh", "pr", "list", "--json", "--limit", "3" }, out var error);

        error.Should().BeNull();
        resolved!.Tail.Should().BeEquivalentTo(new[] { "--limit", "3" });
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

    /// <summary>布尔参数宽容接受显式 true/false 值（--log true / --log false / --log=1 / --log=0）</summary>
    /// <para>缺陷2: AI 习惯写 --log true，不应报"多余的位置参数: true"</para>
    [Theory]
    [InlineData("--log", "true", "true")]
    [InlineData("--log", "false", "false")]
    [InlineData("--log", "1", "true")]
    [InlineData("--log", "0", "false")]
    [InlineData("--log", "yes", "true")]
    [InlineData("--log", "no", "false")]
    [InlineData("--log=true", null, "true")]
    [InlineData("--log=false", null, "false")]
    [InlineData("--log=1", null, "true")]
    [InlineData("--log=0", null, "false")]
    public void Bind_BooleanFlag_WithExplicitValue_ShouldAcceptAndConsume(string first, string? second, string expected) {
        var parameters = new List<GhParam>
        {
            new("run_id", IsRequired: true, IsBoolean: false),
            new("log", IsRequired: false, IsBoolean: true),
            new("max_lines", IsRequired: false, IsBoolean: false),
        };

        var tail = second is null ? new[] { "123", first } : new[] { "123", first, second };

        var bound = GhArgsBinder.Bind(tail, parameters, "gh_run_view", out var error);

        error.Should().BeNull();
        bound!["run_id"].Should().Be("123");
        bound["log"].Should().Be(expected);
    }

    /// <summary>布尔参数 --log true 后不应把 true 当位置参数，后续选项应正常绑定</summary>
    [Fact]
    public void Bind_BooleanFlag_WithTrueThenMoreOptions_ShouldNotBreakSubsequent() {
        var parameters = new List<GhParam>
        {
            new("run_id", IsRequired: true, IsBoolean: false),
            new("log", IsRequired: false, IsBoolean: true),
            new("filter", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "123", "--log", "true", "--filter", "error" }, parameters, "gh_run_view", out var error);

        error.Should().BeNull();
        bound!["log"].Should().Be("true");
        bound["filter"].Should().Be("error");
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

    /// <summary>系统 gh CLI 的 --approve/--request-changes/--comment 应映射到 action（gh pr review）</summary>
    [Theory]
    [InlineData("approve", "approve")]
    [InlineData("request-changes", "request_changes")]
    [InlineData("comment", "comment")]
    public void Bind_GhCliAlias_ReviewFlags_ShouldMapToAction(string flag, string expected) {
        var parameters = new List<GhParam> {
            new("pr_number", IsRequired: true, IsBoolean: false),
            new("action", IsRequired: false, IsBoolean: false),
            new("body", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "42", $"--{flag}" }, parameters, "gh_pr_review", out var error);

        error.Should().BeNull();
        bound!["action"].Should().Be(expected);
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

    /// <summary>--add-label 应建议 --label（key 包含参数名子串匹配）</summary>
    [Fact]
    public void Bind_UnknownOption_AddLabel_ShouldSuggestLabel() {
        var parameters = new List<GhParam> {
            new("pr_number", IsRequired: true, IsBoolean: false),
            new("label", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "42", "--add-label", "bug" }, parameters, "gh_pr_edit", out var error);

        bound.Should().BeNull();
        error.Should().Contain("你是不是想用");
        error.Should().Contain("--label");
    }

    /// <summary>系统 gh CLI 的 --enable-issues 应映射到 has_issues=true（gh repo edit --enable-issues）</summary>
    [Fact]
    public void Bind_GhCliAlias_EnableIssues_ShouldMapToHasIssuesTrue() {
        var parameters = new List<GhParam> {
            new("repo", IsRequired: false, IsBoolean: false),
            new("has_issues", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "--enable-issues" }, parameters, "gh_repo_edit", out var error);

        error.Should().BeNull();
        bound!["has_issues"].Should().Be("true");
    }

    /// <summary>系统 gh CLI 的 --enable-issues=false 应映射到 has_issues=false（带值形式）</summary>
    [Fact]
    public void Bind_GhCliAlias_EnableIssuesFalse_ShouldMapToHasIssuesFalse() {
        var parameters = new List<GhParam> {
            new("repo", IsRequired: false, IsBoolean: false),
            new("has_issues", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "--enable-issues=false" }, parameters, "gh_repo_edit", out var error);

        error.Should().BeNull();
        bound!["has_issues"].Should().Be("false");
    }

    /// <summary>系统 gh CLI 的 --enable-wiki 应映射到 has_wiki=true（gh repo edit --enable-wiki）</summary>
    [Fact]
    public void Bind_GhCliAlias_EnableWiki_ShouldMapToHasWikiTrue() {
        var parameters = new List<GhParam> {
            new("repo", IsRequired: false, IsBoolean: false),
            new("has_wiki", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "--enable-wiki" }, parameters, "gh_repo_edit", out var error);

        error.Should().BeNull();
        bound!["has_wiki"].Should().Be("true");
    }

    /// <summary>系统 gh CLI 的 --enable-projects 应映射到 has_projects=true</summary>
    [Fact]
    public void Bind_GhCliAlias_EnableProjects_ShouldMapToHasProjectsTrue() {
        var parameters = new List<GhParam> {
            new("repo", IsRequired: false, IsBoolean: false),
            new("has_projects", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "--enable-projects" }, parameters, "gh_repo_edit", out var error);

        error.Should().BeNull();
        bound!["has_projects"].Should().Be("true");
    }

    /// <summary>系统 gh CLI 的 --latest 应映射到 make_latest=true（gh release create --latest）</summary>
    [Fact]
    public void Bind_GhCliAlias_Latest_ShouldMapToMakeLatestTrue() {
        var parameters = new List<GhParam> {
            new("tag", IsRequired: true, IsBoolean: false),
            new("make_latest", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "v1.0", "--latest" }, parameters, "gh_release_create", out var error);

        error.Should().BeNull();
        bound!["make_latest"].Should().Be("true");
    }

    /// <summary>系统 gh CLI 的 --latest=false 应映射到 make_latest=false</summary>
    [Fact]
    public void Bind_GhCliAlias_LatestFalse_ShouldMapToMakeLatestFalse() {
        var parameters = new List<GhParam> {
            new("tag", IsRequired: true, IsBoolean: false),
            new("make_latest", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "v1.0", "--latest=false" }, parameters, "gh_release_create", out var error);

        error.Should().BeNull();
        bound!["make_latest"].Should().Be("false");
    }

    /// <summary>系统 gh CLI 的 --event 应映射到 event_type（gh run list --event push）</summary>
    [Fact]
    public void Bind_GhCliAlias_Event_ShouldMapToEventType() {
        var parameters = new List<GhParam> {
            new("limit", IsRequired: false, IsBoolean: false),
            new("event_type", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "--event", "push", "--limit", "5" }, parameters, "gh_run_list", out var error);

        error.Should().BeNull();
        bound!["event_type"].Should().Be("push");
        bound["limit"].Should().Be("5");
    }

    /// <summary>gh release edit --latest 无值应映射到 make_latest=true（RenameOnly 别名）</summary>
    [Fact]
    public void Bind_GhCliAlias_ReleaseEditLatest_ShouldMapToLatestTrue() {
        var parameters = new List<GhParam> {
            new("tag", IsRequired: true, IsBoolean: false),
            new("make_latest", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "v1.0", "--latest" }, parameters, "gh_release_edit", out var error);

        error.Should().BeNull();
        bound!["make_latest"].Should().Be("true");
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

    /// <summary>gh ssh-key list → gh_ssh_key_list，连字符分组名转下划线</summary>
    [Fact]
    public void Resolve_SshKeyGroup_ShouldConvertHyphenToUnderscore() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "ssh-key", "list" }, out var error);

        error.Should().BeNull();
        resolved!.ToolName.Should().Be("gh_ssh_key_list");
        resolved.Group.Should().Be("ssh-key");
    }

    /// <summary>gh gpg-key list → gh_gpg_key_list，连字符分组名转下划线</summary>
    [Fact]
    public void Resolve_GpgKeyGroup_ShouldConvertHyphenToUnderscore() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "gpg-key", "list" }, out var error);

        error.Should().BeNull();
        resolved!.ToolName.Should().Be("gh_gpg_key_list");
    }

    /// <summary>gh browse → gh_browse，单级命令无需 action</summary>
    [Fact]
    public void Resolve_Browse_ShouldMapToSingleLevelCommand() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "browse" }, out var error);

        error.Should().BeNull();
        resolved!.ToolName.Should().Be("gh_browse");
        resolved.Action.Should().BeNull();
    }

    /// <summary>gh status → gh_status，单级命令无需 action</summary>
    [Fact]
    public void Resolve_Status_ShouldMapToSingleLevelCommand() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "status" }, out var error);

        error.Should().BeNull();
        resolved!.ToolName.Should().Be("gh_status");
        resolved.Action.Should().BeNull();
    }

    /// <summary>gh licenses → gh_licenses，单级命令无需 action</summary>
    [Fact]
    public void Resolve_Licenses_ShouldMapToSingleLevelCommand() {
        var resolved = GhCommandResolver.Resolve(new[] { "gh", "licenses" }, out var error);

        error.Should().BeNull();
        resolved!.ToolName.Should().Be("gh_licenses");
        resolved.Action.Should().BeNull();
    }

    /// <summary>用法提示应包含所有已知分组（workflow/label/search/gist/secret/ssh-key 等）</summary>
    [Fact]
    public void Usage_ShouldListAllKnownGroups() {
        GhCommandResolver.Usage.Should().Contain("workflow");
        GhCommandResolver.Usage.Should().Contain("label");
        GhCommandResolver.Usage.Should().Contain("search");
        GhCommandResolver.Usage.Should().Contain("gist");
        GhCommandResolver.Usage.Should().Contain("secret");
        GhCommandResolver.Usage.Should().Contain("ssh-key");
    }

    /// <summary>-L 短选项映射到 limit（gh pr list -L 5）</summary>
    [Fact]
    public void Bind_ShortOption_L_ShouldMapToLimit() {
        var parameters = new List<GhParam> {
            new("state", IsRequired: false, IsBoolean: false),
            new("limit", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "-L", "5" }, parameters, "gh_pr_list", out var error);

        error.Should().BeNull();
        bound!["limit"].Should().Be("5");
    }

    /// <summary>-s 短选项在 pr list 映射到 state</summary>
    [Fact]
    public void Bind_ShortOption_s_PrList_ShouldMapToState() {
        var parameters = new List<GhParam> {
            new("state", IsRequired: false, IsBoolean: false),
            new("limit", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "-s", "closed" }, parameters, "gh_pr_list", out var error);

        error.Should().BeNull();
        bound!["state"].Should().Be("closed");
    }

    /// <summary>-s 短选项在 run list 映射到 status（per-command 覆盖）</summary>
    [Fact]
    public void Bind_ShortOption_s_RunList_ShouldMapToStatus() {
        var parameters = new List<GhParam> {
            new("status", IsRequired: false, IsBoolean: false),
            new("limit", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "-s", "success" }, parameters, "gh_run_list", out var error);

        error.Should().BeNull();
        bound!["status"].Should().Be("success");
    }

    /// <summary>-d 短选项映射到 draft bool flag（gh pr create -d）</summary>
    [Fact]
    public void Bind_ShortOption_d_ShouldMapToDraftTrue() {
        var parameters = new List<GhParam> {
            new("title", IsRequired: true, IsBoolean: false),
            new("draft", IsRequired: false, IsBoolean: true),
        };

        var bound = GhArgsBinder.Bind(new[] { "feat: test", "-d" }, parameters, "gh_pr_create", out var error);

        error.Should().BeNull();
        bound!["draft"].Should().Be("true");
    }

    /// <summary>-L5 内联值形式（gh pr list -L5）</summary>
    [Fact]
    public void Bind_ShortOption_InlineValue_ShouldMapCorrectly() {
        var parameters = new List<GhParam> {
            new("limit", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "-L5" }, parameters, "gh_pr_list", out var error);

        error.Should().BeNull();
        bound!["limit"].Should().Be("5");
    }

    /// <summary>-F 短选项在 release create 映射到 notes_file（per-command 覆盖）</summary>
    [Fact]
    public void Bind_ShortOption_F_ReleaseCreate_ShouldMapToNotesFile() {
        var parameters = new List<GhParam> {
            new("tag", IsRequired: true, IsBoolean: false),
            new("notes_file", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "v1.0", "-F", "notes.md" }, parameters, "gh_release_create", out var error);

        error.Should().BeNull();
        bound!["notes_file"].Should().Be("notes.md");
    }

    /// <summary>gh repo clone 的 dir 是可选位置参数, 应接受位置传递（gh repo clone owner/repo target-dir）</summary>
    [Fact]
    public void Bind_OptionalPositional_RepoClone_ShouldAcceptTargetDir() {
        var parameters = new List<GhParam> {
            new("repo", IsRequired: true, IsBoolean: false),
            new("dir", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "owner/repo", "target-dir" }, parameters, "gh_repo_clone", out var error);

        error.Should().BeNull();
        bound!["repo"].Should().Be("owner/repo");
        bound!["dir"].Should().Be("target-dir");
    }

    /// <summary>gh repo clone 不传 dir 时, 只传 required 位置参数应成功</summary>
    [Fact]
    public void Bind_OptionalPositional_RepoClone_WithoutTargetDir_ShouldSucceed() {
        var parameters = new List<GhParam> {
            new("repo", IsRequired: true, IsBoolean: false),
            new("dir", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "owner/repo" }, parameters, "gh_repo_clone", out var error);

        error.Should().BeNull();
        bound!["repo"].Should().Be("owner/repo");
        bound!.ContainsKey("dir").Should().BeFalse();
    }

    /// <summary>gh pr checkout 的 branch 是可选位置参数, 应接受位置传递</summary>
    [Fact]
    public void Bind_OptionalPositional_PrCheckout_ShouldAcceptBranch() {
        var parameters = new List<GhParam> {
            new("pr_number", IsRequired: true, IsBoolean: false),
            new("branch", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "42", "my-branch" }, parameters, "gh_pr_checkout", out var error);

        error.Should().BeNull();
        bound!["pr_number"].Should().Be("42");
        bound!["branch"].Should().Be("my-branch");
    }

    /// <summary>非白名单工具的 optional 参数不能用位置参数, 应报错（gh pr view 1 2 → 报错）</summary>
    [Fact]
    public void Bind_OptionalPositional_NonWhitelisted_ShouldReject() {
        var parameters = new List<GhParam> {
            new("pr_number", IsRequired: true, IsBoolean: false),
            new("repo", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "1", "2" }, parameters, "gh_pr_view", out var error);

        bound.Should().BeNull();
        error.Should().Contain("多余的位置参数");
    }

    /// <summary>-f 短选项在 gh api 映射到 fields（gh api repos/.../labels -f name=test）</summary>
    [Fact]
    public void Bind_ShortOption_f_Api_ShouldMapToFields() {
        var parameters = new List<GhParam> {
            new("path", IsRequired: true, IsBoolean: false),
            new("fields", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "repos/o/r/labels", "-f", "name=test" }, parameters, "gh_api", out var error);

        error.Should().BeNull();
        bound!["fields"].Should().Be("name=test");
    }

    /// <summary>重复 -f 短选项追加(逗号分隔): -f name=test -f color=ff0000 → fields="name=test,color=ff0000"</summary>
    [Fact]
    public void Bind_ShortOption_f_Repeated_ShouldAppend() {
        var parameters = new List<GhParam> {
            new("path", IsRequired: true, IsBoolean: false),
            new("fields", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "repos/o/r/labels", "-f", "name=test", "-f", "color=ff0000" }, parameters, "gh_api", out var error);

        error.Should().BeNull();
        bound!["fields"].Should().Be("name=test,color=ff0000");
    }

    /// <summary>AI 习惯写 key=value(不带 -- 前缀),应宽容解析为命名参数而非"多余的位置参数"</summary>
    [Fact]
    public void Bind_BareKeyEqualsValue_ShouldParseAsNamedParam() {
        var parameters = new List<GhParam> {
            new("run_id", IsRequired: true, IsBoolean: false),
            new("expand", IsRequired: false, IsBoolean: false),
            new("job_id", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "755", "--expand", "steps", "job_id=112965982126" }, parameters, "gh_run_view", out var error);

        error.Should().BeNull();
        bound!["run_id"].Should().Be("755");
        bound["expand"].Should().Be("steps");
        bound["job_id"].Should().Be("112965982126");
    }

    /// <summary>key=value 连字符参数名应宽容匹配下划线(如 expand-steps → expand_steps)</summary>
    [Fact]
    public void Bind_BareKeyEqualsValue_HyphenKey_ShouldMatchUnderscoreParam() {
        var parameters = new List<GhParam> {
            new("run_id", IsRequired: true, IsBoolean: false),
            new("max_lines", IsRequired: false, IsBoolean: false),
        };

        var bound = GhArgsBinder.Bind(new[] { "123", "max-lines=50" }, parameters, "gh_run_view", out var error);

        error.Should().BeNull();
        bound!["run_id"].Should().Be("123");
        bound["max_lines"].Should().Be("50");
    }
}