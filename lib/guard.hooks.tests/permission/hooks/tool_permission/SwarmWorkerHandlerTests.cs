namespace Guard.Hooks.Tests.Permission.Hooks.ToolPermission;

/// <summary>
/// SwarmWorkerHandler 确定性测试 — ExtractCommand 命令提取纯函数 + HandleAsync 非 Swarm Worker 分支。
/// 不依赖 Leader 转发/分类器/桥接回调,仅测纯计算分支。
/// </summary>
public sealed class SwarmWorkerHandlerTests {
    // === ExtractCommand: 从 input 字典提取 command 键 ===

    /// <summary>command 键存在且为 string 时返回该值。</summary>
    [Fact]
    public void ExtractCommand_ValidStringCommand_ReturnsValue() {
        var input = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement("git status")
        };

        SwarmWorkerHandler.ExtractCommand(input).Should().Be("git status");
    }

    /// <summary>command 键不存在时返回 null。</summary>
    [Fact]
    public void ExtractCommand_NoCommandKey_ReturnsNull() {
        var input = new Dictionary<string, JsonElement> {
            ["path"] = JsonSerializer.SerializeToElement("/tmp")
        };

        SwarmWorkerHandler.ExtractCommand(input).Should().BeNull();
    }

    /// <summary>空字典返回 null。</summary>
    [Fact]
    public void ExtractCommand_EmptyDictionary_ReturnsNull() {
        var input = new Dictionary<string, JsonElement>();

        SwarmWorkerHandler.ExtractCommand(input).Should().BeNull();
    }

    /// <summary>command 键存在但为数字类型时返回 null。</summary>
    [Fact]
    public void ExtractCommand_NonStringValue_ReturnsNull() {
        var input = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement(123)
        };

        SwarmWorkerHandler.ExtractCommand(input).Should().BeNull();
    }

    /// <summary>command 键存在但为布尔类型时返回 null。</summary>
    [Fact]
    public void ExtractCommand_BooleanValue_ReturnsNull() {
        var input = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement(false)
        };

        SwarmWorkerHandler.ExtractCommand(input).Should().BeNull();
    }

    /// <summary>command 为空字符串时返回空字符串(非 null)。</summary>
    [Fact]
    public void ExtractCommand_EmptyString_ReturnsEmptyString() {
        var input = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement("")
        };

        SwarmWorkerHandler.ExtractCommand(input).Should().Be("");
    }

    // === HandleAsync: 非 Swarm Worker 分支 ===

    /// <summary>非 Swarm Worker 调用时返回 null(回退到本地处理)。</summary>
    [Fact]
    public async Task HandleAsync_NotSwarmWorker_ReturnsNull() {
        await using var handler = new SwarmWorkerHandler();
        var ctx = new PermissionContext(
            "bash",
            new Dictionary<string, JsonElement>(),
            "msg-1",
            "tooluse-1",
            Mock.Of<IPermissionLogger>());
        var @params = new SwarmWorkerPermissionParams {
            Context = ctx,
            Description = "test",
            IsSwarmWorker = false
        };

        var result = await handler.HandleAsync(@params);

        result.Should().BeNull();
    }

    /// <summary>Swarm Worker 但无回调且无分类器时返回 null(回退到本地处理)。</summary>
    [Fact]
    public async Task HandleAsync_SwarmWorkerNoCallbacksNoClassifier_ReturnsNull() {
        await using var handler = new SwarmWorkerHandler();
        var ctx = new PermissionContext(
            "bash",
            new Dictionary<string, JsonElement>(),
            "msg-1",
            "tooluse-1",
            Mock.Of<IPermissionLogger>());
        var @params = new SwarmWorkerPermissionParams {
            Context = ctx,
            Description = "test",
            IsSwarmWorker = true,
            PendingClassifierCheck = null
        };

        var result = await handler.HandleAsync(@params);

        result.Should().BeNull();
    }
}
