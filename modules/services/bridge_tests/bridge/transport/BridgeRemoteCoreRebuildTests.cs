// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Bridge.Tests.Transport;

/// <summary>
/// TASK071 #2/#5 — RebuildTransportAsync 重建语义对齐 TS remoteBridgeCore.ts:486-500：
/// 重建必须携带旧 seq（InitialSequenceNum — 防 SSE 全量回放）+ fresh epoch（防双重 bump 409）+
/// 心跳接 config.HeartbeatIntervalMs（旧 4 参便捷工厂误接 ConnectTimeoutMs 致 JCC_BRIDGE_HEARTBEAT_MS 死配置）。
/// </summary>
public sealed class BridgeRemoteCoreRebuildTests {
    /// <summary>可编程序列号的假传输。</summary>
    private sealed class FakeTransport : IReplBridgeTransport {
        public int DisposeCount { get; private set; }
        public int LastSequenceNumValue { get; set; }

        public ValueTask DisposeAsync() {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }

        public Task WriteAsync(string message, CancellationToken ct = default) => Task.CompletedTask;
        public Task WriteBatchAsync(IReadOnlyList<string> messages, CancellationToken ct = default) => Task.CompletedTask;
        public bool IsConnectedStatus() => true;
        public string GetStateLabel() => "fake";
        public void SetOnData(Action<string> callback) { }
        public void SetOnClose(Action<int?> callback) { }
        public void SetOnConnect(Action callback) { }
        public void SetOnBatchDropped(Action<int, int> callback) { }
        public void Connect() { }
        public int GetLastSequenceNum() => LastSequenceNumValue;
        public int DroppedBatchCount { get; }
        public Task ReportStateAsync(BridgeSessionActivity state, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReportMetadataAsync(Dictionary<string, JsonElement> metadata, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReportDeliveryAsync(string eventId, string status, CancellationToken ct = default) => Task.CompletedTask;
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>捕获 V2TransportOptions 的假工厂（行为锁观察点）。</summary>
    private sealed class CapturingFactory : IReplBridgeTransportFactory {
        public V2TransportOptions? LastOptions { get; private set; }
        public IReplBridgeTransport Transport { get; } = new FakeTransport();

        public IReplBridgeTransport CreateV1Transport(V1TransportOptions options, ILogger? logger = null) =>
            throw new NotSupportedException("重建路径不应创建 v1");

        public IReplBridgeTransport CreateV2Transport(V2TransportOptions options, ILogger? logger = null) {
            LastOptions = options;
            return Transport;
        }
    }

    private static BridgeRemoteCredentials Credentials(long epoch = 99) => new() {
        WorkerJwt = "fresh-jwt",
        ApiBaseUrl = "https://bridge.example.com",
        ExpiresIn = 3600,
        WorkerEpochRaw = System.Text.Json.JsonDocument.Parse(epoch.ToString()).RootElement.Clone(),
    };

    private static BridgeInitState InitState() => new() {
        FlushGate = new BridgeFlushGate<string>(),
        RecentPostedUUIDs = new BoundedUUIDSet(64),
        RecentInboundUUIDs = new BoundedUUIDSet(64),
        InitCts = new CancellationTokenSource(),
    };

    private static V2BridgeParams Params() => new() {
        BaseUrl = "https://api.example.com",
        OrgUUID = "org-1",
        Title = "t",
        GetAccessToken = () => "token",
    };

    /// <summary>#2 — 重建传输携带旧 seq + fresh epoch（TS remoteBridgeCore.ts:489-500 显式传递）。</summary>
    [Fact]
    public async Task RebuildTransport_CarriesOldSequenceNumAndFreshEpoch() {
        await using var oldTransport = new FakeTransport { LastSequenceNumValue = 57 };
        var factory = new CapturingFactory();
        var state = InitState();
        var config = new V2BridgeConfig { HeartbeatIntervalMs = 20_000 };

        await BridgeRemoteCore.RebuildTransportAsync(
            Credentials(epoch: 99), "session-1", Params(), config,
            logger: null, oldTransport, factory, state,
            new BridgeTokenRefreshScheduler(new TokenRefreshOptions {
            GetAccessToken = () => "token",
            OnRefresh = (_, _) => { },
            Label = "rebuild-test",
        }), CancellationToken.None).ConfigureAwait(true);

        var options = factory.LastOptions;
        Assert.NotNull(options);
        Assert.Equal(57, options.InitialSequenceNum);
        Assert.Equal(99, options.Epoch);
        Assert.Equal("fresh-jwt", options.IngressToken);
        Assert.Equal("fresh-jwt", options.GetAuthToken?.Invoke());
        Assert.Equal(1, oldTransport.DisposeCount);
        // TASK071 #4 — 重建后 state.Transport 指向新传输（flush 完成回调用它做旧闭包守卫）
        Assert.Same(factory.Transport, state.Transport);
    }

    /// <summary>#5 — 重建传输心跳接 config.HeartbeatIntervalMs（非 ConnectTimeoutMs）。</summary>
    [Fact]
    public async Task RebuildTransport_HeartbeatUsesConfiguredInterval() {
        var factory = new CapturingFactory();
        var config = new V2BridgeConfig { HeartbeatIntervalMs = 25_000, ConnectTimeoutMs = 15_000 };

        await BridgeRemoteCore.RebuildTransportAsync(
            Credentials(), "session-1", Params(), config,
            logger: null, new FakeTransport { LastSequenceNumValue = 3 }, factory, InitState(),
            new BridgeTokenRefreshScheduler(new TokenRefreshOptions {
            GetAccessToken = () => "token",
            OnRefresh = (_, _) => { },
            Label = "rebuild-test",
        }), CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(factory.LastOptions);
        Assert.Equal(25_000, factory.LastOptions.HeartbeatIntervalMs);
    }

    /// <summary>TS remoteBridgeCore.ts:502-506 — 重建途中 TornDown 则新传输立即释放不再接线。</summary>
    [Fact]
    public async Task RebuildTransport_TornDownDuringCreate_DisposesNewTransport() {
        var factory = new CapturingFactory();
        var state = InitState();
        state.TornDown = true;

        await BridgeRemoteCore.RebuildTransportAsync(
            Credentials(), "session-1", Params(), new V2BridgeConfig(),
            logger: null, new FakeTransport(), factory, state,
            new BridgeTokenRefreshScheduler(new TokenRefreshOptions {
            GetAccessToken = () => "token",
            OnRefresh = (_, _) => { },
            Label = "rebuild-test",
        }), CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(1, Assert.IsType<FakeTransport>(factory.Transport).DisposeCount);
    }
}
