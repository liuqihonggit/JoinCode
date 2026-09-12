namespace Hands.Tests.ToolHandlers;

/// <summary>
/// AgentValidationMiddleware 单元测试 — 验证 Agent 参数验证中间件的结构化诊断
/// </summary>
public class AgentValidationMiddlewareTests
{
    [Fact]
    public async Task EmptyDescription_SetsErrorWithDiagnostic()
    {
        var sut = new AgentValidationMiddleware();
        var context = new AgentToolContext { Description = "", Prompt = "do something" };

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        context.Result.Diagnostic.Should().NotBeNull();
        context.Result.Diagnostic!.Reason.Should().Be("参数验证失败");
        context.Result.Diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "description");
    }

    [Fact]
    public async Task EmptyPrompt_SetsErrorWithDiagnostic()
    {
        var sut = new AgentValidationMiddleware();
        var context = new AgentToolContext { Description = "test agent", Prompt = "" };

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        context.Result.Diagnostic.Should().NotBeNull();
        context.Result.Diagnostic!.Reason.Should().Be("参数验证失败");
        context.Result.Diagnostic.Details.Should().Contain(d => d.Key == "field" && d.Value == "prompt");
    }

    [Fact]
    public async Task ValidInputs_PassesToNext()
    {
        var sut = new AgentValidationMiddleware();
        var context = new AgentToolContext { Description = "test agent", Prompt = "do something" };

        var nextCalled = false;
        await sut.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public void BuildEmptyDescriptionDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = AgentValidationMiddleware.BuildEmptyDescriptionDiagnostic();

        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.FormattedMessage.Should().Be("description cannot be empty");
        diagnostic.Details.Should().ContainSingle(d => d.Key == "field" && d.Value == "description");
        diagnostic.Suggestions.Should().ContainSingle();
    }

    [Fact]
    public void BuildEmptyPromptDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = AgentValidationMiddleware.BuildEmptyPromptDiagnostic();

        diagnostic.Reason.Should().Be("参数验证失败");
        diagnostic.FormattedMessage.Should().Be("prompt cannot be empty");
        diagnostic.Details.Should().ContainSingle(d => d.Key == "field" && d.Value == "prompt");
        diagnostic.Suggestions.Should().ContainSingle();
    }

    [Fact]
    public async Task SubagentType_WithComma_ParsesPrimaryTypeAndAllowedTypes()
    {
        var sut = new AgentValidationMiddleware();
        var context = new AgentToolContext { Description = "test agent", Prompt = "do something", SubagentType = "worker,researcher" };

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.ResolvedPrimaryType.Should().Be("worker");
        context.AllowedAgentTypes.Should().BeEquivalentTo(new[] { "worker", "researcher" });
    }

    [Fact]
    public async Task SubagentType_SingleType_DoesNotParseAllowedTypes()
    {
        var sut = new AgentValidationMiddleware();
        var context = new AgentToolContext { Description = "test agent", Prompt = "do something", SubagentType = "worker" };

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.ResolvedPrimaryType.Should().BeNull();
        context.AllowedAgentTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task SubagentType_Null_DoesNotParse()
    {
        var sut = new AgentValidationMiddleware();
        var context = new AgentToolContext { Description = "test agent", Prompt = "do something" };

        await sut.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.ResolvedPrimaryType.Should().BeNull();
        context.AllowedAgentTypes.Should().BeEmpty();
    }
}
