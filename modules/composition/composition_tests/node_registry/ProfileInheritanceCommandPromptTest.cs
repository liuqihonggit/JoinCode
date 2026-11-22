namespace JoinCode.Composition.NodeRegistry.Tests;

/// <summary>
/// Profile 继承链对 command-dispatch / prompt-assembly 消费点的生效验证（DSG051 阶段3）
/// </summary>
public sealed class ProfileInheritanceCommandPromptTest {
    [Fact]
    public void BaseProfile_CommandDispatch_HasAll15Categories() {
        var profiles = LoadBaseProfile();
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("base");

        resolved.Consumers.Should().ContainKey("command-dispatch");
        var nodes = resolved.Consumers["command-dispatch"].Nodes;
        nodes.Should().HaveCount(15);
        nodes.Select(n => n.Id).Should().Contain([
            "会话-command", "模型-command", "代码-command", "工具-command",
            "配置-command", "信息-command", "系统-command", "认证-command",
            "智能体-command", "任务-command", "连接-command", "平台-command",
            "社交-command", "法律-command", "其他-command"
        ]);
    }

    [Fact]
    public void BaseProfile_PromptAssembly_Has3Groups() {
        var profiles = LoadBaseProfile();
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("base");

        resolved.Consumers.Should().ContainKey("prompt-assembly");
        var nodes = resolved.Consumers["prompt-assembly"].Nodes;
        nodes.Should().HaveCount(3);
        nodes.Select(n => n.Id).Should().Contain([
            "always-prompt", "agent-mode-prompt", "coordinator-mode-prompt"
        ]);
    }

    [Fact]
    public void HeadlessProfile_InheritsBase_CommandDispatchPresent() {
        var profiles = LoadBaseAndHeadlessProfiles();
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("headless");

        resolved.Consumers.Should().ContainKey("command-dispatch");
        resolved.Consumers["command-dispatch"].Nodes.Should().NotBeEmpty();
    }

    [Fact]
    public void HeadlessProfile_InheritsBase_PromptAssemblyPresent() {
        var profiles = LoadBaseAndHeadlessProfiles();
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("headless");

        resolved.Consumers.Should().ContainKey("prompt-assembly");
        resolved.Consumers["prompt-assembly"].Nodes.Should().NotBeEmpty();
    }

    [Fact]
    public void HeadlessProfile_ExcludesSocialCommand() {
        var profiles = LoadBaseAndHeadlessProfiles();
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("headless");

        resolved.Consumers["command-dispatch"].Nodes
            .Should().NotContain(n => n.Id == "社交-command");
    }

    [Fact]
    public void HeadlessProfile_ExcludesCoordinatorModePrompt() {
        var profiles = LoadBaseAndHeadlessProfiles();
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("headless");

        resolved.Consumers["prompt-assembly"].Nodes
            .Should().NotContain(n => n.Id == "coordinator-mode-prompt");
    }

    [Fact]
    public void HeadlessProfile_OtherCommandsStillPresent() {
        var profiles = LoadBaseAndHeadlessProfiles();
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("headless");

        var commandNodes = resolved.Consumers["command-dispatch"].Nodes;
        commandNodes.Should().HaveCount(14);
        commandNodes.Select(n => n.Id).Should().Contain([
            "会话-command", "模型-command", "代码-command", "工具-command",
            "配置-command", "信息-command", "系统-command", "认证-command",
            "智能体-command", "任务-command", "连接-command", "平台-command",
            "法律-command", "其他-command"
        ]);
    }

    [Fact]
    public void HeadlessProfile_OtherPromptsStillPresent() {
        var profiles = LoadBaseAndHeadlessProfiles();
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("headless");

        var promptNodes = resolved.Consumers["prompt-assembly"].Nodes;
        promptNodes.Should().HaveCount(2);
        promptNodes.Select(n => n.Id).Should().Contain([
            "always-prompt", "agent-mode-prompt"
        ]);
    }

    [Fact]
    public void CiProfile_InheritsHeadless_CommandDispatchPresent() {
        var profiles = LoadAllProfiles();
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("ci");

        resolved.Consumers.Should().ContainKey("command-dispatch");
        resolved.Consumers["command-dispatch"].Nodes
            .Should().NotContain(n => n.Id == "社交-command");
    }

    [Fact]
    public void SdkProfile_InheritsHeadless_PromptAssemblyPresent() {
        var profiles = LoadAllProfiles();
        var resolver = new NodeProfileResolver(profiles);
        var resolved = resolver.Resolve("sdk");

        resolved.Consumers.Should().ContainKey("prompt-assembly");
        resolved.Consumers["prompt-assembly"].Nodes
            .Should().NotContain(n => n.Id == "coordinator-mode-prompt");
    }

    private static FrozenDictionary<string, NodeProfile> LoadBaseProfile() {
        var baseYaml = """
            name: base
            consumers:
              command-dispatch:
                nodes:
                  - id: 会话-command
                    priority: 100
                  - id: 模型-command
                    priority: 200
                  - id: 代码-command
                    priority: 300
                  - id: 工具-command
                    priority: 400
                  - id: 配置-command
                    priority: 500
                  - id: 信息-command
                    priority: 600
                  - id: 系统-command
                    priority: 700
                  - id: 认证-command
                    priority: 800
                  - id: 智能体-command
                    priority: 900
                  - id: 任务-command
                    priority: 1000
                  - id: 连接-command
                    priority: 1100
                  - id: 平台-command
                    priority: 1200
                  - id: 社交-command
                    priority: 1300
                  - id: 法律-command
                    priority: 1400
                  - id: 其他-command
                    priority: 1500
              prompt-assembly:
                nodes:
                  - id: always-prompt
                    priority: 100
                  - id: agent-mode-prompt
                    priority: 200
                  - id: coordinator-mode-prompt
                    priority: 300
            """;
        var baseProfile = NodeProfileLoader.ParseYaml("base", baseYaml);
        return new Dictionary<string, NodeProfile> { ["base"] = baseProfile }.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, NodeProfile> LoadBaseAndHeadlessProfiles() {
        var baseYaml = """
            name: base
            consumers:
              command-dispatch:
                nodes:
                  - id: 会话-command
                    priority: 100
                  - id: 模型-command
                    priority: 200
                  - id: 代码-command
                    priority: 300
                  - id: 工具-command
                    priority: 400
                  - id: 配置-command
                    priority: 500
                  - id: 信息-command
                    priority: 600
                  - id: 系统-command
                    priority: 700
                  - id: 认证-command
                    priority: 800
                  - id: 智能体-command
                    priority: 900
                  - id: 任务-command
                    priority: 1000
                  - id: 连接-command
                    priority: 1100
                  - id: 平台-command
                    priority: 1200
                  - id: 社交-command
                    priority: 1300
                  - id: 法律-command
                    priority: 1400
                  - id: 其他-command
                    priority: 1500
              prompt-assembly:
                nodes:
                  - id: always-prompt
                    priority: 100
                  - id: agent-mode-prompt
                    priority: 200
                  - id: coordinator-mode-prompt
                    priority: 300
            """;
        var headlessYaml = """
            name: headless
            inherits: base
            excludes:
              - 社交-command
              - coordinator-mode-prompt
            """;
        var baseProfile = NodeProfileLoader.ParseYaml("base", baseYaml);
        var headlessProfile = NodeProfileLoader.ParseYaml("headless", headlessYaml);
        return new Dictionary<string, NodeProfile> {
            ["base"] = baseProfile,
            ["headless"] = headlessProfile
        }.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, NodeProfile> LoadAllProfiles() {
        var baseYaml = """
            name: base
            consumers:
              command-dispatch:
                nodes:
                  - id: 会话-command
                    priority: 100
                  - id: 模型-command
                    priority: 200
                  - id: 代码-command
                    priority: 300
                  - id: 工具-command
                    priority: 400
                  - id: 配置-command
                    priority: 500
                  - id: 信息-command
                    priority: 600
                  - id: 系统-command
                    priority: 700
                  - id: 认证-command
                    priority: 800
                  - id: 智能体-command
                    priority: 900
                  - id: 任务-command
                    priority: 1000
                  - id: 连接-command
                    priority: 1100
                  - id: 平台-command
                    priority: 1200
                  - id: 社交-command
                    priority: 1300
                  - id: 法律-command
                    priority: 1400
                  - id: 其他-command
                    priority: 1500
              prompt-assembly:
                nodes:
                  - id: always-prompt
                    priority: 100
                  - id: agent-mode-prompt
                    priority: 200
                  - id: coordinator-mode-prompt
                    priority: 300
            """;
        var headlessYaml = """
            name: headless
            inherits: base
            excludes:
              - 社交-command
              - coordinator-mode-prompt
            """;
        var ciYaml = """
            name: ci
            inherits: headless
            excludes:
              - web
              - audit-log-mw
              - crash-snapshot-mw
              - process-usage-mw
            """;
        var sdkYaml = """
            name: sdk
            inherits: headless
            excludes:
              - web
              - crash-snapshot-mw
              - audit-log-mw
            """;
        return new Dictionary<string, NodeProfile> {
            ["base"] = NodeProfileLoader.ParseYaml("base", baseYaml),
            ["headless"] = NodeProfileLoader.ParseYaml("headless", headlessYaml),
            ["ci"] = NodeProfileLoader.ParseYaml("ci", ciYaml),
            ["sdk"] = NodeProfileLoader.ParseYaml("sdk", sdkYaml)
        }.ToFrozenDictionary();
    }
}
