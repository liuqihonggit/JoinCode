namespace CliNodes.Tests;

/// <summary>
/// CliNodeRunner 的入口细节：帮助文本里的工具清单解析、未知工具近似名建议。
/// <para>这两件事决定 LLM 打错工具名时是「得到一条可用提示」还是「看到一段宿主报错」，
/// 因此不放在真实 exe 验收里碰运气，用单测锁住。</para>
/// </summary>
public sealed class CliNodeRunnerTests {
    private const string HelpText = """
        jcc-tool-web — web 工具分组（2 个工具）

        用法:
          jcc-tool-web.exe <工具名> [参数...]
          jcc-tool-web.exe -h

        工具列表（说明复用自 [McpTool] 特性）:
          download_file                      下载文件到指定路径
          web_to_markdown                    Fetch a URL and convert

        单个工具的参数说明:

          download_file — 下载文件到指定路径
            --url                        string    必填   下载 URL
        """;

    [Fact]
    public void ListToolIds_ReadsOnlyToolListSection() {
        var ids = CliNodeRunner.ListToolIds(HelpText);

        Assert.Equal(["download_file", "web_to_markdown"], ids);
    }

    [Fact]
    public void ListToolIds_ReturnsEmptyWhenNoToolList() {
        Assert.Empty(CliNodeRunner.ListToolIds("没有工具列表的文本\njcc-tool-x.exe -h"));
    }

    [Fact]
    public void Suggest_OffersContainingNameForTypo() {
        var suggestions = CliNodeRunner.Suggest("grepsearch", ["glob", "grep", "code_search"]);

        Assert.NotEmpty(suggestions);
        Assert.Contains("grep", suggestions);
    }

    [Fact]
    public void Suggest_OffersEditDistanceMatch() {
        var suggestions = CliNodeRunner.Suggest("grap", ["glob", "grep", "code_search"]);

        Assert.Contains("grep", suggestions);
    }

    [Fact]
    public void Suggest_ReturnsNothingForUnrelatedName() {
        Assert.Empty(CliNodeRunner.Suggest("screenshot", ["glob", "grep", "code_search"]));
    }

    /// <summary>构造一个含 N 个 gh_issue_* 工具的大组帮助文本。</summary>
    private static string BigHelp(int count) {
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("jcc-tool-github — github 工具分组");
        lines.AppendLine();
        lines.AppendLine("用法:");
        lines.AppendLine("  jcc-tool-github.exe <工具名> [参数...]");
        lines.AppendLine();
        lines.AppendLine("工具列表（说明复用自 [McpTool] 特性）:");
        for (var i = 0; i < count; i++) lines.AppendLine($"  gh_issue_tool{i}                      Issue 工具 {i}");
        lines.AppendLine();
        lines.AppendLine("单个工具的参数说明:");
        for (var i = 0; i < count; i++) {
            lines.AppendLine();
            lines.AppendLine($"  gh_issue_tool{i} — Issue 工具 {i}");
            lines.AppendLine($"    --value                      string    可选   参数 {i}");
        }
        return lines.ToString();
    }

    [Fact]
    public void RenderIndex_KeepsSmallGroupVerbatim() {
        var help = BigHelp(3).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(help, CliNodeRunner.RenderIndex(help));
    }

    [Fact]
    public void RenderIndex_DropsParameterDetailsForBigGroup() {
        var index = CliNodeRunner.RenderIndex(BigHelp(40));

        Assert.DoesNotContain("--value", index, StringComparison.Ordinal);
        Assert.Contains("工具索引（40 个", index, StringComparison.Ordinal);
        Assert.Contains("gh_issue/ (40)", index, StringComparison.Ordinal);
        Assert.True(index.Length < BigHelp(40).Length / 3);
    }

    [Fact]
    public void RenderIndex_FoldsOnTwoSegmentsWhenFirstSegmentCoversMost() {
        var index = CliNodeRunner.RenderIndex(BigHelp(40));

        // 第一段全是 gh，折叠后应是 gh_issue/ 一个组，而不是 40 个 gh/ 单条。
        Assert.Contains("gh_issue/", index, StringComparison.Ordinal);
        Assert.DoesNotContain("gh/ (", index, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderIndex_UsesCategorySectionsWhenPresent() {
        var help = """
            jcc-slash — 斜杠命令节点

            命令列表（说明复用自 [ChatCommand] 特性）:

            [Agent] (2 个)
              /advisor                        配置顾问模型
              /agents                         查看和管理代理

            [Model] (2 个)
              /model                          切换模型
              /cost                           查看用量

            单个命令的参数说明:

              /advisor — 配置顾问模型
                --model                    string    可选   顾问模型名称

            """;
        var index = CliNodeRunner.RenderIndex(help);

        // 4 个命令低于阈值，原样输出；这里验证类别解析不会把列表读残。
        Assert.Equal(4, CliNodeRunner.ListToolIds(help).Count);
        Assert.Contains("命令列表", index, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderMatch_PrintsDetailSectionOnce() {
        var match = CliNodeRunner.RenderMatch(BigHelp(40), "tool7");

        Assert.Contains("--value", match, StringComparison.Ordinal);
        var header = "  gh_issue_tool7 — Issue 工具 7";
        var first = match.IndexOf(header, StringComparison.Ordinal);
        Assert.True(first >= 0);
        Assert.Equal(-1, match.IndexOf(header, first + 1, StringComparison.Ordinal));
    }

    [Fact]
    public void RenderMatch_PrefersNameHitOverSummaryHit() {
        var help = """
            jcc-tool-x — x 分组

            工具列表（说明复用自 [McpTool] 特性）:
              alpha_topic                       提到 beta 的工具
              beta_tool                         名字里有 beta

            单个工具的参数说明:

              alpha_topic — 提到 beta 的工具
                --v                          string    可选   v

              beta_tool — 名字里有 beta
                --v                          string    可选   v

            """;
        var match = CliNodeRunner.RenderMatch(help, "beta");

        Assert.True(
            match.IndexOf("beta_tool", StringComparison.Ordinal) < match.IndexOf("alpha_topic", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderMatch_NormalizesHyphenToUnderscore() {
        var match = CliNodeRunner.RenderMatch(BigHelp(3), "issue-tool1");

        Assert.Contains("gh_issue_tool1", match, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderMatch_ReportsNoHitWithGuidance() {
        var match = CliNodeRunner.RenderMatch(BigHelp(3), "zzzznothing");

        Assert.Contains("没有匹配", match, StringComparison.Ordinal);
        Assert.Contains("-h", match, StringComparison.Ordinal);
    }
}
