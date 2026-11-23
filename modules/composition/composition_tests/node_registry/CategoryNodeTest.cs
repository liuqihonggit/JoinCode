namespace JoinCode.Composition.NodeRegistry.Tests;

/// <summary>
/// CommandCategoryNode + PromptSectionCategoryNode 单元测试
/// </summary>
public sealed class CategoryNodeTest {
    [Fact]
    public void CommandCategoryNode_Properties_Correct() {
        var commands = new IChatCommand[] { new StubCommand("help"), new StubCommand("exit") };
        var node = new CommandCategoryNode("session-command", ChatCommandCategory.Session, commands);

        node.NodeId.Should().Be("session-command");
        node.Category.Should().Be(ChatCommandCategory.Session);
        node.Commands.Should().HaveCount(2);
        node.Commands.Select(c => c.Name).Should().BeEquivalentTo(["help", "exit"]);
        node.State.Should().Be(NodeState.Active);
        node.Metadata.Priority.Should().Be(100);
    }

    [Fact]
    public void CommandCategoryNode_EmptyId_Throws() {
        var act = () => new CommandCategoryNode("", ChatCommandCategory.Other, []);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CommandCategoryNode_CustomPriority() {
        var node = new CommandCategoryNode("config-command", ChatCommandCategory.Config, [], priority: 500);
        node.Metadata.Priority.Should().Be(500);
    }

    [Fact]
    public void PromptSectionCategoryNode_AlwaysGroup_Correct() {
        var sections = new SystemPromptSection[] {
            SystemPromptSection.Cached("intro", () => "hello"),
            SystemPromptSection.Cached("env", () => "env-info"),
        };
        var node = new PromptSectionCategoryNode("always-prompt", PromptSectionGroup.Always, sections);

        node.NodeId.Should().Be("always-prompt");
        node.Group.Should().Be(PromptSectionGroup.Always);
        node.Sections.Should().HaveCount(2);
        node.State.Should().Be(NodeState.Active);
        node.Metadata.Priority.Should().Be(100);
    }

    [Fact]
    public void PromptSectionCategoryNode_AgentModeGroup_Correct() {
        var node = new PromptSectionCategoryNode("agent-mode-prompt", PromptSectionGroup.AgentMode, []);
        node.Group.Should().Be(PromptSectionGroup.AgentMode);
    }

    [Fact]
    public void PromptSectionCategoryNode_CoordinatorModeGroup_Correct() {
        var node = new PromptSectionCategoryNode("coordinator-mode-prompt", PromptSectionGroup.CoordinatorMode, []);
        node.Group.Should().Be(PromptSectionGroup.CoordinatorMode);
    }

    [Fact]
    public void PromptSectionCategoryNode_EmptyId_Throws() {
        var act = () => new PromptSectionCategoryNode("", PromptSectionGroup.Always, []);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PromptSectionCategoryNode_CustomPriority() {
        var node = new PromptSectionCategoryNode("always-prompt", PromptSectionGroup.Always, [], priority: 200);
        node.Metadata.Priority.Should().Be(200);
    }

    private sealed class StubCommand(string name) : IChatCommand {
        public string Name { get; } = name;
        public string Description { get; } = "";
        public string Usage { get; } = "";
        public string[] Aliases { get; } = [];
        public string ArgumentHint { get; } = "";
        public bool IsHidden { get; } = false;
        public bool IsEnabled { get; } = true;
        public Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) => Task.FromResult(ChatCommandResult.Continue());
    }
}
