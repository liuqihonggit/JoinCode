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
}
