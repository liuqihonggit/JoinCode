namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// C6 技能与子代理交互 RPC 测试：skills/list 读取引擎技能服务，
/// subagents/prompt 转发文本并回显 requestId，subagents/interruptByParent 中断子代理；
/// 参数缺失与未知子代理都返回带引导的错误，不静默吞掉。
/// </summary>
public sealed class DshSkillsSubagentsRpcTests {
    private const string SessionId = "s-c6";

    private static DshRpcDispatcher NewDispatcher(out PlaceholderChatSession engine) {
        var services = new DshHostServices(new InMemoryFileSystem());
        engine = new PlaceholderChatSession();
        services.AttachEngine(engine);
        return new DshRpcDispatcher(services);
    }

    private static JsonElement SkillArgs(string sessionId) =>
        JsonSerializer.SerializeToElement(new { request = new { sessionId } });

    private static JsonElement PromptArgs(
        string requestId,
        string childSessionId,
        object[] content,
        string delivery = "queue") =>
        JsonSerializer.SerializeToElement(new {
            request = new {
                requestId,
                parentSessionId = SessionId,
                childSessionId,
                mode = "continuable",
                delivery,
                content,
            },
        });

    [Fact]
    public async Task SkillsList_MapsEngineSkills() {
        var dispatcher = NewDispatcher(out var engine);
        engine.Skills = [new SkillSummary("code-review", "审查代码", ModelInvocable: false, Path: "/skills/code-review.md")];

        var result = await dispatcher.HandleAsync("skills/list", SkillArgs(SessionId));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var skill = result.Value.GetValueOrDefault().GetProperty("skills")[0];
        skill.GetProperty("name").GetString().Should().Be("code-review");
        skill.GetProperty("description").GetString().Should().Be("审查代码");
        skill.GetProperty("modelInvocable").GetBoolean().Should().BeFalse();
        skill.GetProperty("path").GetString().Should().Be("/skills/code-review.md");
        skill.TryGetProperty("whenToUse", out _).Should().BeFalse("null 字段不应出现在线上 JSON");
    }

    [Fact]
    public async Task SkillsList_EmptyEngineSkills_ReturnsEmptyArray() {
        var dispatcher = NewDispatcher(out _);

        var result = await dispatcher.HandleAsync("skills/list", SkillArgs(SessionId));

        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetProperty("skills").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task SkillsList_MissingSessionId_ReturnsGuidance() {
        var dispatcher = NewDispatcher(out _);

        var result = await dispatcher.HandleAsync("skills/list", SkillArgs(""));

        result.Ok.Should().BeFalse();
        var error = result.Error ?? throw new Xunit.Sdk.XunitException("期望 skills/list 失败，但实际成功");
        error.Code.Should().Be("gateway/bad-request");
        error.Message.Should().Contain("request.sessionId").And.Contain("<会话ID>");
    }

    [Fact]
    public async Task SkillsList_WithoutEngine_ReturnsNotReady() {
        var dispatcher = new DshRpcDispatcher(new DshHostServices(new InMemoryFileSystem()));

        var result = await dispatcher.HandleAsync("skills/list", SkillArgs(SessionId));

        result.Ok.Should().BeFalse();
        var error = result.Error ?? throw new Xunit.Sdk.XunitException("期望 skills/list 失败，但实际成功");
        error.Code.Should().Be("session/not-ready");
        error.Message.Should().Contain("startup_timing.log");
    }

    [Fact]
    public async Task SubagentPrompt_ForwardsJoinedTextAndEchoesRequestId() {
        var dispatcher = NewDispatcher(out var engine);
        engine.SubAgentForwardSink = (_, _) => true;

        var result = await dispatcher.HandleAsync("subagents/prompt", PromptArgs("req-1", "child-9", [
            new { type = "text", text = "第一行" },
            new { type = "text", text = "第二行" },
        ]));

        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetProperty("messageId").GetString().Should().Be("req-1");
        engine.LastSubAgentForward.Should().Be(("child-9", "第一行\n第二行"));
    }

    [Fact]
    public async Task SubagentPrompt_ImageBlock_ReturnsGuidance() {
        var dispatcher = NewDispatcher(out var engine);

        var result = await dispatcher.HandleAsync("subagents/prompt", PromptArgs("req-2", "child-9", [
            new { type = "image", mediaType = "image/png", data = "AAA" },
        ]));

        result.Ok.Should().BeFalse();
        var error = result.Error ?? throw new Xunit.Sdk.XunitException("期望 subagents/prompt 失败，但实际成功");
        error.Code.Should().Be("session/rejected");
        error.Message.Should().Contain("image").And.Contain("纯文本").And.Contain("文件路径");
        engine.LastSubAgentForward.Should().BeNull("被拒绝的图片投递不得进入引擎");
    }

    [Fact]
    public async Task SubagentPrompt_UnknownChild_ReturnsNotFoundWithGuidance() {
        var dispatcher = NewDispatcher(out var engine);
        engine.SubAgentForwardSink = (_, _) => false;

        var result = await dispatcher.HandleAsync("subagents/prompt", PromptArgs("req-3", "missing", [
            new { type = "text", text = "hello" },
        ]));

        result.Ok.Should().BeFalse();
        var error = result.Error ?? throw new Xunit.Sdk.XunitException("期望 subagents/prompt 失败，但实际成功");
        error.Code.Should().Be("subagent/not-found");
        error.Message.Should().Contain("missing").And.Contain("刷新子代理列表");
    }

    [Fact]
    public async Task SubagentPrompt_RejectsUnsupportedMode() {
        var dispatcher = NewDispatcher(out _);
        var args = JsonSerializer.SerializeToElement(new {
            request = new {
                requestId = "req-4",
                parentSessionId = SessionId,
                childSessionId = "child-9",
                mode = "one-shot",
                delivery = "queue",
                content = new object[] { new { type = "text", text = "hi" } },
            },
        });

        var result = await dispatcher.HandleAsync("subagents/prompt", args);

        result.Ok.Should().BeFalse();
        var error = result.Error ?? throw new Xunit.Sdk.XunitException("期望 subagents/prompt 失败，但实际成功");
        error.Code.Should().Be("gateway/bad-request");
        error.Message.Should().Contain("mode=continuable");
    }

    [Fact]
    public async Task SubagentInterrupt_Accepted_ReturnsTrue() {
        var dispatcher = NewDispatcher(out var engine);
        engine.SubAgentInterruptSink = _ => true;

        var result = await dispatcher.HandleAsync("subagents/interruptByParent",
            JsonSerializer.SerializeToElement(new { childSessionId = "child-9", parentSessionId = SessionId, mode = "continuable" }));

        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetProperty("accepted").GetBoolean().Should().BeTrue();
        engine.LastInterruptedSubAgent.Should().Be("child-9");
    }

    [Fact]
    public async Task SubagentInterrupt_UnknownChild_ReturnsNotFound() {
        var dispatcher = NewDispatcher(out var engine);
        engine.SubAgentInterruptSink = _ => false;

        var result = await dispatcher.HandleAsync("subagents/interruptByParent",
            JsonSerializer.SerializeToElement(new { childSessionId = "missing", parentSessionId = SessionId }));

        result.Ok.Should().BeFalse();
        var error = result.Error ?? throw new Xunit.Sdk.XunitException("期望 subagents/interruptByParent 失败，但实际成功");
        error.Code.Should().Be("subagent/not-found");
        error.Message.Should().Contain("未发生中断");
    }

    [Fact]
    public async Task SubagentInterrupt_MissingChild_ReturnsGuidance() {
        var dispatcher = NewDispatcher(out _);

        var result = await dispatcher.HandleAsync("subagents/interruptByParent",
            JsonSerializer.SerializeToElement(new { parentSessionId = SessionId }));

        result.Ok.Should().BeFalse();
        var error = result.Error ?? throw new Xunit.Sdk.XunitException("期望 subagents/interruptByParent 失败，但实际成功");
        error.Code.Should().Be("gateway/bad-request");
        error.Message.Should().Contain("childSessionId");
    }
}
