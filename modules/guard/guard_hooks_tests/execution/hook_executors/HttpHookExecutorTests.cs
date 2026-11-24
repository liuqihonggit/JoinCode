namespace Guard.Tests.Hooks.Execution.HookExecutors;

#pragma warning disable JCC11003 // 测试代码中 NotBeNull 后访问属性需要 null-forgiving（同 HookOutputParsingTests）

/// <summary>
/// HTTP 型 hook 执行器测试（TASK130-A7）。
/// <para>
/// 这层此前**一条测试都没有**：`HttpHookExecutor` 自带一份 JSON 解析复制品，只读
/// <c>decision/reason/continue</c>，不解析 <c>hookSpecificOutput</c>，于是同样是 hook，
/// 命令型能产出 <c>systemMessage</c>/<c>permissionDecision</c>/<c>additionalContext</c>，
/// HTTP 型却全部静默丢弃。这里既锁「协议字段要落地」，也锁「HTTP 特有的状态码语义不许被合并改掉」。
/// </para>
/// </summary>
public sealed class HttpHookExecutorTests {
    /// <summary>返回固定响应的 HttpClient 工厂。</summary>
    private sealed class StubHttpClientFactory(HttpResponseMessage response) : IHttpClientFactory {
        /// <summary>获取最后一次请求，供断言请求体。</summary>
        public HttpRequestMessage? LastRequest { get; internal set; }

        /// <summary>获取最后一次请求体文本。</summary>
        public string? LastRequestBody { get; internal set; }

        /// <inheritdoc />
        public HttpClient CreateClient(string name) =>
            new(new StubHandler(this, response), disposeHandler: false);
    }

    /// <summary>把固定响应返回给被测执行器的 handler。</summary>
    private sealed class StubHandler(StubHttpClientFactory owner, HttpResponseMessage response) : HttpMessageHandler {
        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            owner.LastRequest = request;
            owner.LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private static async Task<(HookResult Result, StubHttpClientFactory Factory)> RunAsync(
        string body,
        System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) {
        var factory = new StubHttpClientFactory(new HttpResponseMessage(status) {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
        var executor = new HttpHookExecutor(factory, NullLogger<HttpHookExecutor>.Instance);
        var hook = new HttpHook { Url = "http://127.0.0.1:9/hook" };
        var input = new HookInput {
            Event = HookEvent.PreToolUse,
            Payload = new Dictionary<string, JsonElement>()
        };

        var result = await executor.ExecuteTypedAsync(hook, input);
        return (result, factory);
    }

    // ── 修复本体：hookSpecificOutput 必须被解析 ──

    [Fact]
    public async Task ExecuteTypedAsync_HookSpecificOutputAdditionalContext_LandsOnAdditionalContext() {
        var (result, _) = await RunAsync(
            """{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"本仓库禁止直接提交到 main"}}""");

        result.AdditionalContext.Should().Be("本仓库禁止直接提交到 main");
        result.Message.Should().BeNull("上下文不进 Message，避免被当用户可见提示重复展示");
    }

    [Fact]
    public async Task ExecuteTypedAsync_PermissionDecisionDeny_SetsPermissionDeny() {
        var (result, _) = await RunAsync(
            """{"hookSpecificOutput":{"permissionDecision":"deny","permissionDecisionReason":"blocked by remote policy"}}""");

        var deny = result.PermissionRequestResult.Should().BeOfType<PermissionDenyResult>().Subject;
        deny.Message.Should().Be("blocked by remote policy");
    }

    [Fact]
    public async Task ExecuteTypedAsync_SystemMessage_LandsOnSystemMessage() {
        var (result, _) = await RunAsync(
            """{"hookSpecificOutput":{"systemMessage":"远端策略已更新"}}""");

        result.SystemMessage.Should().Be("远端策略已更新");
    }

    [Fact]
    public async Task ExecuteTypedAsync_UpdatedInput_LandsOnUpdatedInput() {
        var (result, _) = await RunAsync(
            """{"hookSpecificOutput":{"updatedInput":{"command":"ls -la"}}}""");

        result.UpdatedInput.Should().NotBeNull();
        result.UpdatedInput!["command"].GetString().Should().Be("ls -la");
    }

    // ── 合并两份实现时的语义对齐（防止回退成「只读 reason」） ──

    [Fact]
    public async Task ExecuteTypedAsync_MessageOnly_FillsMessage() {
        var (result, _) = await RunAsync("""{"message":"远端提示"}""");

        result.Message.Should().Be("远端提示", "message 是 HookDecision 的「面向用户的提示消息」，只读 reason 会静默丢掉");
    }

    [Fact]
    public async Task ExecuteTypedAsync_StopReason_PassedThrough() {
        var (result, _) = await RunAsync("""{"continue":false,"stopReason":"远端要求停止"}""");

        result.PreventContinuation.Should().BeTrue();
        result.StopReason.Should().Be("远端要求停止");
    }

    [Fact]
    public async Task ExecuteTypedAsync_DecisionBlock_SetsBlocking() {
        var (result, _) = await RunAsync("""{"decision":"block","reason":"被远端规则拦截"}""");

        result.Outcome.Should().Be(HookOutcome.Blocking);
        result.Message.Should().Be("被远端规则拦截");
    }

    // ── HTTP 特有语义：状态码与纯文本响应 ──

    [Fact]
    public async Task ExecuteTypedAsync_PlainTextSuccess_KeepsHttpStatusPrefix() {
        var (result, _) = await RunAsync("remote ok");

        result.Outcome.Should().Be(HookOutcome.Success);
        result.Message.Should().StartWith("HTTP OK", "纯文本响应保留状态码前缀，便于排查服务返回了什么");
        result.Message.Should().Contain("remote ok");
    }

    [Fact]
    public async Task ExecuteTypedAsync_Forbidden_Blocks() {
        var (result, _) = await RunAsync("denied by remote", System.Net.HttpStatusCode.Forbidden);

        result.Outcome.Should().Be(HookOutcome.Blocking);
        result.PreventContinuation.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteTypedAsync_ServerError_NonBlockingError() {
        var (result, _) = await RunAsync("boom", System.Net.HttpStatusCode.InternalServerError);

        result.Outcome.Should().Be(HookOutcome.NonBlockingError, "远端 5xx 不许把整个会话卡死");
    }

    [Fact]
    public async Task ExecuteTypedAsync_SendsSnakeCaseHookInputBody() {
        var (_, factory) = await RunAsync("""{"continue":true}""");

        factory.LastRequest.Should().NotBeNull();
        factory.LastRequest!.Method.Should().Be(HttpMethod.Post);
        factory.LastRequestBody.Should().Contain("hook_event_name", "body 用 snake_case 完整 hook input（对齐 execHttpHook.ts）");
        factory.LastRequestBody.Should().Contain("PreToolUse");
    }
}
