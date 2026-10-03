namespace McpToolRegistry.Tests;

/// <summary>
/// GitCommitConstraintHidingMiddleware 单元测试 — 验证 git_commit 约束回显拦截逻辑。
/// <para>
/// IsConstraintEchoCommit 为 internal static 纯函数，覆盖工具名/参数/消息各分支；
/// InvokeAsync 验证约束回显拒绝（Deny）与正常放行（调 next）行为。
/// InvokeAsync 内部使用全局 CooldownService，断言不依赖具体冷却分支文本，
/// 只验证 Deny 结果（Result 非空 + IsError + Denied）以避免 flaky。
/// </para>
/// </summary>
public class GitCommitConstraintHidingMiddlewareTest {
    #region IsConstraintEchoCommit 纯函数

    [Fact]
    public void IsConstraintEchoCommit_GitCommitWithConstraintEcho_ReturnsTrue() {
        var context = new ToolExecutionContext {
            ToolName = "git_commit",
            Arguments = new Dictionary<string, JsonElement> {
                ["message"] = JsonSerializer.SerializeToElement("feat: xxx (不含分支名)")
            }
        };

        GitCommitConstraintHidingMiddleware.IsConstraintEchoCommit(context).Should().BeTrue();
    }

    [Fact]
    public void IsConstraintEchoCommit_GitCommitWithNormalMessage_ReturnsFalse() {
        var context = new ToolExecutionContext {
            ToolName = "git_commit",
            Arguments = new Dictionary<string, JsonElement> {
                ["message"] = JsonSerializer.SerializeToElement("feat: 添加新功能")
            }
        };

        GitCommitConstraintHidingMiddleware.IsConstraintEchoCommit(context).Should().BeFalse();
    }

    [Fact]
    public void IsConstraintEchoCommit_NonGitCommitTool_ReturnsFalse() {
        var context = new ToolExecutionContext {
            ToolName = "bash",
            Arguments = new Dictionary<string, JsonElement> {
                ["message"] = JsonSerializer.SerializeToElement("feat: xxx (不含分支名)")
            }
        };

        GitCommitConstraintHidingMiddleware.IsConstraintEchoCommit(context).Should().BeFalse();
    }

    [Fact]
    public void IsConstraintEchoCommit_MessageParamMissing_ReturnsFalse() {
        var context = new ToolExecutionContext {
            ToolName = "git_commit",
            Arguments = new Dictionary<string, JsonElement> {
                ["other"] = JsonSerializer.SerializeToElement("value")
            }
        };

        GitCommitConstraintHidingMiddleware.IsConstraintEchoCommit(context).Should().BeFalse();
    }

    [Fact]
    public void IsConstraintEchoCommit_MessageNotString_ReturnsFalse() {
        var context = new ToolExecutionContext {
            ToolName = "git_commit",
            Arguments = new Dictionary<string, JsonElement> {
                ["message"] = JsonSerializer.SerializeToElement(42)
            }
        };

        GitCommitConstraintHidingMiddleware.IsConstraintEchoCommit(context).Should().BeFalse();
    }

    [Fact]
    public void IsConstraintEchoCommit_EmptyArguments_ReturnsFalse() {
        var context = new ToolExecutionContext {
            ToolName = "git_commit",
            Arguments = []
        };

        GitCommitConstraintHidingMiddleware.IsConstraintEchoCommit(context).Should().BeFalse();
    }

    #endregion

    #region InvokeAsync 行为

    [Fact]
    public async Task InvokeAsync_ConstraintEcho_DeniesExecution() {
        await using var middleware = new GitCommitConstraintHidingMiddleware();
        var context = new ToolExecutionContext {
            ToolName = "git_commit",
            Arguments = new Dictionary<string, JsonElement> {
                ["message"] = JsonSerializer.SerializeToElement("feat: xxx (不含分支名)")
            }
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse("约束回显应短路管道，不调 next");
        context.Result.Should().NotBeNull("应设置拒绝结果");
        context.Result!.IsError.Should().BeTrue();
        context.PermissionDecision.Should().Be(PermissionDecision.Denied);
    }

    [Fact]
    public async Task InvokeAsync_NormalMessage_CallsNext() {
        await using var middleware = new GitCommitConstraintHidingMiddleware();
        var context = new ToolExecutionContext {
            ToolName = "git_commit",
            Arguments = new Dictionary<string, JsonElement> {
                ["message"] = JsonSerializer.SerializeToElement("feat: 添加新功能")
            }
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("正常消息应放行调 next");
        context.Result.Should().BeNull("正常消息不应设置结果");
    }

    [Fact]
    public async Task InvokeAsync_NonGitCommit_CallsNext() {
        await using var middleware = new GitCommitConstraintHidingMiddleware();
        var context = new ToolExecutionContext {
            ToolName = "bash",
            Arguments = new Dictionary<string, JsonElement> {
                ["command"] = JsonSerializer.SerializeToElement("echo hello")
            }
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("非 git_commit 工具应放行");
    }

    [Fact]
    public async Task InvokeAsync_MessageMissing_CallsNext() {
        await using var middleware = new GitCommitConstraintHidingMiddleware();
        var context = new ToolExecutionContext {
            ToolName = "git_commit",
            Arguments = []
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue("message 参数缺失不构成约束回显，应放行");
    }

    [Fact]
    public async Task InvokeAsync_ConstraintEcho_WithinCooldown_DeniesWithShortReason() {
        // 预先激活冷却，使 ShouldTrigger 返回 false → 进入 else 分支（短提示）
        CooldownService.RecordTrigger("commit-constraint-echo");

        await using var middleware = new GitCommitConstraintHidingMiddleware();
        var context = new ToolExecutionContext {
            ToolName = "git_commit",
            Arguments = new Dictionary<string, JsonElement> {
                ["message"] = JsonSerializer.SerializeToElement("feat: xxx (不含分支名)")
            }
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        context.PermissionDecision.Should().Be(PermissionDecision.Denied);
        var text = context.Result.GetFirstText();
        text.Should().Contain("已提示过", "冷却内应使用短提示文本");
    }

    [Fact]
    public async Task InvokeAsync_ConstraintEcho_FirstTrigger_DeniesWithFullReason() {
        // 反射清空冷却状态，确保首次触发 → if 分支（完整拒绝原因）
        ResetCooldownState();

        await using var middleware = new GitCommitConstraintHidingMiddleware();
        var context = new ToolExecutionContext {
            ToolName = "git_commit",
            Arguments = new Dictionary<string, JsonElement> {
                ["message"] = JsonSerializer.SerializeToElement("feat: xxx (不含分支名)")
            }
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        context.PermissionDecision.Should().Be(PermissionDecision.Denied);
        var text = context.Result.GetFirstText();
        text.Should().Contain("错误示例", "首次触发应使用完整拒绝原因");
    }

    #endregion

    #region OnError 行为

    [Fact]
    public async Task OnError_ReturnsContinue() {
        await using var middleware = new GitCommitConstraintHidingMiddleware();

        middleware.OnError.Should().Be(ErrorBehavior.Continue,
            "注入失败不中断管道");
    }

    #endregion

    /// <summary>
    /// 反射清空 CooldownService 全局状态，用于测试隔离（确保首次触发分支可覆盖）。
    /// </summary>
    private static void ResetCooldownState() {
        var field = typeof(CooldownService).GetField("LastTrigger",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (field?.GetValue(null) is System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> dict) {
            dict.Clear();
        }
    }
}
