namespace McpToolDispatch.Tests.Execution;

/// <summary>
/// ToolPatrolEngine 单元测试 — 验证跳过清单（HasRealSideEffects）+ 最小参数构造（BuildMinimalArguments）+ 助记符。
/// </summary>
public sealed class ToolPatrolEngineTest {
    private static ToolInfo MakeTool(string name, string? category = null) => new() { Name = name, Category = category };

    // === HasRealSideEffects — 跳过清单 ===

    [Theory]
    [InlineData("gh_issue_create")]
    [InlineData("gh_pr_view")]
    [InlineData("gh_run_cancel")]
    [InlineData("gh_repo_fork")]
    [InlineData("gh_codespace_create")]
    public void HasRealSideEffects_GhTool_ReturnsTrue(string toolName) {
        ToolPatrolEngine.HasRealSideEffects(MakeTool(toolName)).Should().BeTrue($"gh_* 工具 {toolName} 有 GitHub 写入副作用，应跳过");
    }

    [Theory]
    [InlineData("git_status")]
    [InlineData("git_add")]
    [InlineData("git_commit")]
    [InlineData("git_push")]
    [InlineData("git_clone")]
    public void HasRealSideEffects_GitTool_ReturnsTrue(string toolName) {
        ToolPatrolEngine.HasRealSideEffects(MakeTool(toolName)).Should().BeTrue($"git_* 工具 {toolName} 有本地仓库副作用，应跳过");
    }

    [Fact]
    public void HasRealSideEffects_DesktopCategory_ReturnsTrue() {
        ToolPatrolEngine.HasRealSideEffects(MakeTool("send_input", "desktop")).Should().BeTrue("desktop 类别有 Win32 SendInput 副作用");
    }

    [Fact]
    public void HasRealSideEffects_McpConnect_ReturnsTrue() {
        ToolPatrolEngine.HasRealSideEffects(MakeTool("mcp_connect")).Should().BeTrue("mcp_connect 连真实 MCP 服务器，应跳过");
    }

    [Theory]
    [InlineData("file_read")]
    [InlineData("file_edit")]
    [InlineData("search_code")]
    [InlineData("sleep")]
    public void HasRealSideEffects_NormalTool_ReturnsFalse(string toolName) {
        ToolPatrolEngine.HasRealSideEffects(MakeTool(toolName)).Should().BeFalse($"普通工具 {toolName} 无副作用，应巡检");
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("agent_list")]
    [InlineData("agent_send_message")]
    [InlineData("agent_stop")]
    [InlineData("forward_user_input")]
    public void HasRealSideEffects_AgentTool_ReturnsTrue(string toolName) {
        ToolPatrolEngine.HasRealSideEffects(MakeTool(toolName)).Should().BeTrue($"agent 工具 {toolName} 会真实调 LLM 浪费 token，应跳过");
    }

    // === BuildMinimalArguments — 最小参数构造 ===

    [Fact]
    public void BuildMinimalArguments_NoRequired_ReturnsEmpty() {
        var tool = new ToolInfo { Name = "test", InputSchema = new ToolSchema() };
        ToolPatrolEngine.BuildMinimalArguments(tool).Should().BeEmpty("无必填参数应返回空字典");
    }

    [Fact]
    public void BuildMinimalArguments_FilePath_FillsTestTxt() {
        var tool = new ToolInfo {
            Name = "file_read",
            InputSchema = new ToolSchema {
                Required = ["file_path"],
                Properties = new() { ["file_path"] = new ToolSchemaProperty { Type = "string" } },
            },
        };
        var args = ToolPatrolEngine.BuildMinimalArguments(tool);
        args.Should().ContainKey("file_path");
        args["file_path"].GetString().Should().Be("test.txt");
    }

    [Fact]
    public void BuildMinimalArguments_IntegerParam_FillsOne() {
        var tool = new ToolInfo {
            Name = "sleep",
            InputSchema = new ToolSchema {
                Required = ["duration_seconds"],
                Properties = new() { ["duration_seconds"] = new ToolSchemaProperty { Type = "integer" } },
            },
        };
        var args = ToolPatrolEngine.BuildMinimalArguments(tool);
        args["duration_seconds"].GetInt32().Should().Be(1);
    }

    [Fact]
    public void BuildMinimalArguments_BooleanParam_FillsFalse() {
        var tool = new ToolInfo {
            Name = "test",
            InputSchema = new ToolSchema {
                Required = ["flag"],
                Properties = new() { ["flag"] = new ToolSchemaProperty { Type = "boolean" } },
            },
        };
        var args = ToolPatrolEngine.BuildMinimalArguments(tool);
        args["flag"].GetBoolean().Should().BeFalse();
    }

    // === 助记符 ===

    [Theory]
    [InlineData(ToolPatrolStatus.Ok, "[OK]")]
    [InlineData(ToolPatrolStatus.Error, "[ERR]")]
    [InlineData(ToolPatrolStatus.Crash, "[CRASH]")]
    [InlineData(ToolPatrolStatus.Deadlock, "[LOCK]")]
    [InlineData(ToolPatrolStatus.Timeout, "[TMO]")]
    [InlineData(ToolPatrolStatus.Skipped, "[SKIP]")]
    public void Mnemonic_AllStatuses_CorrectPrefix(ToolPatrolStatus status, string expected) {
        var result = new ToolPatrolResult("test", status, null, TimeSpan.Zero);
        result.Mnemonic.Should().Be(expected);
    }

    [Fact]
    public void ToolPatrolReport_IsHealthy_TrueWhenNoCrashDeadlockTimeout() {
        var report = new ToolPatrolReport(DateTimeOffset.UtcNow, 3, [
            new("a", ToolPatrolStatus.Ok, null, TimeSpan.Zero),
            new("b", ToolPatrolStatus.Error, "err", TimeSpan.Zero),
            new("c", ToolPatrolStatus.Skipped, null, TimeSpan.Zero),
        ]);
        report.IsHealthy.Should().BeTrue("错误和跳过允许，崩溃/死锁/超时为 0 即健康");
    }

    [Fact]
    public void ToolPatrolReport_IsHealthy_FalseWhenCrashPresent() {
        var report = new ToolPatrolReport(DateTimeOffset.UtcNow, 1, [
            new("a", ToolPatrolStatus.Crash, "boom", TimeSpan.Zero),
        ]);
        report.IsHealthy.Should().BeFalse("有崩溃即不健康");
    }
}
