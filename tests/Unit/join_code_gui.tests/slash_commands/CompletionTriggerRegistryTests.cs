namespace JoinCode.Gui.Tests.SlashCommands;

/// <summary>
/// CompletionTriggerRegistry 单元测试 — 验证触发符注册表按字符索引返回正确 Provider。
/// </summary>
public class CompletionTriggerRegistryTests
{
    [Fact]
    public void TryGet_AtSign_ReturnsAgentProvider()
    {
        var provider = CompletionTriggerRegistry.TryGet('@');
        provider.Should().NotBeNull();
        provider!.TriggerChar.Should().Be('@');
        provider.Mode.Should().Be(SlashCompletionMode.Agent);
        provider.Label.Should().Be("代理补全");
    }

    [Fact]
    public void TryGet_HashSign_ReturnsFileProvider()
    {
        var provider = CompletionTriggerRegistry.TryGet('#');
        provider.Should().NotBeNull();
        provider!.TriggerChar.Should().Be('#');
        provider.Mode.Should().Be(SlashCompletionMode.File);
        provider.Label.Should().Be("文件补全");
    }

    [Fact]
    public void TryGet_Slash_ReturnsCommandProvider()
    {
        var provider = CompletionTriggerRegistry.TryGet('/');
        provider.Should().NotBeNull();
        provider!.TriggerChar.Should().Be('/');
        provider.Mode.Should().Be(SlashCompletionMode.Command);
        provider.Label.Should().Be("斜杠命令");
    }

    [Fact]
    public void TryGet_UnknownChar_ReturnsNull()
    {
        var provider = CompletionTriggerRegistry.TryGet('$');
        provider.Should().BeNull();
    }

    [Fact]
    public void All_ReturnsThreeProviders()
    {
        CompletionTriggerRegistry.All.Should().HaveCount(3);
    }

    [Fact]
    public void AgentProvider_GetCandidates_WithSubAgents_ReturnsFilteredItems()
    {
        var provider = CompletionTriggerRegistry.TryGet('@')!;
        var agents = new List<SubAgentSummary>
        {
            new("executor:code", "代码代理", "executor:code"),
            new("coordinator", "协调代理", "coordinator")
        };
        var context = new CompletionContext
        {
            Session = null!,
            AvailableSubAgentsCache = agents
        };
        var result = provider.GetCandidates("ex", context);
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("executor:code");
    }

    [Fact]
    public void FileProvider_GetCandidates_ReturnsCurrentDirEntries()
    {
        var provider = CompletionTriggerRegistry.TryGet('#')!;
        var context = new CompletionContext { Session = null! };
        var result = provider.GetCandidates("", context);
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void CommandProvider_GetCandidates_WithCache_ReturnsFilteredRanked()
    {
        var provider = CompletionTriggerRegistry.TryGet('/')!;
        var cache = SlashCommandItem.BuiltInCommands;
        var context = new CompletionContext
        {
            Session = null!,
            SlashCommandCache = cache
        };
        var result = provider.GetCandidates("/c", context);
        result.Should().NotBeEmpty();
        result.All(c => c.Name.StartsWith("/c", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }
}
