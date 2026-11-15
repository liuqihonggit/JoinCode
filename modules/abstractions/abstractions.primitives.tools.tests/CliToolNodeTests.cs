namespace CliNodes.Tests;

/// <summary>独立 CLI.exe 节点的自描述、帮助渲染与搜索下钻。</summary>
public sealed class CliToolNodeTests {
    private const string NodeYaml = """
        id: rg
        name: jcc-rg
        summary: ripgrep 兼容的代码内容搜索
        category: search
        exe: jcc-rg.exe
        tags: [search, grep, code]
        examples:
          - jcc-rg "NodeRegistry" modules/ --type cs
        parameters:
          - name: pattern
            type: string
            description: 搜索模式
            required: true
            aliases: 位置参数1
          - name: head_limit
            type: integer
            description: 最多输出条数
            required: false
            default: "200"
            aliases: --head-limit
        """;

    [Fact]
    public void LoadNodeFromText_ParsesDescriptorAndParameters() {
        var descriptor = NodeManifestLoader.LoadNodeFromText(NodeYaml, "node.yaml");

        Assert.Equal("rg", descriptor.Id);
        Assert.Equal("jcc-rg.exe", descriptor.Exe);
        Assert.False(descriptor.IsBaseTool);
        Assert.Equal(["search", "grep", "code"], descriptor.Tags);
        Assert.Equal(2, descriptor.Parameters.Length);
        Assert.True(descriptor.Parameters[0].Required);
        Assert.Equal("200", descriptor.Parameters[1].Default);
        Assert.Equal("--head-limit", descriptor.Parameters[1].Aliases);
    }

    [Fact]
    public void Help_RendersDescriptionWithoutInjectingAnythingIntoPrompts() {
        var descriptor = NodeManifestLoader.LoadNodeFromText(NodeYaml, "node.yaml");

        var help = CliToolHelp.Render(descriptor);

        Assert.Contains("ripgrep 兼容的代码内容搜索", help);
        Assert.Contains("--pattern", help);
        Assert.Contains("搜索模式", help);
        Assert.Contains("head_limit 默认值: 200", help);
        Assert.Contains("jcc-rg \"NodeRegistry\" modules/ --type cs", help);
        Assert.Contains("search, grep, code", help);
    }

    [Fact]
    public void Find_MatchesIdSummaryAndTags_RanksByIdFirst() {
        var nodes = new[] {
            NodeManifestLoader.LoadNodeFromText(NodeYaml, "node.yaml"),
            MakeNode("gh_pr_view", "github", "查看 pull request 详情", ["pr", "github"])
        };

        Assert.Equal("rg", Assert.Single(CliToolSearch.Find(nodes, "rg")).Id);
        Assert.Equal("gh_pr_view", Assert.Single(CliToolSearch.Find(nodes, "pull request")).Id);
        Assert.Equal("gh_pr_view", Assert.Single(CliToolSearch.Find(nodes, "github")).Id);
        Assert.Empty(CliToolSearch.Find(nodes, "完全不存在的关键词"));
        Assert.Equal(2, CliToolSearch.Find(nodes, "").Count);
    }

    [Fact]
    public void RenderTree_GroupsByCategory() {
        var nodes = new[] {
            NodeManifestLoader.LoadNodeFromText(NodeYaml, "node.yaml"),
            MakeNode("gh_pr_view", "github", "查看 pull request", [])
        };

        var tree = CliToolSearch.RenderTree(nodes);

        Assert.Contains("github/", tree);
        Assert.Contains("search/", tree);
        Assert.Contains("rg/ — ripgrep 兼容的代码内容搜索", tree);
        Assert.True(tree.IndexOf("github/", StringComparison.Ordinal) < tree.IndexOf("search/", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderTree_TruncatesLongGroupSummary() {
        // 分组节点的 summary 是「前 3 个工具描述」的拼接，真实的 search 组近千字符；
        // 目录树是索引不是文档，必须截断，否则一行就吃掉整屏上下文。
        var node = MakeNode("search", "search", new string('长', 600), []);

        var tree = CliToolSearch.RenderTree([node]);

        var line = tree.Split('\n').First(item => item.Contains("search/ — ", StringComparison.Ordinal));
        Assert.True(line.Length < 160, $"目录树单行过长: {line.Length} 字符");
        Assert.EndsWith("…", line.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void RenderResults_TruncatesLongGroupSummary() {
        var node = MakeNode("search", "search", new string('长', 600), []);

        var text = CliToolSearch.RenderResults([node]);

        Assert.Contains("…", text);
        Assert.DoesNotContain(new string('长', 200), text);
    }

    [Fact]
    public void RenderResults_AlwaysPointsToHelpInsteadOfDumpingSchemas() {
        var nodes = new[] { NodeManifestLoader.LoadNodeFromText(NodeYaml, "node.yaml") };

        var text = CliToolSearch.RenderResults(nodes);

        Assert.Contains("jcc-rg.exe -h", text);
        Assert.DoesNotContain("搜索模式", text);
    }

    [Fact]
    public void ParseNode_MissingId_ThrowsWithSourceName() {
        const string yaml = """
            name: no-id
            summary: 缺少 id
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => NodeManifestLoader.LoadNodeFromText(yaml, "broken.yaml"));
        Assert.Contains("broken.yaml", ex.Message);
        Assert.Contains("缺少 id", ex.Message);
    }

    // 目录树下钻 LoadNodesFromDirectory 涉及真实磁盘 IO，按 JCC9001「测试 0 磁盘读写」
    // 不放单元测试，由 ADR 0080 真实 exe 验收覆盖（jcc tool find / jcc tool tree）。

    private static CliToolDescriptor MakeNode(string id, string category, string summary, string[] tags) =>
        new() { Id = id, Name = id, Summary = summary, Category = category, Tags = tags, Exe = id + ".exe" };
}
