namespace Guard.Hooks.Tests.Permission.Hooks.ToolPermission;

/// <summary>
/// InteractiveHandler 确定性测试 — ExtractCommand 命令提取 + CreatePermissionResult 权限结果构造纯函数。
/// 不依赖 PermissionContext/队列/桥接回调,仅测纯计算分支。
/// </summary>
public sealed class InteractiveHandlerTests {
    // === ExtractCommand: 从 input 字典提取 command 键 ===

    /// <summary>command 键存在且为 string 时返回该值。</summary>
    [Fact]
    public void ExtractCommand_ValidStringCommand_ReturnsValue() {
        var input = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement("ls -la")
        };

        InteractiveHandler.ExtractCommand(input).Should().Be("ls -la");
    }

    /// <summary>command 键不存在时返回 null。</summary>
    [Fact]
    public void ExtractCommand_NoCommandKey_ReturnsNull() {
        var input = new Dictionary<string, JsonElement> {
            ["other"] = JsonSerializer.SerializeToElement("value")
        };

        InteractiveHandler.ExtractCommand(input).Should().BeNull();
    }

    /// <summary>空字典返回 null。</summary>
    [Fact]
    public void ExtractCommand_EmptyDictionary_ReturnsNull() {
        var input = new Dictionary<string, JsonElement>();

        InteractiveHandler.ExtractCommand(input).Should().BeNull();
    }

    /// <summary>command 键存在但为数字类型时返回 null。</summary>
    [Fact]
    public void ExtractCommand_NonStringValue_ReturnsNull() {
        var input = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement(42)
        };

        InteractiveHandler.ExtractCommand(input).Should().BeNull();
    }

    /// <summary>command 键存在但为布尔类型时返回 null。</summary>
    [Fact]
    public void ExtractCommand_BooleanValue_ReturnsNull() {
        var input = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement(true)
        };

        InteractiveHandler.ExtractCommand(input).Should().BeNull();
    }

    /// <summary>command 键存在但为对象类型时返回 null。</summary>
    [Fact]
    public void ExtractCommand_ObjectValue_ReturnsNull() {
        var input = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement(new { x = 1 })
        };

        InteractiveHandler.ExtractCommand(input).Should().BeNull();
    }

    /// <summary>command 为空字符串时返回空字符串(非 null)。</summary>
    [Fact]
    public void ExtractCommand_EmptyString_ReturnsEmptyString() {
        var input = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.SerializeToElement("")
        };

        InteractiveHandler.ExtractCommand(input).Should().Be("");
    }

    // === CreatePermissionResult: PermissionAskDecision → PermissionResult ===

    /// <summary>PermissionAskDecision(Behavior=Ask) 带 Message 时返回 PendingConfirmation(message)。</summary>
    [Fact]
    public void CreatePermissionResult_AskDecisionWithMessage_ReturnsPendingConfirmation() {
        var decision = new PermissionAskDecision { Message = "需要确认" };

        var result = InteractiveHandler.CreatePermissionResult(decision);

        result.Type.Should().Be(PermissionResultType.Pending);
        result.Message.Should().Be("需要确认");
    }

    /// <summary>PermissionAskDecision(Behavior=Ask) 无 Message 时返回 PendingConfirmation("需要用户确认")。</summary>
    [Fact]
    public void CreatePermissionResult_AskDecisionWithoutMessage_ReturnsDefaultMessage() {
        var decision = new PermissionAskDecision();

        var result = InteractiveHandler.CreatePermissionResult(decision);

        result.Type.Should().Be(PermissionResultType.Pending);
        result.Message.Should().Be("需要用户确认");
    }

    /// <summary>PermissionAskDecision 带 null Message 时返回 PendingConfirmation("需要用户确认")。</summary>
    [Fact]
    public void CreatePermissionResult_AskDecisionNullMessage_ReturnsDefaultMessage() {
        var decision = new PermissionAskDecision { Message = null };

        var result = InteractiveHandler.CreatePermissionResult(decision);

        result.Type.Should().Be(PermissionResultType.Pending);
        result.Message.Should().Be("需要用户确认");
    }

    /// <summary>PermissionAskDecision 带空字符串 Message 时返回 PendingConfirmation("")。</summary>
    [Fact]
    public void CreatePermissionResult_AskDecisionEmptyMessage_ReturnsEmptyMessage() {
        var decision = new PermissionAskDecision { Message = "" };

        var result = InteractiveHandler.CreatePermissionResult(decision);

        result.Type.Should().Be(PermissionResultType.Pending);
        result.Message.Should().Be("");
    }
}
