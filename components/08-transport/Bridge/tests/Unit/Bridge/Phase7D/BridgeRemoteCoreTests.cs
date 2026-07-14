
namespace Bridge.Tests.Phase7D;

public sealed class BridgeRemoteCoreTests
{
    #region withRetry

    [Fact]
    public async Task WithRetry_SucceedsOnFirstAttempt_ReturnsResult()
    {
        var callCount = 0;
        var result = await BridgeRemoteCore.WithRetryAsync<string>(
            () => { callCount++; return Task.FromResult<string?>("ok"); },
            "test",
            maxAttempts: 3,
            baseDelayMs: 10,
            maxDelayMs: 100,
            jitterFraction: 0).ConfigureAwait(true);
        Assert.Equal("ok", result);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task WithRetry_SucceedsOnSecondAttempt_ReturnsResult()
    {
        var callCount = 0;
        var result = await BridgeRemoteCore.WithRetryAsync<string>(
            () => { callCount++; return Task.FromResult<string?>(callCount < 2 ? null : "ok"); },
            "test",
            maxAttempts: 3,
            baseDelayMs: 10,
            maxDelayMs: 100,
            jitterFraction: 0).ConfigureAwait(true);
        Assert.Equal("ok", result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task WithRetry_ExhaustsAttempts_ReturnsNull()
    {
        var result = await BridgeRemoteCore.WithRetryAsync<string>(
            () => Task.FromResult<string?>(null),
            "test",
            maxAttempts: 2,
            baseDelayMs: 10,
            maxDelayMs: 100,
            jitterFraction: 0).ConfigureAwait(true);
        Assert.Null(result);
    }

    #endregion

    #region EnvLessBridgeParams

    [Fact]
    public void EnvLessBridgeParams_Defaults_AreSet()
    {
        var params_ = new BridgeEnvLessParams
        {
            BaseUrl = "https://api.example.com",
            OrgUUID = "org-123",
            Title = "Test",
            GetAccessToken = () => "token",
            OnInboundMessage = _ => { },
        };
        Assert.Equal("https://api.example.com", params_.BaseUrl);
        Assert.Equal("org-123", params_.OrgUUID);
        Assert.Equal("Test", params_.Title);
        Assert.False(params_.OutboundOnly);
        Assert.Equal(0, params_.InitialHistoryCap);
    }

    #endregion

    #region archiveSession

    [Fact]
    public async Task ArchiveSession_NullToken_ReturnsSkipped()
    {
        var result = await BridgeSessionApi.ArchiveAsync(
            sessionId: "cse_test",
            baseUrl: "https://api.example.com",
            accessToken: null,
            orgUUID: "org-123",
            timeoutMs: 1000,
            httpClient: null!).ConfigureAwait(true);
        Assert.Equal("skipped_no_token", result);
    }

    #endregion

    #region deriveTitle

    [Fact]
    public void DeriveTitle_ShortText_ReturnsAsIs()
    {
        var result = BridgeRemoteCore.DeriveTitle("Hello world");
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void DeriveTitle_LongText_Truncates()
    {
        var longText = new string('a', 200);
        var result = BridgeRemoteCore.DeriveTitle(longText);
        Assert.True(result.Length <= 50);
    }

    [Fact]
    public void DeriveTitle_Multiline_TakesFirstLine()
    {
        var result = BridgeRemoteCore.DeriveTitle("First line\nSecond line");
        Assert.Equal("First line", result);
    }

    [Fact]
    public void DeriveTitle_EmptyText_ReturnsEmpty()
    {
        var result = BridgeRemoteCore.DeriveTitle("");
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region makeResultMessage

    [Fact]
    public void MakeResultMessage_ContainsSessionId()
    {
        var result = BridgeMessaging.MakeResultMessage("cse_test123");
        Assert.Contains("cse_test123", result);
        Assert.Contains("\"type\":\"result\"", result);
        Assert.Contains("\"subtype\":\"success\"", result);
    }

    #endregion

    #region flushHistory

    [Fact]
    public async Task FlushHistory_NoMessages_NoWrite()
    {
        var transport = new MockTransport();
        await BridgeRemoteCore.FlushHistoryAsync(
            [], 0, null, transport, "cse_test", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(0, transport.WriteBatchCallCount);
    }

    [Fact]
    public async Task FlushHistory_WithCap_TruncatesFromStart()
    {
        var transport = new MockTransport();
        var messages = new[] { "msg1", "msg2", "msg3", "msg4", "msg5" };
        await BridgeRemoteCore.FlushHistoryAsync(
            messages, initialHistoryCap: 3, null, transport, "cse_test", CancellationToken.None).ConfigureAwait(true);
        // 应该只发送最后 3 条消息
        Assert.Equal(1, transport.WriteBatchCallCount);
        Assert.Equal(3, transport.LastBatch!.Count);
        Assert.Equal("msg3", transport.LastBatch[0]);
        Assert.Equal("msg4", transport.LastBatch[1]);
        Assert.Equal("msg5", transport.LastBatch[2]);
    }

    [Fact]
    public async Task FlushHistory_NoCap_SendsAll()
    {
        var transport = new MockTransport();
        var messages = new[] { "msg1", "msg2", "msg3" };
        await BridgeRemoteCore.FlushHistoryAsync(
            messages, initialHistoryCap: 0, null, transport, "cse_test", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, transport.WriteBatchCallCount);
        Assert.Equal(3, transport.LastBatch!.Count);
    }

    [Fact]
    public async Task FlushHistory_WithToSDKMessages_ConvertsAndSends()
    {
        var transport = new MockTransport();
        var messages = new[] { "raw1", "raw2" };
        Func<string, string[]> toSDK = msg => [$"sdk_{msg}"];
        await BridgeRemoteCore.FlushHistoryAsync(
            messages, initialHistoryCap: 0, toSDK, transport, "cse_test", CancellationToken.None).ConfigureAwait(true);
        Assert.Equal(1, transport.WriteBatchCallCount);
        Assert.Equal(2, transport.LastBatch!.Count);
        Assert.Equal("sdk_raw1", transport.LastBatch[0]);
        Assert.Equal("sdk_raw2", transport.LastBatch[1]);
    }

    #endregion

    #region drainFlushGate

    [Fact]
    public void DrainFlushGate_NoPending_NoWrite()
    {
        var flushGate = new BridgeFlushGate<string>();
        var transport = new MockTransport();
        var uuidSet = new BoundedUUIDSet(100);
        flushGate.Start();
        var msgs = flushGate.End();
        Assert.Empty(msgs);

        BridgeRemoteCore.DrainFlushGate(flushGate, uuidSet, null, transport, "cse_test");
        Assert.Equal(0, transport.WriteBatchCallCount);
    }

    [Fact]
    public void DrainFlushGate_WithPending_SendsAll()
    {
        var flushGate = new BridgeFlushGate<string>();
        var transport = new MockTransport();
        var uuidSet = new BoundedUUIDSet(100);

        flushGate.Start();
        flushGate.Enqueue("queued1", "queued2");

        BridgeRemoteCore.DrainFlushGate(flushGate, uuidSet, null, transport, "cse_test");
        Assert.Equal(1, transport.WriteBatchCallCount);
        Assert.Equal(2, transport.LastBatch!.Count);
        Assert.Equal("queued1", transport.LastBatch[0]);
        Assert.Equal("queued2", transport.LastBatch[1]);
    }

    [Fact]
    public void DrainFlushGate_WithToSDKMessages_ConvertsAndSends()
    {
        var flushGate = new BridgeFlushGate<string>();
        var transport = new MockTransport();
        var uuidSet = new BoundedUUIDSet(100);
        Func<string, string[]> toSDK = msg => [$"sdk_{msg}"];

        flushGate.Start();
        flushGate.Enqueue("raw1");

        BridgeRemoteCore.DrainFlushGate(flushGate, uuidSet, toSDK, transport, "cse_test");
        Assert.Equal(1, transport.WriteBatchCallCount);
        Assert.Single(transport.LastBatch!);
        Assert.Equal("sdk_raw1", transport.LastBatch![0]);
    }

    #endregion

    /// <summary>模拟传输层 — 用于测试</summary>
    private sealed class MockTransport : IReplBridgeTransport
    {
        public int WriteBatchCallCount { get; private set; }
        public List<string>? LastBatch { get; private set; }

        public Task WriteAsync(string message, CancellationToken ct = default) => Task.CompletedTask;

        public Task WriteBatchAsync(IReadOnlyList<string> messages, CancellationToken ct = default)
        {
            WriteBatchCallCount++;
            LastBatch = [.. messages];
            return Task.CompletedTask;
        }

        public void Close() { }
        public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public bool IsConnectedStatus() => true;
        public string GetStateLabel() => "mock";
        public void SetOnData(Action<string> callback) { }
        public void SetOnClose(Action<int?> callback) { }
        public void SetOnConnect(Action callback) { }
        public void SetOnBatchDropped(Action<int, int> callback) { }
        public void Connect() { }
        public int GetLastSequenceNum() => 0;
        public int DroppedBatchCount => 0;
        public Task ReportStateAsync(BridgeSessionActivity state, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReportMetadataAsync(Dictionary<string, JsonElement> metadata, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReportDeliveryAsync(string eventId, string status, CancellationToken ct = default) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
