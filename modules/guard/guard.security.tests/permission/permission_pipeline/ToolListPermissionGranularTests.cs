namespace JoinCode.Security.Tests.Permission.Pipeline;

/// <summary>
/// TASK070 P2/DSG046 — ToolListPermissionMiddleware 规则粒度测试：
/// 工具级规则整工具决策（既有语义）；命令前缀规则（"Bash(npm run:*)"）参数匹配才决策、
/// 未命中继续判定链落确认（消除"前缀规则退化为整工具放行"的实证缺陷）；deny 优先于 allow。
/// </summary>
public sealed class ToolListPermissionGranularTests {
    private static readonly IMiddleware<PermissionCheckContext> Middleware = new ToolListPermissionMiddleware();

    private static PermissionCheckContext CreateContext(
        string[] approved, string[] rejected,
        Structura.Collections.ImmutableHamT<string, ToolPermissionRule> approvedRules,
        Structura.Collections.ImmutableHamT<string, ToolPermissionRule> rejectedRules,
        Dictionary<string, JsonElement>? arguments = null) {
        var config = new PermissionConfig {
            AutoApprovedTools = approvedRules,
            AutoRejectedTools = rejectedRules,
        };
        return new PermissionCheckContext {
            ToolName = "Bash",
            Arguments = arguments ?? [],
            CurrentMode = PermissionMode.Auto,
            Config = config,
            AutoApprovedTools = [.. approved],
            AutoRejectedTools = [.. rejected],
        };
    }

    private static Structura.Collections.ImmutableHamT<string, ToolPermissionRule> Rules(params (string Tool, string? Content)[] entries) {
        var builder = Structura.Collections.ImmutableHamT<string, ToolPermissionRule>.Empty
            .WithComparers(StringComparer.OrdinalIgnoreCase).ToBuilder();
        foreach (var (tool, content) in entries) {
            builder[tool] = new ToolPermissionRule { ToolName = tool, RuleContent = content };
        }
        return builder.ToImmutable();
    }

    /// <summary>工具级规则（无 RuleContent）— 整工具放行（既有语义）。</summary>
    [Fact]
    public async Task Invoke_ToolLevelAllowRule_ApprovesWholeTool() {
        var context = CreateContext(["Bash"], [], Rules(("Bash", null)), Structura.Collections.ImmutableHamT<string, ToolPermissionRule>.Empty);

        await Middleware.InvokeAsync(context, (_, _) => throw new InvalidOperationException("不应继续判定链"), CancellationToken.None);

        Assert.True(context.Result?.IsApproved);
    }

    /// <summary>前缀规则命中 — 命令以 prefix 开头 → 放行。</summary>
    [Fact]
    public async Task Invoke_PrefixRule_MatchingCommand_Approves() {
        var arguments = new Dictionary<string, JsonElement> {
            ["command"] = JsonDocument.Parse("\"npm run build --verbose\"").RootElement.Clone(),
        };
        var context = CreateContext(["Bash"], [], Rules(("Bash", "npm run:*")), Structura.Collections.ImmutableHamT<string, ToolPermissionRule>.Empty, arguments);

        await Middleware.InvokeAsync(context, (_, _) => throw new InvalidOperationException("不应继续判定链"), CancellationToken.None);

        Assert.True(context.Result?.IsApproved);
    }

    /// <summary>前缀规则未命中 — 继续判定链（落确认），不整工具放行（P2 实证缺陷消除）。</summary>
    [Fact]
    public async Task Invoke_PrefixRule_NonMatchingCommand_ContinuesChain() {
        var arguments = new Dictionary<string, JsonElement> {
            ["command"] = JsonDocument.Parse("\"rm -rf /\"").RootElement.Clone(),
        };
        var context = CreateContext(["Bash"], [], Rules(("Bash", "npm run:*")), Structura.Collections.ImmutableHamT<string, ToolPermissionRule>.Empty, arguments);
        var nextCalled = false;

        await Middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    /// <summary>前缀规则无 command 参数 — 继续判定链（不匹配即不决策）。</summary>
    [Fact]
    public async Task Invoke_PrefixRule_NoCommandArgument_ContinuesChain() {
        var context = CreateContext(["Bash"], [], Rules(("Bash", "npm run:*")), Structura.Collections.ImmutableHamT<string, ToolPermissionRule>.Empty);
        var nextCalled = false;

        await Middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }

    /// <summary>deny 优先于 allow — 同工具双规则时拒绝先决。</summary>
    [Fact]
    public async Task Invoke_DenyRule_TakesPrecedenceOverAllow() {
        var context = CreateContext(["Bash"], ["Bash"], Rules(("Bash", null)), Rules(("Bash", null)));

        await Middleware.InvokeAsync(context, (_, _) => throw new InvalidOperationException("不应继续判定链"), CancellationToken.None);

        Assert.False(context.Result?.IsApproved);
        Assert.False(context.Result?.ConfirmationRequired);
    }

    /// <summary>不在任何列表 — 继续判定链。</summary>
    [Fact]
    public async Task Invoke_NotListed_ContinuesChain() {
        var context = CreateContext([], [], Structura.Collections.ImmutableHamT<string, ToolPermissionRule>.Empty, Structura.Collections.ImmutableHamT<string, ToolPermissionRule>.Empty);
        var nextCalled = false;

        await Middleware.InvokeAsync(context, (_, _) => {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(nextCalled);
    }
}
