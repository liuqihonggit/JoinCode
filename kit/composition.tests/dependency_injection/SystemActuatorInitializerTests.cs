namespace Core.Tests.DependencyInjection;

/// <summary>
/// SystemActuatorInitializer 确定性测试 — BuildActuatorFactories 纯字典构建。
/// </summary>
public sealed class SystemActuatorInitializerTests {
    [Fact]
    public void BuildActuatorFactories_ReturnsFourKinds() {
        var factories = SystemActuatorInitializer.BuildActuatorFactories();
        factories.Should().HaveCount(4);
    }

    [Fact]
    public void BuildActuatorFactories_ContainsAllExpectedKinds() {
        var factories = SystemActuatorInitializer.BuildActuatorFactories();
        factories.Keys.Should().Contain(SystemActuatorKind.Bash);
        factories.Keys.Should().Contain(SystemActuatorKind.PowerShell);
        factories.Keys.Should().Contain(SystemActuatorKind.Cmd);
        factories.Keys.Should().Contain(SystemActuatorKind.Python);
    }

    [Fact]
    public void BuildActuatorFactories_AllValuesAreNonNull() {
        var factories = SystemActuatorInitializer.BuildActuatorFactories();
        foreach (var kvp in factories) {
            kvp.Value.Should().NotBeNull($"kind {kvp.Key.Id} 的工厂函数不应为 null");
        }
    }

    [Fact]
    public void BuildActuatorFactories_EachKeyHasDistinctFactory() {
        var factories = SystemActuatorInitializer.BuildActuatorFactories();
        var distinctFactories = factories.Values.Distinct().ToList();
        distinctFactories.Should().HaveCount(4);
    }

    // Initialize 跳过：依赖 4 个 CreateCapability（IO 检测 shell 路径）+ 全局静态状态
    // （_initialized + SystemActuatorRegistry._factories），并行不安全，非确定性测试范畴。
    // 如需测试 Initialize，应 mock IFileSystem + 用 Reset() 清理全局状态，但 CreateCapability
    // 的 shell 路径检测依赖环境，测试脆弱，故跳过。
}
