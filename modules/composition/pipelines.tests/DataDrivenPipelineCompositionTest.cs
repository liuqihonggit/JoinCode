namespace Pipelines.Tests;

/// <summary>
/// DataDrivenPipelineComposition 单元测试 — 验证数据驱动管道组装的核心行为
/// 重点测试 BuildNodeFactories 和 NodeServiceResolver 集成
/// </summary>
public sealed class DataDrivenPipelineCompositionTest {
    [Fact]
    public void BuildNodeFactories_ContainsChatPipelineStreamCrashSnapshot() {
        var factories = DataDrivenPipelineComposition.BuildNodeFactories().ToDictionary();

        factories.Should().ContainKey("chat-pipeline:stream-crash-snapshot-mw");
    }

    [Fact]
    public void BuildNodeFactories_ContainsPreprocessPipelineCrashSnapshot() {
        var factories = DataDrivenPipelineComposition.BuildNodeFactories().ToDictionary();

        factories.Should().ContainKey("preprocess-pipeline:crash-snapshot-mw");
    }

    [Fact]
    public void BuildNodeFactories_ContainsAll17ConsumerCrashSnapshots() {
        var factories = DataDrivenPipelineComposition.BuildNodeFactories().ToDictionary();

        var crashKeys = factories.Keys.Where(k => k.Contains("crash-snapshot")).ToList();
        crashKeys.Should().HaveCount(17);
        crashKeys.Should().Contain("chat-pipeline:stream-crash-snapshot-mw");
        crashKeys.Should().Contain("preprocess-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("chatinit-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("chatadmin-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("compact-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("query-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("permission-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("settings-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("unified-spawn-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("agent-dispose-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("fork-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("web-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("shell-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("skill-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("code-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("dream-pipeline:crash-snapshot-mw");
        crashKeys.Should().Contain("tool-execution-pipeline:crash-snapshot-mw");
    }

    [Fact]
    public void BuildNodeFactories_SameNodeIdDifferentConsumer_HasDifferentFactories() {
        var factories = DataDrivenPipelineComposition.BuildNodeFactories().ToDictionary();

        factories.Should().ContainKey("preprocess-pipeline:crash-snapshot-mw");
        factories.Should().ContainKey("chatinit-pipeline:crash-snapshot-mw");
        factories.Should().ContainKey("chatadmin-pipeline:crash-snapshot-mw");
    }

    [Fact]
    public void BuildNodeFactories_AllKeysUse_ColonSeparator() {
        var factories = DataDrivenPipelineComposition.BuildNodeFactories().ToList();

        factories.Should().AllSatisfy(kvp => kvp.Key.Should().Contain(":"));
    }

    [Fact]
    public void NodeServiceResolver_BuildFromFactories_ContainsAllKeys() {
        var factories = DataDrivenPipelineComposition.BuildNodeFactories().ToList();
        var resolver = NodeServiceResolver.Build(factories);

        foreach (var kvp in factories) {
            var parts = kvp.Key.Split(':', 2);
            var consumerId = parts[0];
            var nodeId = parts[1];
            var key = NodeServiceResolver.CombineKey(consumerId, nodeId);
            factories.Should().ContainKey(key, $"key '{key}' should be in factories");
        }
    }

    private sealed class NullServiceProvider : IServiceProvider {
        public static readonly NullServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
