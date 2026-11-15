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
        // base.yaml 里真实存在的 3 个文件工具必须被推荐
        content.Should().Contain("file_read");
        content.Should().Contain("file_edit");
        content.Should().Contain("file_write");
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
    public void GetContent_RetiresTwoPhaseToolGroupsProtocol() {
        var content = ToolsSection.GetContent();

        // tool_search / list_groups / map[...] 三级下钻已随 tool_groups 一起退役
        content.Should().NotContain("list_groups");
        content.Should().NotContain("tool_search");
    }
}
