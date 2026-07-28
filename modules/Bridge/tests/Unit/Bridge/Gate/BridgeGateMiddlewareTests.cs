namespace Bridge.Tests.Gate;

using JoinCode.Abstractions.Clock;
using Core.Bridge.Gate;
using Infrastructure.Pipeline;

public sealed class BridgeGateMiddlewareTests
{
    // === BridgeGateEnabledMiddleware ===

    [Fact]
    public async Task Enabled_BridgeDisabled_Fails()
    {
        var mw = new BridgeGateEnabledMiddleware();
        var ctx = CreateContext(bridgeEnabled: false);

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeTrue();
    }

    [Fact]
    public async Task Enabled_BridgeEnabled_Continues()
    {
        var mw = new BridgeGateEnabledMiddleware();
        var ctx = CreateContext(bridgeEnabled: true);

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeFalse();
    }

    // === BridgeGateOAuthMiddleware ===

    [Fact]
    public async Task OAuth_NoToken_Fails()
    {
        var mw = new BridgeGateOAuthMiddleware();
        var ctx = CreateContext(getAccessToken: () => null);

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeTrue();
    }

    [Fact]
    public async Task OAuth_HasToken_Continues()
    {
        var mw = new BridgeGateOAuthMiddleware();
        var ctx = CreateContext(getAccessToken: () => "test-token");

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeFalse();
        ctx.AccessToken.Should().Be("test-token");
    }

    // === BridgeGateDeadTokenBackoffMiddleware ===

    [Fact]
    public async Task DeadToken_SameExpiryAndCount3_Fails()
    {
        var mw = new BridgeGateDeadTokenBackoffMiddleware();
        var deadState = new Mock<IBridgeOAuthDeadTokenState>();
        deadState.Setup(x => x.DeadExpiresAt).Returns(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        deadState.Setup(x => x.DeadFailCount).Returns(3);

        var ctx = CreateContext(options: new BridgeInitOptions
        {
            DeadTokenState = deadState.Object,
            GetOAuthTokenExpiry = () => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        });

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeTrue();
    }

    [Fact]
    public async Task DeadToken_DifferentExpiry_Continues()
    {
        var mw = new BridgeGateDeadTokenBackoffMiddleware();
        var deadState = new Mock<IBridgeOAuthDeadTokenState>();
        deadState.Setup(x => x.DeadExpiresAt).Returns(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        deadState.Setup(x => x.DeadFailCount).Returns(3);

        var ctx = CreateContext(options: new BridgeInitOptions
        {
            DeadTokenState = deadState.Object,
            GetOAuthTokenExpiry = () => new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        });

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeFalse();
    }

    // === BridgeGatePolicyMiddleware ===

    [Fact]
    public async Task Policy_NotAllowed_Fails()
    {
        var mw = new BridgeGatePolicyMiddleware();
        var ctx = CreateContext(options: new BridgeInitOptions { IsPolicyAllowed = _ => false });

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeTrue();
    }

    [Fact]
    public async Task Policy_Allowed_Continues()
    {
        var mw = new BridgeGatePolicyMiddleware();
        var ctx = CreateContext(options: new BridgeInitOptions { IsPolicyAllowed = _ => true });

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeFalse();
    }

    [Fact]
    public async Task Policy_NullPolicy_Continues()
    {
        var mw = new BridgeGatePolicyMiddleware();
        var ctx = CreateContext();

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeFalse();
    }

    // === BridgeGateOrgUUIDMiddleware ===

    [Fact]
    public async Task OrgUUID_NoUUID_Fails()
    {
        var mw = new BridgeGateOrgUUIDMiddleware();
        var ctx = CreateContext(getOrgUUID: () => null);

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeTrue();
    }

    [Fact]
    public async Task OrgUUID_HasUUID_Continues()
    {
        var mw = new BridgeGateOrgUUIDMiddleware();
        var ctx = CreateContext(getOrgUUID: () => "org-123");

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeFalse();
        ctx.OrgUUID.Should().Be("org-123");
    }

    // === Full Gate Pipeline ===

    [Fact]
    public async Task FullGatePipeline_AllPass_Continues()
    {
        var pipeline = new PipelineBuilder<BridgeInitGateContext>()
            .WithShortCircuit(ctx => ctx.Failed)
            .Use(new BridgeGateEnabledMiddleware())
            .Use(new BridgeGateOAuthMiddleware())
            .Use(new BridgeGateDeadTokenBackoffMiddleware())
            .Use(new BridgeGatePolicyMiddleware())
            .Use(new BridgeGateOrgUUIDMiddleware())
            .Build();

        var ctx = CreateContext(bridgeEnabled: true, getAccessToken: () => "token", getOrgUUID: () => "org-123");
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeFalse();
        ctx.AccessToken.Should().Be("token");
        ctx.OrgUUID.Should().Be("org-123");
    }

    [Fact]
    public async Task FullGatePipeline_BridgeDisabled_ShortCircuits()
    {
        var pipeline = new PipelineBuilder<BridgeInitGateContext>()
            .WithShortCircuit(ctx => ctx.Failed)
            .Use(new BridgeGateEnabledMiddleware())
            .Use(new BridgeGateOAuthMiddleware())
            .Use(new BridgeGatePolicyMiddleware())
            .Build();

        var ctx = CreateContext(bridgeEnabled: false, getAccessToken: () => "token");
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.Failed.Should().BeTrue();
        ctx.AccessToken.Should().BeNull();
    }

    private static BridgeInitGateContext CreateContext(
        bool bridgeEnabled = true,
        Func<string?>? getAccessToken = null,
        Func<string?>? getOrgUUID = null,
        Func<string>? getBaseUrl = null,
        BridgeInitOptions? options = null)
    {
        return new BridgeInitGateContext
        {
            Options = options ?? new BridgeInitOptions(),
            BridgeEnabled = bridgeEnabled,
            GetAccessToken = getAccessToken ?? (() => "default-token"),
            GetOrgUUID = getOrgUUID ?? (() => "default-org"),
            GetBaseUrl = getBaseUrl ?? (() => "https://api.test.com"),
            FileSystem = new InMemoryFileSystem(),
        };
    }
}
