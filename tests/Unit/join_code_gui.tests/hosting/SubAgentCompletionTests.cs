namespace JoinCode.Gui.Tests.Hosting;

/// <summary>
/// 子代理补全数据源测试 — 验证 IJccChatSession.GetAvailableSubAgentsAsync 接口契约
/// 与 SubAgentSummary record 结构。任务1红测试：接口方法与 record 尚不存在时编译失败。
/// </summary>
public class SubAgentCompletionTests
{
    /// <summary>占位会话无真实引擎，返回空列表（占位契约）</summary>
    [Fact]
    public async Task PlaceholderSession_GetAvailableSubAgents_ReturnsEmpty()
    {
        await using var session = new PlaceholderChatSession();
        var agents = await session.GetAvailableSubAgentsAsync();
        agents.Should().BeEmpty();
    }

    /// <summary>SubAgentSummary 携带 Name/Description/DisplayId 三字段</summary>
    [Fact]
    public void SubAgentSummary_HasNameDescriptionDisplayId()
    {
        var summary = new SubAgentSummary(
            Name: "executor:code",
            Description: "Code agent focused on code reading, writing and editing",
            DisplayId: "executor:code");
        summary.Name.Should().Be("executor:code");
        summary.Description.Should().Be("Code agent focused on code reading, writing and editing");
        summary.DisplayId.Should().Be("executor:code");
    }

    /// <summary>SubAgentSummary 为 record，值相等按字段比较</summary>
    [Fact]
    public void SubAgentSummary_RecordEquality_ByField()
    {
        var a = new SubAgentSummary("coordinator", "Coordinator agent", "coordinator");
        var b = new SubAgentSummary("coordinator", "Coordinator agent", "coordinator");
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }
}
