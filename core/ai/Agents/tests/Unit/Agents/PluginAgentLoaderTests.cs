namespace Core.Agents;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;
using JoinCode.Abstractions.Prompts.ToolPrompts;

public sealed class PluginAgentLoaderTests
{
    private static AgentDefinition CreateDef(string role = "executor:code") => new()
    {
        Role = AgentRole.Executor,
        Variant = ExecutorVariant.Code,
        WhenToUse = "test",
        SystemPrompt = "test prompt",
    };

    private sealed class SimpleProvider : IPluginAgentProvider
    {
        private readonly List<AgentDefinition> _defs;
        public SimpleProvider(List<AgentDefinition> defs) => _defs = defs;
        public IReadOnlyList<AgentDefinition> GetAgentDefinitions() => _defs;
    }

    [Fact]
    public void LoadFromPlugin_SafeAgent_AvailableInGetAll()
    {
        var loader = new PluginAgentLoader();
        var provider = new SimpleProvider([CreateDef()]);

        loader.LoadFromPlugin("pluginA", provider);

        loader.GetAll().Should().HaveCount(1);
        loader.Find("executor:code").Should().NotBeNull();
    }

    [Fact]
    public void LoadFromPlugin_UnsafeAgent_Throws()
    {
        var loader = new PluginAgentLoader();
        var def = CreateDef();
        def.PermissionMode = "auto";
        var provider = new SimpleProvider([def]);

        var act = () => loader.LoadFromPlugin("pluginA", provider);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LoadFromPlugin_ReturnedUndo_RemovesAgent()
    {
        var loader = new PluginAgentLoader();
        var provider = new SimpleProvider([CreateDef()]);

        var undo = loader.LoadFromPlugin("pluginA", provider);
        loader.GetAll().Should().HaveCount(1);

        undo();
        loader.GetAll().Should().BeEmpty();
        loader.Find("executor:code").Should().BeNull();
    }

    [Fact]
    public void LoadFromPlugin_ChangedEvent_FiresOnLoadAndUnload()
    {
        var loader = new PluginAgentLoader();
        var eventCount = 0;
        loader.Changed += (_, _) => eventCount++;

        var undo = loader.LoadFromPlugin("pluginA", new SimpleProvider([CreateDef()]));
        eventCount.Should().Be(1);

        undo();
        eventCount.Should().Be(2);
    }

    [Fact]
    public void LoadFromPlugin_TwoPluginsSameAgentName_LastWins()
    {
        var loader = new PluginAgentLoader();
        var def1 = CreateDef();
        def1.SystemPrompt = "from plugin A";
        var def2 = CreateDef();
        def2.SystemPrompt = "from plugin B";

        loader.LoadFromPlugin("pluginA", new SimpleProvider([def1]));
        loader.LoadFromPlugin("pluginB", new SimpleProvider([def2]));

        loader.GetAll().Should().HaveCount(1);
        loader.Find("executor:code")!.SystemPrompt.Should().Be("from plugin B");
    }

    [Fact]
    public void LoadFromPlugin_UndoOnlyRemovesOwnPlugin()
    {
        var loader = new PluginAgentLoader();
        var def1 = CreateDef();
        def1.SystemPrompt = "A";
        var def2 = CreateDef();
        def2.SystemPrompt = "B";

        var undoA = loader.LoadFromPlugin("pluginA", new SimpleProvider([def1]));
        loader.LoadFromPlugin("pluginB", new SimpleProvider([def2]));

        undoA();
        loader.GetAll().Should().HaveCount(1);
        loader.Find("executor:code")!.SystemPrompt.Should().Be("B");
    }
}
