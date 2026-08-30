namespace JoinCode.Gui.Tests.SlashCommands;

/// <summary>
/// ToolCompletionProvider 单元测试 — 验证工具列表与前缀过滤。
/// </summary>
public class ToolCompletionProviderTests
{
    [Fact]
    public void GetTools_EmptyPrefix_ReturnsAllTools()
    {
        var tools = ToolCompletionProvider.GetTools("");
        tools.Should().NotBeEmpty();
        tools.Select(t => t.Name).Should().Contain("ReadFile");
    }

    [Fact]
    public void GetTools_WithPrefix_FiltersResultsCaseInsensitive()
    {
        var tools = ToolCompletionProvider.GetTools("Git");
        tools.Should().NotBeEmpty();
        tools.All(t => t.Name.StartsWith("Git", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public void GetTools_NonMatchingPrefix_ReturnsEmpty()
    {
        var tools = ToolCompletionProvider.GetTools("zzz_nonexistent_xyz");
        tools.Should().BeEmpty();
    }

    [Fact]
    public void GetTools_AllItemsHaveDescription()
    {
        var tools = ToolCompletionProvider.GetTools("");
        tools.All(t => !string.IsNullOrEmpty(t.Description)).Should().BeTrue();
    }
}
