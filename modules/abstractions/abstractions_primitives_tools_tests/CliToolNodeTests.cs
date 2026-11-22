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
    public void Find_TwoLetterTerm_MatchesWordBoundaryNotMidwordNoise() {
        var nodes = new[] {
            MakeNode("confirm_action", "system", "确认一个危险操作", []),
            MakeNode("permission_list", "system", "列出权限规则", []),
            MakeNode("gh_pr_view", "github", "查看 pull request", [])
        };

        // 两字母查询：「rm」是 confirm/per-**mi**ssion 的单词内子串，不算命中；
        // 「pr」是 gh_pr_view 的下划线边界独立词，命中。
        Assert.DoesNotContain("confirm_action", CliToolSearch.Find(nodes, "rm").Select(n => n.Id));
        Assert.DoesNotContain("permission_list", CliToolSearch.Find(nodes, "rm").Select(n => n.Id));
        Assert.Contains("gh_pr_view", CliToolSearch.Find(nodes, "pr").Select(n => n.Id));
    }

    [Fact]
    public void Find_LongerTerm_KeepsSubstringSemantics() {
        var nodes = new[] { MakeNode("gh_pr_view", "github", "查看 pull request 详情", []) };

        // 长词保留子串语义 —— TASK093 担心的 find pr / find request 既有预期不能破。
        Assert.Equal("gh_pr_view", Assert.Single(CliToolSearch.Find(nodes, "request")).Id);
        Assert.Equal("gh_pr_view", Assert.Single(CliToolSearch.Find(nodes, "pull request")).Id);
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
        Assert.Contains("  rg/ — ripgrep 兼容的代码内容搜索", tree);
        Assert.True(tree.IndexOf("github/", StringComparison.Ordinal) < tree.IndexOf("search/", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderTree_FoldsSingleNodeGroupWhoseIdEqualsCategory() {
        // 13 个分组 exe 都是「一组一节点」，目录名与节点 ID 相同；
        // 再打一遍 "github/ —" 只是把同一个词写两次，目录树会白占一行。
        var tree = CliToolSearch.RenderTree([MakeNode("github", "github", "GitHub 工具", [])]);

        Assert.Equal("github/ — GitHub 工具", tree);
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

    [Fact]
    public void ParseNode_DelegateModeCountsAsProcessIsolated() {        const string yaml = """
            id: github
            name: jcc-tool-github
            summary: github 工具分组
            category: github
            exe: jcc-tool-github.exe
            tools:
              - id: gh_issue_list
                mode: delegate
              - id: gh_pr_view
                mode: host
            """;

        var descriptor = NodeManifestLoader.LoadNodeFromText(yaml, "node.yaml");

        // delegate 不经过宿主，必须计入隔离数；否则审计会把已隔离的组看成「还在宿主里」。
        Assert.Equal(1, descriptor.LocalToolCount);
        Assert.Equal("delegate", descriptor.Tools[0].ModeLabel);
        Assert.True(descriptor.Tools[0].IsLocal);
        Assert.False(descriptor.Tools[1].IsLocal);
    }

    [Fact]
    public void ParseNode_UnknownModeMentionsAllAcceptedValues() {
        const string yaml = """
            id: x
            name: jcc-x
            summary: x
            category: x
            exe: jcc-x.exe
            tools:
              - id: a_tool
                mode: remote
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => NodeManifestLoader.LoadNodeFromText(yaml, "node.yaml"));
        Assert.Contains("local / delegate / host", ex.Message);
    }

    [Fact]
    public void FindTool_AcceptsSlashCommandWithOrWithoutLeadingSlash() {
        const string yaml = """
            id: slash
            name: jcc-slash
            summary: 斜杠命令节点
            category: slash
            exe: jcc-slash.exe
            tools:
              - id: /agents
                mode: host
            """;
        var descriptor = NodeManifestLoader.LoadNodeFromText(yaml, "node.yaml");

        // Git Bash 会把以 / 开头的参数展开成安装根下的路径，还原后往往只剩 agents；
        // 两种写法必须命中同一条，否则 `jcc tool call slash /agents` 会报「工具不存在」。
        Assert.Equal("/agents", descriptor.FindTool("/agents")?.Id);
        Assert.Equal("/agents", descriptor.FindTool("agents")?.Id);
        Assert.Null(descriptor.FindTool("agentz"));
    }

    [Fact]
    public void RenderTree_DistinguishesFullyDelegatedGroupFromBrokenOne() {
        var node = new CliToolDescriptor {
            Id = "media",
            Name = "media",
            Summary = "桌面控制",
            Category = "media",
            Exe = "jcc-tool-media.exe",
            Tools = [
                new CliToolEntry { Id = "mouse_click", Mode = CliExecutionMode.Host },
                new CliToolEntry { Id = "key_press", Mode = CliExecutionMode.Host }
            ]
        };

        var tree = CliToolSearch.RenderTree([node]);

        // 「独立 exe 实现 0/2」会被读成「这组不能用」；它其实 2 个都能用，只是 Agent 功能留在 jcc 内。
        Assert.Contains("全部 2 个由 jcc 内执行", tree);
    }

    // 目录树下钻 LoadNodesFromDirectory 涉及真实磁盘 IO，按 JCC9001「测试 0 磁盘读写」
    // 不放单元测试，由 ADR 0080 真实 exe 验收覆盖（jcc tool find / jcc tool tree）。

    // ===== G2 节点级"省略即继承" =====
    // 期望值来源: Harness² Tree dict 的 write_tree 语义（DSG048 §2.2.2）——
    // 子只声明要改的文件,未提及的从 base 继承；迁移到节点层 = 子只声明差异字段,
    // 未提及的标量/数组从父节点继承,parameters 按 name 合并（子覆盖同名）。

    private const string ParentYaml = """
        id: base_search
        name: jcc-base-search
        summary: 搜索类工具基类
        category: search
        exe: jcc-base-search.exe
        tags: [search, grep, code]
        examples:
          - jcc-base-search "pat" modules/
        parameters:
          - name: pattern
            type: string
            description: 搜索模式
            required: true
          - name: head_limit
            type: integer
            description: 最多条数
            default: "200"
        """;

    private const string ChildYaml = """
        id: child_search
        name: jcc-child-search
        inherits: base_search
        summary: 子类只覆写摘要
        parameters:
          - name: head_limit
            type: integer
            description: 子类收紧默认值
            default: "50"
        """;

    [Fact]
    public void Inheritance_ChildOmitsFields_InheritsFromParent() {
        // 子未声明 category/exe/tags/examples,必须从父继承；id/name/summary 用子的。
        var parent = NodeManifestLoader.LoadNodeFromText(ParentYaml, "base_search/node.yaml");
        var child = NodeManifestLoader.LoadNodeFromText(ChildYaml, "child_search/node.yaml");

        var resolved = CliNodeInheritanceResolver.Resolve(child, [parent]);

        Assert.Equal("base_search", child.Inherits);
        Assert.Equal("child_search", resolved.Id);
        Assert.Equal("jcc-child-search", resolved.Name);
        Assert.Equal("子类只覆写摘要", resolved.Summary);
        // 以下均为"省略即继承"——子 YAML 里一个字都没写。
        Assert.Equal("search", resolved.Category);
        Assert.Equal("jcc-base-search.exe", resolved.Exe);
        Assert.Equal(["search", "grep", "code"], resolved.Tags);
        Assert.Contains("jcc-base-search", Assert.Single(resolved.Examples));
    }

    [Fact]
    public void Inheritance_ParametersMergeByName_ChildOverridesSameName() {
        // parameters 按 name 合并（仿 NodeProfileResolver.MergeConfig：父打底,子覆写同名键）。
        var parent = NodeManifestLoader.LoadNodeFromText(ParentYaml, "base_search/node.yaml");
        var child = NodeManifestLoader.LoadNodeFromText(ChildYaml, "child_search/node.yaml");

        var resolved = CliNodeInheritanceResolver.Resolve(child, [parent]);

        Assert.Equal(2, resolved.Parameters.Length);
        var headLimit = Assert.Single(resolved.Parameters, p => p.Name == "head_limit");
        Assert.Equal("50", headLimit.Default);                 // 子覆写
        Assert.Equal("子类收紧默认值", headLimit.Description);   // 子覆写
        var pattern = Assert.Single(resolved.Parameters, p => p.Name == "pattern");
        Assert.Equal("搜索模式", pattern.Description);            // 子未提及,父继承
        Assert.True(pattern.Required);
    }

    [Fact]
    public void Inheritance_MultiLevelChain_ResolvesDepthThree() {
        // 三层链 base → mid → leaf,leaf 的未声明字段应穿透两层继承到 base。
        var baseNode = NodeManifestLoader.LoadNodeFromText(ParentYaml, "base/node.yaml");
        var midYaml = """
            id: mid
            name: jcc-mid
            inherits: base_search
            tags: [search, refined]
            """;
        var leafYaml = """
            id: leaf
            name: jcc-leaf
            inherits: mid
            """;
        var mid = NodeManifestLoader.LoadNodeFromText(midYaml, "mid/node.yaml");
        var leaf = NodeManifestLoader.LoadNodeFromText(leafYaml, "leaf/node.yaml");

        var resolved = CliNodeInheritanceResolver.Resolve(leaf, [baseNode, mid]);

        Assert.Equal("jcc-leaf", resolved.Name);
        Assert.Equal("search", resolved.Category);                       // 来自 base
        Assert.Equal(["search", "refined"], resolved.Tags);              // mid 覆写 base
        Assert.Equal(2, resolved.Parameters.Length);                     // 来自 base
    }

    [Fact]
    public void Inheritance_CircularChain_ThrowsWithNodeIds() {
        const string aYaml = """
            id: a
            name: jcc-a
            inherits: b
            summary: A
            """;
        const string bYaml = """
            id: b
            name: jcc-b
            inherits: a
            summary: B
            """;
        var a = NodeManifestLoader.LoadNodeFromText(aYaml, "a/node.yaml");
        var b = NodeManifestLoader.LoadNodeFromText(bYaml, "b/node.yaml");

        var ex = Assert.Throws<InvalidOperationException>(() => CliNodeInheritanceResolver.Resolve(a, [a, b]));
        Assert.Contains("a", ex.Message);
        Assert.Contains("b", ex.Message);
        Assert.Contains("循环", ex.Message);
    }

    [Fact]
    public void Inheritance_MissingParent_ThrowsWithGuidance() {
        var child = NodeManifestLoader.LoadNodeFromText(ChildYaml, "child_search/node.yaml");

        var ex = Assert.Throws<InvalidOperationException>(() => CliNodeInheritanceResolver.Resolve(child, []));
        Assert.Contains("base_search", ex.Message);
        Assert.Contains("继承", ex.Message);
    }

    [Fact]
    public void Inheritance_ParameterDeclaresOnlyDefault_InheritsTypeAndDescriptionFromParent() {
        // P0 回归：子参数只写 default —— 其余字段必须从父继承，不能被 parser 的默认值冲掉。
        // 旧实现整体替换会把 type=integer 退化为 string、description 变 null，且真机不报错。
        var parent = NodeManifestLoader.LoadNodeFromText(ParentYaml, "base/node.yaml");
        const string childOnlyDefault = """
            id: tiny
            name: jcc-tiny
            inherits: base_search
            parameters:
              - name: head_limit
                default: "50"
            """;
        var child = NodeManifestLoader.LoadNodeFromText(childOnlyDefault, "tiny/node.yaml");

        var resolved = CliNodeInheritanceResolver.Resolve(child, [parent]);

        var headLimit = Assert.Single(resolved.Parameters, p => p.Name == "head_limit");
        Assert.Equal("integer", headLimit.Type);        // 父继承（子未写 type）
        Assert.Equal("最多条数", headLimit.Description);  // 父继承（子未写 description）
        Assert.Equal("50", headLimit.Default);           // 子覆写
        Assert.False(headLimit.Required);
    }

    [Fact]
    public void Inheritance_ToolEntryInheritsAliases() {
        // 子条目只改 mode，aliases 应从父继承 —— 否则 FindTool 用别名调用会失效。
        const string parentWithAliases = """
            id: base_group
            name: jcc-base-group
            summary: 带别名子工具的父分组
            category: base
            exe: jcc-base-group.exe
            tools:
              - id: do_thing
                mode: local
                aliases: [dt, dothing]
            """;
        const string childTool = """
            id: child_group
            name: jcc-child-group
            inherits: base_group
            tools:
              - id: do_thing
                mode: delegate
            """;
        var parent = NodeManifestLoader.LoadNodeFromText(parentWithAliases, "base/node.yaml");
        var child = NodeManifestLoader.LoadNodeFromText(childTool, "child/node.yaml");

        var resolved = CliNodeInheritanceResolver.Resolve(child, [parent]);

        var entry = Assert.Single(resolved.Tools);
        Assert.Equal(CliExecutionMode.Delegate, entry.Mode);     // 子覆写
        Assert.Equal(["dt", "dothing"], entry.Aliases);          // 父继承
        Assert.Equal("do_thing", resolved.FindTool("dt")?.Id);   // 别名仍可调用
    }

    [Fact]
    public void ResolveAll_MissingParent_KeepsNodeWithPlaceholderDescription() {
        // 缺父时保留节点（可发现性优先），但 parser 放宽后 name/summary 是空串，
        // 必须补占位描述，否则 tree 显示 "orphan/ — " 且 find 用关键词搜不到。
        const string orphanYaml = """
            id: orphan
            name: ""
            inherits: missing_parent
            """;
        var orphan = NodeManifestLoader.LoadNodeFromText(orphanYaml, "orphan/node.yaml");

        var resolved = Assert.Single(CliNodeInheritanceResolver.ResolveAll([orphan]));

        Assert.Equal("orphan", resolved.Name);
        Assert.Contains("missing_parent", resolved.Summary);
        Assert.Contains("继承失败", resolved.Summary);
    }

    // ===== G2 生产落地（TASK097 §14）: node.inherit.yaml 叠加 =====

    [Fact]
    public void ApplyOverlay_DeclaredFieldsWin_OmittedFieldsInheritedFromBaseline() {
        // 生成器全量重写 node.yaml；人工差异放 node.inherit.yaml 只写要改的字段。
        var baseline = NodeManifestLoader.LoadNodeFromText(ParentYaml, "search/node.yaml");
        const string overlayYaml = """
            id: base_search
            tags: [search, overlay-only]
            parameters:
              - name: head_limit
                default: "50"
            """;
        var overlay = NodeManifestLoader.LoadOverlayFromText(overlayYaml, "search/node.inherit.yaml");

        var merged = CliNodeInheritanceResolver.ApplyOverlay(overlay, baseline);

        // 显式字段覆盖；省略的 name/summary/category/exe/examples 从基准继承。
        Assert.Equal(["search", "overlay-only"], merged.Tags);
        Assert.Equal("jcc-base-search", merged.Name);
        Assert.Equal("搜索类工具基类", merged.Summary);
        Assert.Equal("jcc-base-search.exe", merged.Exe);
        Assert.Contains("jcc-base-search", Assert.Single(merged.Examples));
        // parameters 按 name 合并：子只写 default，父的 type/description/required 不能退化。
        var headLimit = Assert.Single(merged.Parameters, p => p.Name == "head_limit");
        Assert.Equal("50", headLimit.Default);
        Assert.Equal("integer", headLimit.Type);
        Assert.Equal("最多条数", headLimit.Description);
        Assert.Contains(merged.Parameters, p => p.Name == "pattern");
    }

    [Fact]
    public void ApplyOverlay_IdMismatch_ThrowsWithActionableMessage() {
        var baseline = NodeManifestLoader.LoadNodeFromText(ParentYaml, "search/node.yaml");
        var overlay = NodeManifestLoader.LoadOverlayFromText(
            "id: wrong_group\ntags: [x]\n", "search/node.inherit.yaml");

        var ex = Assert.Throws<InvalidOperationException>(
            () => CliNodeInheritanceResolver.ApplyOverlay(overlay, baseline));

        Assert.Contains("wrong_group", ex.Message);
        Assert.Contains("base_search", ex.Message);
        Assert.Contains("正确写法", ex.Message);
    }

    private static CliToolDescriptor MakeNode(string id, string category, string summary, string[] tags) =>
        new() { Id = id, Name = id, Summary = summary, Category = category, Tags = tags, Exe = id + ".exe" };
}
