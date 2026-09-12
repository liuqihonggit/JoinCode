namespace McpToolRegistry.Tests;

public class PermissionCheckMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoInterceptor_CallsNext()
    {
        var logger = NullLogger<PermissionCheckMiddleware>.Instance;
        var middleware = new PermissionCheckMiddleware(null, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = []
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_InterceptorPasses_CallsNext()
    {
        var logger = NullLogger<PermissionCheckMiddleware>.Instance;
        var interceptor = new Mock<IPermissionCheckingInterceptor>();
        interceptor.Setup(i => i.CheckPermissionAsync(It.IsAny<ToolInvokeContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionCheckOutcome.Allowed);

        var middleware = new PermissionCheckMiddleware(interceptor.Object, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "bash",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["command"] = JsonSerializer.SerializeToElement("echo hello")
            }
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        interceptor.Verify(i => i.CheckPermissionAsync(It.IsAny<ToolInvokeContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_InterceptorDenies_SetsDeniedResultAndShortCircuits()
    {
        var logger = NullLogger<PermissionCheckMiddleware>.Instance;
        var interceptor = new Mock<IPermissionCheckingInterceptor>();
        interceptor.Setup(i => i.CheckPermissionAsync(It.IsAny<ToolInvokeContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionCheckOutcome.Denied("denied"));

        var middleware = new PermissionCheckMiddleware(interceptor.Object, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "bash",
            Arguments = []
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        context.PermissionDecision.Should().Be(PermissionDecision.Denied);
        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
    }
}

public class AgentRestrictionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoRestrictions_CallsNext()
    {
        var logger = NullLogger<AgentRestrictionMiddleware>.Instance;
        var middleware = new AgentRestrictionMiddleware(null, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = []
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ToolAllowed_CallsNext()
    {
        var logger = NullLogger<AgentRestrictionMiddleware>.Instance;
        var restrictions = new Mock<IAgentToolRestrictions>();
        restrictions.Setup(r => r.IsToolAllowedForMode("bash", PermissionMode.Auto)).Returns(true);

        var middleware = new AgentRestrictionMiddleware(restrictions.Object, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "bash",
            Arguments = [],
            AgentMode = PermissionMode.Auto
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ToolNotAllowed_SetsDeniedResultAndShortCircuits()
    {
        var logger = NullLogger<AgentRestrictionMiddleware>.Instance;
        var restrictions = new Mock<IAgentToolRestrictions>();
        restrictions.Setup(r => r.IsToolAllowedForMode("dangerous", PermissionMode.Auto)).Returns(false);

        var middleware = new AgentRestrictionMiddleware(restrictions.Object, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "dangerous",
            Arguments = [],
            AgentMode = PermissionMode.Auto
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        context.PermissionDecision.Should().Be(PermissionDecision.Denied);
        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
    }
}

public class RemotePolicyMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoService_CallsNext()
    {
        var logger = NullLogger<RemotePolicyMiddleware>.Instance;
        var middleware = new RemotePolicyMiddleware(null, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = []
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_PolicyAllows_CallsNext()
    {
        var logger = NullLogger<RemotePolicyMiddleware>.Instance;
        var policyService = new Mock<IRemotePolicyService>();
        policyService.Setup(s => s.EvaluateAsync("bash", It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyEvaluationResult { Allowed = true, RuleId = "rule1", Action = PolicyAction.Allow, Reason = "" });

        var middleware = new RemotePolicyMiddleware(policyService.Object, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "bash",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["command"] = JsonSerializer.SerializeToElement("echo hello")
            }
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_PolicyDenies_SetsDeniedResultAndShortCircuits()
    {
        var logger = NullLogger<RemotePolicyMiddleware>.Instance;
        var policyService = new Mock<IRemotePolicyService>();
        policyService.Setup(s => s.EvaluateAsync("dangerous", It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyEvaluationResult { Allowed = false, RuleId = "block", Action = PolicyAction.Deny, Reason = "blocked" });

        var middleware = new RemotePolicyMiddleware(policyService.Object, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "dangerous",
            Arguments = []
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        context.PermissionDecision.Should().Be(PermissionDecision.Denied);
        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
    }
}

public class FeatureFlagMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoService_CallsNext()
    {
        var logger = NullLogger<FeatureFlagMiddleware>.Instance;
        var middleware = new FeatureFlagMiddleware(null, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = []
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_FeatureEnabled_CallsNext()
    {
        var logger = NullLogger<FeatureFlagMiddleware>.Instance;
        var featureService = new Mock<IFeatureFlagService>();
        featureService.Setup(f => f.IsEnabledAsync("tool.bash.enabled", It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var middleware = new FeatureFlagMiddleware(featureService.Object, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "bash",
            Arguments = []
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_FeatureDisabled_SetsDeniedResultAndShortCircuits()
    {
        var logger = NullLogger<FeatureFlagMiddleware>.Instance;
        var featureService = new Mock<IFeatureFlagService>();
        featureService.Setup(f => f.IsEnabledAsync("tool.bash.enabled", It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var middleware = new FeatureFlagMiddleware(featureService.Object, logger);
        var context = new ToolExecutionContext
        {
            ToolName = "bash",
            Arguments = []
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) => { nextCalled = true; return Task.CompletedTask; }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        context.PermissionDecision.Should().Be(PermissionDecision.Denied);
        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
    }
}
