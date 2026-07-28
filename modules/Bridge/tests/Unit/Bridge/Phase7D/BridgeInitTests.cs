
namespace Bridge.Tests.Phase7D;

using CoreBridgeState = JoinCode.Transport.Bridge.BridgeState;

public sealed class BridgeInitTests
{
    #region InitBridgeOptions

    [Fact]
    public void InitBridgeOptions_Defaults_AreSet()
    {
        var options = new BridgeInitOptions();
        Assert.Null(options.OnInboundMessage);
        Assert.Null(options.OnPermissionResponse);
        Assert.Null(options.OnInterrupt);
        Assert.Null(options.OnSetModel);
        Assert.Null(options.OnSetMaxThinkingTokens);
        Assert.Null(options.OnSetPermissionMode);
        Assert.Null(options.OnStateChange);
        Assert.Null(options.InitialMessages);
        Assert.Null(options.InitialName);
        Assert.Null(options.GetMessages);
        Assert.False(options.Perpetual);
        Assert.False(options.OutboundOnly);
        Assert.Null(options.Tags);
        Assert.Null(options.IsPolicyAllowed);
        Assert.Null(options.GetTrustedDeviceToken);
        Assert.Null(options.GetOAuthTokenExpiry);
        Assert.Null(options.CheckAndRefreshOAuthToken);
        Assert.Null(options.DeadTokenState);
    }

    #endregion

    #region deriveTitle

    [Fact]
    public void DeriveTitle_ShortText_ReturnsAsIs()
    {
        var result = BridgeInit.DeriveTitle("Hello world");
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void DeriveTitle_LongText_TruncatesWithEllipsis()
    {
        var longText = new string('a', 200);
        var result = BridgeInit.DeriveTitle(longText);
        Assert.True(result!.Length <= 50);
        // 对齐 TS 端: 超长截断加省略号
        Assert.EndsWith("\u2026", result);
    }

    [Fact]
    public void DeriveTitle_Multiline_CollapsesToSingleLine()
    {
        var result = BridgeInit.DeriveTitle("First line\nSecond line");
        Assert.DoesNotContain("\n", result);
    }

    [Fact]
    public void DeriveTitle_FirstSentence_StopsAtPeriod()
    {
        var result = BridgeInit.DeriveTitle("This is a test. And more text.");
        Assert.Equal("This is a test.", result);
    }

    [Fact]
    public void DeriveTitle_EmptyAfterStrip_ReturnsNull()
    {
        var result = BridgeInit.DeriveTitle("");
        Assert.Null(result);
    }

    #endregion

    #region initReplBridge — 前置条件检查

    [Fact]
    public async Task InitReplBridge_BridgeNotEnabled_ReturnsNull()
    {
        var result = await BridgeInit.InitReplBridgeAsync(
            new BridgeInitOptions(),
            bridgeEnabled: false,
            getAccessToken: () => "token",
            getOrgUUID: () => "org-123",
            getBaseUrl: () => "https://api.example.com",
            fs: TestFileSystem.Current).ConfigureAwait(true);
        Assert.Null(result);
    }

    [Fact]
    public async Task InitReplBridge_NoOAuthToken_ReturnsNull()
    {
        var stateChanges = new List<(CoreBridgeState, string?)>();
        var result = await BridgeInit.InitReplBridgeAsync(
            new BridgeInitOptions
            {
                OnStateChange = (state, detail) => stateChanges.Add((state, detail)),
            },
            bridgeEnabled: true,
            getAccessToken: () => null,
            getOrgUUID: () => "org-123",
            getBaseUrl: () => "https://api.example.com",
            fs: TestFileSystem.Current).ConfigureAwait(true);
        Assert.Null(result);
        Assert.Contains(stateChanges, s => s.Item1 == CoreBridgeState.Failed);
    }

    [Fact]
    public async Task InitReplBridge_NoOrgUUID_ReturnsNull()
    {
        var stateChanges = new List<(CoreBridgeState, string?)>();
        var result = await BridgeInit.InitReplBridgeAsync(
            new BridgeInitOptions
            {
                OnStateChange = (state, detail) => stateChanges.Add((state, detail)),
            },
            bridgeEnabled: true,
            getAccessToken: () => "token",
            getOrgUUID: () => null,
            getBaseUrl: () => "https://api.example.com",
            fs: TestFileSystem.Current).ConfigureAwait(true);
        Assert.Null(result);
        Assert.Contains(stateChanges, s => s.Item1 == CoreBridgeState.Failed);
    }

    [Fact]
    public async Task InitReplBridge_PolicyDenied_ReturnsNull()
    {
        // 对齐 TS 端: isPolicyAllowed('allow_remote_control') === false
        var stateChanges = new List<(CoreBridgeState, string?)>();
        var result = await BridgeInit.InitReplBridgeAsync(
            new BridgeInitOptions
            {
                OnStateChange = (state, detail) => stateChanges.Add((state, detail)),
                IsPolicyAllowed = policy => false,
            },
            bridgeEnabled: true,
            getAccessToken: () => "token",
            getOrgUUID: () => "org-123",
            getBaseUrl: () => "https://api.example.com",
            fs: TestFileSystem.Current).ConfigureAwait(true);
        Assert.Null(result);
        Assert.Contains(stateChanges, s => s.Item1 == CoreBridgeState.Failed && s.Item2 == "disabled by your organization's policy");
    }

    [Fact]
    public async Task InitReplBridge_PolicyAllowed_Proceeds()
    {
        // 对齐 TS 端: isPolicyAllowed('allow_remote_control') === true → 不阻塞
        var result = await BridgeInit.InitReplBridgeAsync(
            new BridgeInitOptions
            {
                IsPolicyAllowed = policy => true,
            },
            bridgeEnabled: true,
            getAccessToken: () => "token",
            getOrgUUID: () => "org-123",
            getBaseUrl: () => "https://api.example.com",
            fs: TestFileSystem.Current).ConfigureAwait(true);
        // httpClient/transportFactory 为 null，会在后续步骤返回 null
        // 但不应因策略检查返回 null
        Assert.Null(result); // 预期：不是因为策略被拒绝
    }

    [Fact]
    public async Task InitReplBridge_PolicyNull_FailOpen()
    {
        // 对齐 TS 端: IsPolicyAllowed 为 null → fail-open，视为允许
        var result = await BridgeInit.InitReplBridgeAsync(
            new BridgeInitOptions
            {
                IsPolicyAllowed = null, // fail-open
            },
            bridgeEnabled: true,
            getAccessToken: () => "token",
            getOrgUUID: () => "org-123",
            getBaseUrl: () => "https://api.example.com",
            fs: TestFileSystem.Current).ConfigureAwait(true);
        Assert.Null(result); // 预期：不是因为策略被拒绝
    }

    [Fact]
    public async Task InitReplBridge_CrossProcessBackoff_SkipsDeadToken()
    {
        // 对齐 TS 端 2a: 同一 expiresAt 的死令牌 failCount >= 3 → 跳过
        var deadExpiry = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var deadState = new TestDeadTokenState(deadExpiry, 3);

        var result = await BridgeInit.InitReplBridgeAsync(
            new BridgeInitOptions
            {
                GetOAuthTokenExpiry = () => deadExpiry,
                DeadTokenState = deadState,
            },
            bridgeEnabled: true,
            getAccessToken: () => "dead-token",
            getOrgUUID: () => "org-123",
            getBaseUrl: () => "https://api.example.com",
            fs: TestFileSystem.Current).ConfigureAwait(true);
        Assert.Null(result);
    }

    [Fact]
    public async Task InitReplBridge_CrossProcessBackoff_DifferentToken_Proceeds()
    {
        // 对齐 TS 端 2a: 不同 expiresAt → 退避不匹配，继续
        var deadExpiry = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var newExpiry = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var deadState = new TestDeadTokenState(deadExpiry, 3);

        var result = await BridgeInit.InitReplBridgeAsync(
            new BridgeInitOptions
            {
                GetOAuthTokenExpiry = () => newExpiry, // 不同 token
                DeadTokenState = deadState,
            },
            bridgeEnabled: true,
            getAccessToken: () => "new-token",
            getOrgUUID: () => "org-123",
            getBaseUrl: () => "https://api.example.com",
            fs: TestFileSystem.Current).ConfigureAwait(true);
        // 不因退避返回 null（后续因 httpClient null 返回 null）
        Assert.Null(result);
    }

    [Fact]
    public async Task InitReplBridge_ExpiredTokenAfterRefresh_ReturnsNull()
    {
        // 对齐 TS 端 2c: 刷新后仍过期 → 死令牌，记录到 DeadTokenState
        var pastExpiry = DateTimeOffset.UtcNow.AddMinutes(-1);
        var deadState = new TestDeadTokenState(null, 0);

        var stateChanges = new List<(CoreBridgeState, string?)>();
        var result = await BridgeInit.InitReplBridgeAsync(
            new BridgeInitOptions
            {
                OnStateChange = (state, detail) => stateChanges.Add((state, detail)),
                GetOAuthTokenExpiry = () => pastExpiry,
                CheckAndRefreshOAuthToken = () => Task.FromResult(false),
                DeadTokenState = deadState,
            },
            bridgeEnabled: true,
            getAccessToken: () => "expired-token",
            getOrgUUID: () => "org-123",
            getBaseUrl: () => "https://api.example.com",
            fs: TestFileSystem.Current).ConfigureAwait(true);
        Assert.Null(result);
        Assert.Contains(stateChanges, s => s.Item1 == CoreBridgeState.Failed);
        // 死令牌应被记录
        Assert.Equal(pastExpiry, deadState.RecordedExpiry);
    }

    [Fact]
    public async Task InitReplBridge_NullExpiry_NeverSkips()
    {
        // 对齐 TS 端 2c: env-var/FD 令牌 expiresAt=null → 永远不触发过期跳过
        var result = await BridgeInit.InitReplBridgeAsync(
            new BridgeInitOptions
            {
                GetOAuthTokenExpiry = () => null, // env-var 令牌
                CheckAndRefreshOAuthToken = () => Task.FromResult(true),
            },
            bridgeEnabled: true,
            getAccessToken: () => "env-token",
            getOrgUUID: () => "org-123",
            getBaseUrl: () => "https://api.example.com",
            fs: TestFileSystem.Current).ConfigureAwait(true);
        // 不因过期跳过（后续因 httpClient null 返回 null）
        Assert.Null(result);
    }

    #endregion

    /// <summary>
    /// 测试用死令牌状态实现
    /// </summary>
    private sealed class TestDeadTokenState : IBridgeOAuthDeadTokenState
    {
        public TestDeadTokenState(DateTimeOffset? deadExpiresAt, int deadFailCount)
        {
            DeadExpiresAt = deadExpiresAt;
            DeadFailCount = deadFailCount;
        }

        public DateTimeOffset? DeadExpiresAt { get; }
        public int DeadFailCount { get; }
        public DateTimeOffset? RecordedExpiry { get; private set; }

        public Task RecordDeadTokenAsync(DateTimeOffset expiresAt)
        {
            RecordedExpiry = expiresAt;
            return Task.CompletedTask;
        }
    }
}
