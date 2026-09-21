namespace Host.Tests.ChatCommands.Bridge;


/// <summary>
/// BridgeMainCommand Guard 集成单元测试 — P0-C TDD
/// 验证 IsPolicyAllowed/GetAccessToken/CheckRemoteDialogAccepted 三个集成点
/// </summary>
public sealed class BridgeMainCommandGuardIntegrationTests {
    private readonly Mock<IFileSystem> _fsMock = new();
    private readonly Mock<IProcessService> _processMock = new();

    private BridgeMainCommand CreateCommand(
        IRemotePolicyService? policyService = null,
        ITokenStorage? tokenStorage = null,
        IConfigurationService? configService = null,
        ILogger<BridgeMainCommand>? logger = null)
        => new(
            services: null,
            fs: _fsMock.Object,
            processService: _processMock.Object,
            policyService: policyService,
            tokenStorage: tokenStorage,
            configService: configService,
            logger: logger);

    // ============================================================
    // IsPolicyAllowedAsync
    // ============================================================

    [Fact]
    public async Task IsPolicyAllowedAsync_WhenPolicyServiceNull_FailOpen_ReturnsTrue() {
        var command = CreateCommand(policyService: null);
        var result = await command.IsPolicyAllowedAsync(CancellationToken.None).ConfigureAwait(true);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsPolicyAllowedAsync_WhenRuleAllows_ReturnsTrue() {
        var policyMock = new Mock<IRemotePolicyService>();
        policyMock.Setup(p => p.EvaluateAsync(
                It.Is<string>(a => a == "allow_remote_control"),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyEvaluationResult {
                RuleId = "test-rule-allow",
                Allowed = true,
                Action = PolicyAction.Allow
            });
        var command = CreateCommand(policyService: policyMock.Object);

        var result = await command.IsPolicyAllowedAsync(CancellationToken.None).ConfigureAwait(true);

        result.Should().BeTrue();
        policyMock.Verify(p => p.EvaluateAsync(
            It.Is<string>(a => a == "allow_remote_control"),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsPolicyAllowedAsync_WhenRuleDenies_ReturnsFalse() {
        var policyMock = new Mock<IRemotePolicyService>();
        policyMock.Setup(p => p.EvaluateAsync(
                It.Is<string>(a => a == "allow_remote_control"),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PolicyEvaluationResult {
                RuleId = "test-rule-deny",
                Allowed = false,
                Action = PolicyAction.Deny,
                Reason = "禁止远程控制"
            });
        var command = CreateCommand(policyService: policyMock.Object);

        var result = await command.IsPolicyAllowedAsync(CancellationToken.None).ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsPolicyAllowedAsync_WhenPolicyServiceThrows_LogsAndFailsOpen() {
        var policyMock = new Mock<IRemotePolicyService>();
        policyMock.Setup(p => p.EvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("策略服务不可达"));
        var command = CreateCommand(policyService: policyMock.Object);

        var result = await command.IsPolicyAllowedAsync(CancellationToken.None).ConfigureAwait(true);

        result.Should().BeTrue();
    }

    // ============================================================
    // GetAccessTokenAsync
    // ============================================================

    [Fact]
    public async Task GetAccessTokenAsync_WhenStoredTokenValid_PrefersStoredOverEnvVar() {
        using var env = EnvVarScope.Set(JccEnvVar.SessionAccessToken.ToValue(), "env-token-xxx")
            .Add(JccEnvVar.OAuthToken.ToValue(), null);
        var tokenMock = new Mock<ITokenStorage>();
        tokenMock.Setup(t => t.LoadTokenAsync(
                It.Is<string>(p => p == "anthropic"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OAuthToken {
                AccessToken = "stored-token",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
        var command = CreateCommand(tokenStorage: tokenMock.Object);

        var result = await command.GetAccessTokenAsync(CancellationToken.None).ConfigureAwait(true);

        result.Should().Be("stored-token");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenNoEnvVar_LoadsFromTokenStorage() {
        using var env = EnvVarScope.Set(JccEnvVar.SessionAccessToken.ToValue(), null)
            .Add(JccEnvVar.OAuthToken.ToValue(), null);
        var tokenMock = new Mock<ITokenStorage>();
        tokenMock.Setup(t => t.LoadTokenAsync(
                It.Is<string>(p => p == "anthropic"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OAuthToken {
                AccessToken = "stored-token-abc",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
        var command = CreateCommand(tokenStorage: tokenMock.Object);

        var result = await command.GetAccessTokenAsync(CancellationToken.None).ConfigureAwait(true);

        result.Should().Be("stored-token-abc");
        tokenMock.Verify(t => t.LoadTokenAsync(
            It.Is<string>(p => p == "anthropic"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTokenExpired_ReturnsNull_Then_FallsBackToOAuthEnv() {
        using var env = EnvVarScope.Set(JccEnvVar.OAuthToken.ToValue(), "oauth-env-fallback")
            .Add(JccEnvVar.SessionAccessToken.ToValue(), null);
        var tokenMock = new Mock<ITokenStorage>();
        tokenMock.Setup(t => t.LoadTokenAsync(
                It.Is<string>(p => p == "anthropic"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OAuthToken {
                AccessToken = "expired-token",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1)
            });
        var command = CreateCommand(tokenStorage: tokenMock.Object);

        var result = await command.GetAccessTokenAsync(CancellationToken.None).ConfigureAwait(true);

        result.Should().Be("oauth-env-fallback");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTokenStorageNull_FallsBackToOAuthEnvVar() {
        using var env = EnvVarScope.Set(JccEnvVar.OAuthToken.ToValue(), "fallback-from-env")
            .Add(JccEnvVar.SessionAccessToken.ToValue(), null);
        var command = CreateCommand(tokenStorage: null);

        var result = await command.GetAccessTokenAsync(CancellationToken.None).ConfigureAwait(true);

        result.Should().Be("fallback-from-env");
    }

    // ============================================================
    // CheckRemoteDialogAcceptedAsync
    // ============================================================

    [Fact]
    public async Task CheckRemoteDialogAcceptedAsync_WhenConfigServiceNull_ReturnsFalse() {
        var command = CreateCommand(configService: null);

        var result = await command.CheckRemoteDialogAcceptedAsync(CancellationToken.None).ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CheckRemoteDialogAcceptedAsync_WhenSettingIsTrue_ReturnsTrue() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(c => c.GetAsync(
                It.Is<string>(k => k == "remoteDialogSeen"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("true");
        var command = CreateCommand(configService: configMock.Object);

        var result = await command.CheckRemoteDialogAcceptedAsync(CancellationToken.None).ConfigureAwait(true);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRemoteDialogAcceptedAsync_WhenSettingIsMissing_ReturnsFalse() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(c => c.GetAsync(
                It.Is<string>(k => k == "remoteDialogSeen"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var command = CreateCommand(configService: configMock.Object);

        var result = await command.CheckRemoteDialogAcceptedAsync(CancellationToken.None).ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MarkRemoteDialogSeenAsync_CallsSetAsync() {
        var configMock = new Mock<IConfigurationService>();
        configMock.Setup(c => c.SetAsync(
                It.Is<string>(k => k == "remoteDialogSeen"),
                It.Is<string>(v => v == "true"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var command = CreateCommand(configService: configMock.Object);

        await command.MarkRemoteDialogSeenAsync(CancellationToken.None).ConfigureAwait(true);

        configMock.Verify(c => c.SetAsync(
            It.Is<string>(k => k == "remoteDialogSeen"),
            It.Is<string>(v => v == "true"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ============================================================
    // Deps Wiring
    // ============================================================

    [Fact]
    public async Task BuildDeps_PassesMarkRemoteDialogSeen_ToBridgeMainDeps() {
        using var env = EnvVarScope.Set(JccEnvVar.SessionAccessToken.ToValue(), "test-token-for-deps");
        var configMock = new Mock<IConfigurationService>();
        var command = CreateCommand(configService: configMock.Object);

        var deps = await command.BuildDepsForTestAsync(new BridgeMainArgs { DebugLog = false });

        deps.Should().NotBeNull();
        deps!.MarkRemoteDialogSeen.Should().NotBeNull();
    }
}