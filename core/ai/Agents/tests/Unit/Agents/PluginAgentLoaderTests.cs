namespace Core.Agents;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.Models.Agent;
using JoinCode.Abstractions.Prompts.ToolPrompts;

public sealed class PluginAgentLoaderTests
{
    private static AgentDefinition CreateDef(string displayId = "executor:code")
    {
        var parts = displayId.Split(':');
        var role = parts[0] switch
        {
            "coordinator" => AgentRole.Coordinator,
            "executor" => AgentRole.Executor,
            _ => AgentRole.Executor,
        };
        var variant = parts.Length > 1 ? parts[1] switch
        {
            "code" => (ExecutorVariant?)ExecutorVariant.Code,
            "doctor" => (ExecutorVariant?)ExecutorVariant.Doctor,
            _ => (ExecutorVariant?)null,
        } : null;
        return new AgentDefinition
        {
            Role = role,
            Variant = variant,
            WhenToUse = "test",
            SystemPrompt = "test prompt",
        };
    }

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

    [Fact]
    public void UnloadWithCascade_ConsumerDependingOnProvider_IsUnloadedFirst()
    {
        var loader = new PluginAgentLoader();

        var providerDef = CreateDef("executor:code");
        var consumerDef = CreateDef("executor:doctor");
        consumerDef.Skills = ["executor:code"];

        var undoProvider = loader.LoadFromPlugin("pluginA", new SimpleProvider([providerDef]));
        loader.LoadFromPlugin("pluginB", new SimpleProvider([consumerDef]));

        loader.GetAll().Should().HaveCount(2);

        undoProvider();

        loader.GetAll().Should().BeEmpty();
        loader.Find("executor:code").Should().BeNull();
        loader.Find("executor:doctor").Should().BeNull();
    }

    [Fact]
    public void UnloadWithCascade_IndependentPlugin_NotAffected()
    {
        var loader = new PluginAgentLoader();

        var providerDef = CreateDef("executor:code");
        var independentDef = CreateDef("executor:doctor");

        var undoProvider = loader.LoadFromPlugin("pluginA", new SimpleProvider([providerDef]));
        loader.LoadFromPlugin("pluginB", new SimpleProvider([independentDef]));

        loader.GetAll().Should().HaveCount(2);

        undoProvider();

        loader.GetAll().Should().HaveCount(1);
        loader.Find("executor:code").Should().BeNull();
        loader.Find("executor:doctor").Should().NotBeNull();
    }

    [Fact]
    public void UnloadWithCascade_ToolsDependency_ConsumerUnloadedFirst()
    {
        var loader = new PluginAgentLoader();

        var providerDef = CreateDef("executor:code");
        var consumerDef = CreateDef("executor:doctor");
        consumerDef.Tools = ["executor:code"];

        var undoProvider = loader.LoadFromPlugin("pluginA", new SimpleProvider([providerDef]));
        loader.LoadFromPlugin("pluginB", new SimpleProvider([consumerDef]));

        loader.GetAll().Should().HaveCount(2);

        undoProvider();

        loader.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void UnloadWithCascade_ChainDependency_AllUnloadedInReverseOrder()
    {
        var loader = new PluginAgentLoader();

        var defA = CreateDef("executor:code");
        var defB = CreateDef("executor:doctor");
        defB.Skills = ["executor:code"];
        var defC = CreateDef("coordinator");
        defC.Skills = ["executor:doctor"];

        var undoA = loader.LoadFromPlugin("pluginA", new SimpleProvider([defA]));
        loader.LoadFromPlugin("pluginB", new SimpleProvider([defB]));
        loader.LoadFromPlugin("pluginC", new SimpleProvider([defC]));

        loader.GetAll().Should().HaveCount(3);

        undoA();

        loader.GetAll().Should().BeEmpty();
        loader.Find("executor:code").Should().BeNull();
        loader.Find("executor:doctor").Should().BeNull();
        loader.Find("coordinator").Should().BeNull();
    }

    [Fact]
    public void UnloadWithCascade_ChangedEventFiresOnce()
    {
        var loader = new PluginAgentLoader();
        var eventCount = 0;
        loader.Changed += (_, _) => eventCount++;

        var providerDef = CreateDef("executor:code");
        var consumerDef = CreateDef("executor:doctor");
        consumerDef.Skills = ["executor:code"];

        var undoProvider = loader.LoadFromPlugin("pluginA", new SimpleProvider([providerDef]));
        loader.LoadFromPlugin("pluginB", new SimpleProvider([consumerDef]));
        eventCount.Should().Be(2);

        undoProvider();
        eventCount.Should().Be(3);
    }
}
