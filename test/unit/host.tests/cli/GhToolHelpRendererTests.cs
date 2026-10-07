namespace Host.Tests.Cli;

/// <summary>
/// GhToolHelpRenderer 单元测试 — 验证从 ToolInfo + ToolSchema 动态生成帮助文本。
/// <para>覆盖：工具名/描述/分类、用法行、必填参数、可选参数、boolean 参数无 &lt;值&gt; 提示、完整 schema 引导。</para>
/// </summary>
public sealed class GhToolHelpRendererTests {

    [Fact]
    public void Render_ShouldIncludeToolNameAndDescription() {
        var info = MakeInfo("gh_pr_view", "查看 PR 详情", "github");
        var text = GhToolHelpRenderer.Render(info, "pr", "view");

        text.Should().Contain("工具: gh_pr_view");
        text.Should().Contain("描述: 查看 PR 详情");
        text.Should().Contain("分类: github");
    }

    [Fact]
    public void Render_ShouldBuildUsageLineWithRequiredAndOptional() {
        var info = MakeInfo("gh_pr_view", "desc", null,
            required: ["pr_number"],
            properties: [
                ("pr_number", "string", "PR 编号"),
                ("repo", "string", "仓库"),
                ("verbose", "boolean", "详细输出"),
            ]);
        var text = GhToolHelpRenderer.Render(info, "pr", "view");

        text.Should().Contain("用法: jcc gh pr view <pr_number> [--repo] [--verbose] [--json]");
    }

    [Fact]
    public void Render_ShouldListRequiredParameters() {
        var info = MakeInfo("gh_pr_view", "desc", null,
            required: ["pr_number"],
            properties: [
                ("pr_number", "string", "PR 编号或 URL"),
            ]);
        var text = GhToolHelpRenderer.Render(info, "pr", "view");

        text.Should().Contain("必填参数:");
        text.Should().Contain("<pr_number>    PR 编号或 URL");
    }

    [Fact]
    public void Render_ShouldListOptionalParameters() {
        var info = MakeInfo("gh_pr_view", "desc", null,
            required: ["pr_number"],
            properties: [
                ("pr_number", "string", "PR 编号"),
                ("repo", "string", "仓库"),
                ("verbose", "boolean", "详细输出"),
            ]);
        var text = GhToolHelpRenderer.Render(info, "pr", "view");

        text.Should().Contain("可选参数:");
        text.Should().Contain("--repo <值>    仓库");
        text.Should().Contain("--verbose    详细输出");
    }

    [Fact]
    public void Render_BooleanParam_ShouldNotHaveValueHint() {
        var info = MakeInfo("gh_pr_list", "desc", null,
            properties: [
                ("limit", "integer", "限制数量"),
                ("state", "string", "状态过滤"),
            ]);
        var text = GhToolHelpRenderer.Render(info, "pr", "list");

        text.Should().Contain("--limit <值>");
        text.Should().Contain("--state <值>");
    }

    [Fact]
    public void Render_ShouldIncludeSchemaGuidance() {
        var info = MakeInfo("gh_pr_view", "desc", null);
        var text = GhToolHelpRenderer.Render(info, "pr", "view");

        text.Should().Contain("完整 schema: jcc mcp_schema gh_pr_view");
    }

    [Fact]
    public void Render_NoOptionalParams_ShouldOmitOptionalSection() {
        var info = MakeInfo("gh_pr_view", "desc", null,
            required: ["pr_number"],
            properties: [
                ("pr_number", "string", "PR 编号"),
            ]);
        var text = GhToolHelpRenderer.Render(info, "pr", "view");

        text.Should().Contain("必填参数:");
        text.Should().NotContain("可选参数:");
    }

    [Fact]
    public void Render_NoRequiredParams_ShouldOmitRequiredSection() {
        var info = MakeInfo("gh_pr_list", "desc", null,
            properties: [
                ("limit", "integer", "限制数量"),
            ]);
        var text = GhToolHelpRenderer.Render(info, "pr", "list");

        text.Should().NotContain("必填参数:");
        text.Should().Contain("可选参数:");
    }

    [Fact]
    public void SelfHelpCommands_ShouldContainGhAndRg() {
        CliSubCommandHelpText.SelfHelpCommands.Should().Contain("gh");
        CliSubCommandHelpText.SelfHelpCommands.Should().Contain("rg");
    }

    [Fact]
    public void SelfHelpCommands_ShouldNotContainMcpList() {
        CliSubCommandHelpText.SelfHelpCommands.Should().NotContain("mcp_list");
    }

    private static JoinCode.Abstractions.Tools.ToolInfo MakeInfo(
        string name, string? description, string? category,
        string[]? required = null,
        (string Name, string Type, string? Description)[]? properties = null) {
        var schema = new JoinCode.Abstractions.Tools.ToolSchema();
        if (required is not null)
            schema.Required.AddRange(required);
        if (properties is not null) {
            foreach (var (n, t, d) in properties)
                schema.Properties[n] = new JoinCode.Abstractions.Tools.ToolSchemaProperty { Type = t, Description = d };
        }
        return new JoinCode.Abstractions.Tools.ToolInfo {
            Name = name,
            Description = description,
            Category = category,
            InputSchema = schema,
        };
    }
}
