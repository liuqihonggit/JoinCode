namespace Core.Tests.Prompts;

/// <summary>
/// ToolsSection 单元测试 —— 锁定「提示词只推荐真被声明的工具」这条契约（ADR 0157 步骤 8）。
/// <para>旧版断言的是「不要滥用 bash」双向压制；首轮 tools 已砍到 base.yaml 的 4 个后，
/// 更致命的错误是提示词让你用清单里根本没有的工具，故断言改为对齐 base.yaml 与 CLI 发现通道。</para>
/// </summary>
public sealed class ToolsSectionTests {
    [Fact]
    public void GetContent_OnlyRecommendsDeclaredBaseTools() {
        var content = ToolsSection.GetContent();

        content.Should().NotBeNull();
        // base.yaml 里真实存在的 3 个文件工具必须被推荐；名字用运行时注册名（read/write/edit）
        content.Should().Contain("使用 read 读取文件");
        content.Should().Contain("使用 edit 做局部修改");
        content.Should().Contain("使用 write 创建或整体覆写文件");
    }

    [Fact]
    public void GetContent_DoesNotRecommendUndeclaredTools() {
        var content = ToolsSection.GetContent();

        // Glob / Grep / TodoWrite 已不在首轮 tools 字段里，再推荐就是让模型调用不存在的接口
        content.Should().NotContain("Glob");
        content.Should().NotContain("Grep");
        content.Should().NotContain("TodoWrite");
    }

    [Fact]
    public void GetContent_DescribesCliDiscoveryChannel() {
        var content = ToolsSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("jcc tool tree", "全量发现入口");
        content.Should().Contain("jcc tool find", "关键词搜索");
        content.Should().Contain("jcc tool call", "进程隔离执行");
    }

    [Fact]
    public void GetContent_ListsEveryGroupExeWithItsBlurb() {
        var content = ToolsSection.GetContent();
        var index = CliGroupIndex.LoadDefault();

        // 提示词的价值都在这一段：模型首轮看不到这些工具，只有看到「有个 exe 叫这个名字、
        // 它是管什么的」才会想到去 `<exe> -h`。任何一个组漏写，那个组就等于对模型不存在。
        content.Should().NotBeNull();
        foreach (var group in index.Groups) {
            content.Should().Contain(group.Exe, $"分组 {group.Id} 的 exe 未出现在提示词里");
            content.Should().Contain(group.Blurb, $"分组 {group.Id} 的定位说明未出现在提示词里");
        }
        content.Should().Contain($"分装在 {index.GroupCount} 个独立 exe 里（共 {index.ToolCount} 个工具",
            "总量与分组数必须与索引一致，不能写成写死的 540+");
    }

    [Fact]
    public void GetContent_ListsEveryAlwaysRegisteredTool() {
        var content = ToolsSection.GetContent();
        var registry = BaseToolRegistry.LoadDefault();

        content.Should().NotBeNull();
        // 首轮 tools 字段里的工具必须在提示词里逐个点名。少一个，模型就把它当不存在；
        // 尤其是 Agent 内部的规划/目标工具——它们不在 CLI 目录树里，没有第二条发现通道。
        foreach (var tool in registry.All) {
            content.Should().Contain(tool.Id, $"基础工具 {tool.Id} 未出现在首轮提示词");
        }
        content.Should().Contain($"始终注册的工具（共 {registry.Count} 个");
    }

    [Fact]
    public void GetContent_TeachesOnDemandHelpInsteadOfDumpingIt() {
        var content = ToolsSection.GetContent();

        content.Should().NotBeNull();
        // 说明书是「按需读」的：先把索引/关键词检索/单工具参数三层入口说清，
        // 否则模型要么不知道查什么，要么把 5 KB 帮助整段塞进上下文。
        content.Should().Contain("-h");
        content.Should().Contain("不要预先整段读");
        content.Should().Contain("jcc tool call");
    }

    [Fact]
    public void GetContent_RestrictedDomain_ListsOnlyGroupsWithVisibleTools() {
        using var scope = PromptToolGroupScope.Enter(null, new CliToolScopeSnapshot { AllowedTools = ["read"] });

        var content = ToolsSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("jcc-tool-file", "read 属于 file 组");
        content.Should().NotContain("jcc-tool-git", "git 组没有授权工具，不能出现在首轮目录");
        content.Should().Contain("按当前 agent 工具域裁剪");
    }

    [Fact]
    public void GetContent_EmptyDomainIntersection_HidesAllGroups() {
        using var scope = PromptToolGroupScope.Enter(null, new CliToolScopeSnapshot {
            AllowedTools = ["__no_tools_permitted__"]
        });

        var content = ToolsSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("工具域内没有独立 CLI 工具组");
        content.Should().NotContain("jcc-tool-file");
    }

    [Fact]
    public void GetContent_DenyOnly_KeepsOtherGroupsVisible() {
        using var scope = PromptToolGroupScope.Enter(null, new CliToolScopeSnapshot { DeniedTools = ["commit"] });

        var content = ToolsSection.GetContent();

        content.Should().NotBeNull();
        content.Should().Contain("jcc-tool-file");
        content.Should().Contain("jcc-tool-git", "只禁 commit 时 git 组其它工具仍可用");
        content.Should().Contain("按当前 agent 工具域裁剪");
    }

    [Fact]
    public void GetContent_RetiresTwoPhaseToolGroupsProtocol() {
        var content = ToolsSection.GetContent();

        // tool_search / list_groups / map[...] 三级下钻已随 tool_groups 一起退役
        content.Should().NotContain("list_groups");
        content.Should().NotContain("tool_search");
    }
}
