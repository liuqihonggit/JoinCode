namespace JoinCode.Gui.Tests.SlashCommands;

/// <summary>
/// 斜杠命令权限过滤测试 — 验证 IsEnabled=false 的命令从候选面板排除。
/// </summary>
public class SlashCommandPermissionTests
{
    [Fact]
    public void Filter_ExcludesDisabledCommands()
    {
        var commands = new List<SlashCommandItem>
        {
            new() { Name = "/apple", Description = "", IsEnabled = true },
            new() { Name = "/apricot", Description = "", IsEnabled = false },
            new() { Name = "/banana", Description = "", IsEnabled = true }
        };

        var result = SlashCommandItem.Filter("/a", commands);

        result.Select(c => c.Name).Should().BeEquivalentTo(["/apple"]);
        result.Should().NotContain(c => c.Name == "/apricot");
    }

    [Fact]
    public void Filter_AllEnabled_ReturnsAll()
    {
        var commands = new List<SlashCommandItem>
        {
            new() { Name = "/apple", Description = "" },
            new() { Name = "/apricot", Description = "" }
        };

        var result = SlashCommandItem.Filter("/a", commands);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void Filter_AllDisabled_ReturnsEmpty()
    {
        var commands = new List<SlashCommandItem>
        {
            new() { Name = "/apple", Description = "", IsEnabled = false },
            new() { Name = "/apricot", Description = "", IsEnabled = false }
        };

        var result = SlashCommandItem.Filter("/a", commands);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FromMetadata_MapsIsEnabled()
    {
        var metadata = new List<SlashCommandMetadata>
        {
            new() { Name = "clear", Description = "", IsEnabled = true },
            new() { Name = "compact", Description = "", IsEnabled = false }
        };

        var items = SlashCommandItem.FromMetadata(metadata);
        items.First(c => c.Name == "/clear").IsEnabled.Should().BeTrue();
        items.First(c => c.Name == "/compact").IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void FromMetadata_DefaultIsEnabledTrue()
    {
        var metadata = new List<SlashCommandMetadata>
        {
            new() { Name = "clear", Description = "" }
        };

        var items = SlashCommandItem.FromMetadata(metadata);
        items[0].IsEnabled.Should().BeTrue();
    }
}
