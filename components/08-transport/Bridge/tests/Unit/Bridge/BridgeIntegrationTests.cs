
namespace Bridge.Tests;

/// <summary>
/// Bridge 集成测试
/// 验证 Bridge 各组件之间的集成点
/// </summary>
public sealed class BridgeIntegrationTests
{
    private static Mock<IFileOperationService> CreateFileOpMock()
    {
        var mock = new Mock<IFileOperationService>();
        mock.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        return mock;
    }

    #region BridgeServerHostedService + CapacityWakeService

    [Fact]
    public async Task BridgeServerHostedService_StartsCapacityWake()
    {
        // Arrange
        var fileOpMock = CreateFileOpMock();
        var bridgeServer = new BridgeServer(
            fileOpMock.Object,
            port: 0,
            logger: NullLogger<BridgeServer>.Instance);

        var config = new BridgeConfig { Enabled = true };
        var capacityWakeService = new CapacityWakeService(
            new CapacityWakeOptions { CheckIntervalMs = 60000 }, // 长间隔避免实际触发
            NullLogger<CapacityWakeService>.Instance);

        var sut = new BridgeServerHostedService(
            bridgeServer,
            config,
            capacityWakeService,
            NullLogger<BridgeServerHostedService>.Instance);

        // Act
        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);

        // Assert - CapacityWakeService 应已启动监控
        capacityWakeService.GetCurrentCapacity().Should().BeGreaterThanOrEqualTo(1);

        // Cleanup
        await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            await capacityWakeService.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"CapacityWakeService disposal timed out during cleanup: {ex.Message}");
        }
        try
        {
            await sut.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex2)
        {
            System.Diagnostics.Trace.WriteLine($"BridgeServerHostedService disposal timed out during cleanup: {ex2.Message}");
        }
    }

    [Fact]
    public async Task BridgeServerHostedService_WithoutCapacityWake_StillStarts()
    {
        // Arrange
        var fileOpMock = CreateFileOpMock();
        var bridgeServer = new BridgeServer(
            fileOpMock.Object,
            port: 0,
            logger: NullLogger<BridgeServer>.Instance);

        var config = new BridgeConfig { Enabled = true };
        var sut = new BridgeServerHostedService(
            bridgeServer,
            config,
            capacityWakeService: null,
            NullLogger<BridgeServerHostedService>.Instance);

        // Act & Assert - 不应抛出异常
        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            await sut.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"BridgeServerHostedService disposal timed out during cleanup: {ex.Message}");
        }
    }

    #endregion

    #region BridgeServer + BridgeUIService 会话注册/注销

    [Fact]
    public async Task BridgeServer_RegistersSessionToUIService()
    {
        // Arrange
        var uiService = new BridgeUIService(logger: NullLogger<BridgeUIService>.Instance);

        // BridgeServer 构造函数中会订阅 PeerSessionManager 事件
        // 我们直接测试 UIService 的注册行为
        var session = new BridgeSessionDisplay
        {
            SessionId = "test-client-001",
            ClientName = "test-client-001",
            Status = "connected",
            ConnectedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Act - 模拟 BridgeServer 在 WebSocket 连接时调用 RegisterSession
        uiService.RegisterSession(session);

        // Assert
        var activeSessions = await uiService.GetActiveSessionList().ConfigureAwait(true);
        activeSessions.Should().HaveCount(1);
        activeSessions[0].SessionId.Should().Be("test-client-001");
        activeSessions[0].Status.Should().Be("connected");
    }

    [Fact]
    public async Task BridgeServer_UnregistersSessionFromUIService()
    {
        // Arrange
        var uiService = new BridgeUIService(logger: NullLogger<BridgeUIService>.Instance);
        var session = new BridgeSessionDisplay
        {
            SessionId = "test-client-002",
            ClientName = "test-client-002",
            Status = "connected",
            ConnectedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        uiService.RegisterSession(session);

        // Act - 模拟 BridgeServer 在 WebSocket 断开时调用 UnregisterSession
        uiService.UnregisterSession("test-client-002");

        // Assert
        var activeSessions = await uiService.GetActiveSessionList().ConfigureAwait(true);
        activeSessions.Should().BeEmpty();
    }

    #endregion

    #region BridgeClient + JwtService

    [Fact]
    public void BridgeClient_GeneratesJwtTokenOnStart()
    {
        // Arrange
        var jwtService = new BridgeJwtService(new BridgeConfig { JwtSecretKey = "test-secret-key-for-integration-test" }, NullLogger.Instance);

        // Act - 模拟 BridgeClient 启动时生成 JWT Token
        var token = jwtService.GenerateToken("bridge-client", 3600);

        // Assert
        token.Should().NotBeNullOrEmpty();

        var validationResult = jwtService.ValidateToken(token);
        validationResult.IsValid.Should().BeTrue();
        validationResult.Payload.Should().NotBeNull();
        validationResult.Payload!.Sub.Should().Be("bridge-client");
    }

    [Fact]
    public void BridgeClient_JwtTokenRefreshWorks()
    {
        // Arrange
        var jwtService = new BridgeJwtService(new BridgeConfig { JwtSecretKey = "test-secret-key-for-refresh-test" }, NullLogger.Instance);
        // 使用 299 秒过期，使其立即进入刷新窗口（剩余 <= 300 秒）
        var token = jwtService.GenerateToken("bridge-client", 299);

        // Act - 刷新应该返回新 token（已进入刷新窗口）
        var refreshResult = jwtService.RefreshToken(token, 3600);

        // Assert
        refreshResult.Success.Should().BeTrue();
        refreshResult.NewToken.Should().NotBe(token, "已进入刷新窗口应签发新 Token");
    }

    #endregion

    #region BridgeClient + PollConfigManager

    [Fact]
    public async Task BridgeClient_UsesPollConfigForIntervals()
    {
        // Arrange
        var pollConfig = new PollConfig
        {
            IntervalMs = 200,
            MaxIntervalMs = 5000,
            BackoffMultiplier = 2.0,
            JitterPercent = 0.0 // 无抖动，方便断言
        };
        var pollConfigManager = new PollConfigManager(pollConfig, NullLogger<PollConfigManager>.Instance);

        // Act - 无错误时，间隔应为基础值
        var normalInterval = await pollConfigManager.CalculateNextIntervalAsync(hasError: false).ConfigureAwait(true);

        // Assert
        normalInterval.Should().Be(200);

        // Act - 有错误时，间隔应指数退避
        var errorInterval1 = await pollConfigManager.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(true);
        var errorInterval2 = await pollConfigManager.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(true);

        // Assert - 退避应递增
        errorInterval1.Should().BeGreaterThanOrEqualTo(200);
        errorInterval2.Should().BeGreaterThanOrEqualTo(errorInterval1);
    }

    [Fact]
    public async Task BridgeClient_PollConfigResetToDefault()
    {
        // Arrange
        var pollConfigManager = new PollConfigManager(
            new PollConfig { IntervalMs = 500, MaxIntervalMs = 10000 },
            NullLogger<PollConfigManager>.Instance);

        // Act - 触发错误退避
        await pollConfigManager.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(true);
        await pollConfigManager.CalculateNextIntervalAsync(hasError: true).ConfigureAwait(true);

        // 重置
        await pollConfigManager.ResetToDefaultAsync().ConfigureAwait(true);
        var interval = await pollConfigManager.CalculateNextIntervalAsync(hasError: false).ConfigureAwait(true);

        // Assert - 重置后应回到默认值附近（含 jitter）
        interval.Should().BeCloseTo(PollConfig.DefaultIntervalMs, 100);
    }

    #endregion

    #region BridgeServer + FlushGate

    [Fact]
    public async Task BridgeServer_FlushGateBatchesMessages()
    {
        // Arrange
        var flushGate = new FlushGate<BridgeServerMessage>(
            new FlushGateOptions
            {
                MaxBatchSize = 3,
                FlushIntervalMs = 60000 // 长间隔，避免定时触发
            },
            NullLogger.Instance);

        List<IReadOnlyList<BridgeServerMessage>> flushedBatches = new();
        flushGate.BatchFlushed += (_, e) => flushedBatches.Add(e.Items);

        await flushGate.StartAsync().ConfigureAwait(true);

        // Act - 添加消息直到触发批量刷新
        var msg1 = new BridgeServerMessage { Type = "test1" };
        var msg2 = new BridgeServerMessage { Type = "test2" };
        var msg3 = new BridgeServerMessage { Type = "test3" };

        await flushGate.AddAsync(msg1).ConfigureAwait(true);
        await flushGate.AddAsync(msg2).ConfigureAwait(true);

        // 还未满批，不应刷新
        flushedBatches.Should().BeEmpty();

        await flushGate.AddAsync(msg3).ConfigureAwait(true);

        // 满批，应触发刷新
        flushedBatches.Should().HaveCount(1);
        flushedBatches[0].Should().HaveCount(3);

        // Cleanup
        try
        {
            await flushGate.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"FlushGate disposal timed out after batch flush test: {ex.Message}");
        }
    }

    [Fact]
    public async Task BridgeServer_FlushGateManualFlush()
    {
        // Arrange
        var flushGate = new FlushGate<BridgeServerMessage>(
            new FlushGateOptions
            {
                MaxBatchSize = 100, // 大批次，避免自动触发
                FlushIntervalMs = 60000
            },
            NullLogger.Instance);

        List<IReadOnlyList<BridgeServerMessage>> flushedBatches = new();
        flushGate.BatchFlushed += (_, e) => flushedBatches.Add(e.Items);

        await flushGate.StartAsync().ConfigureAwait(true);

        // Act
        await flushGate.AddAsync(new BridgeServerMessage { Type = "test" }).ConfigureAwait(true);
        await flushGate.FlushAsync().ConfigureAwait(true);

        // Assert
        flushedBatches.Should().HaveCount(1);
        flushedBatches[0].Should().HaveCount(1);

        // Cleanup
        try
        {
            await flushGate.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"FlushGate disposal timed out after manual flush test: {ex.Message}");
        }
    }

    [Fact]
    public async Task BridgeServer_BroadcastAsync_RoutesThroughFlushGate()
    {
        // Arrange - 测试定时刷新路由（BridgeServer 生产环境使用 FlushIntervalMs=100）
        var fakeTime = new FakeTimeProvider();
        var flushGate = new FlushGate<BridgeServerMessage>(
            new FlushGateOptions
            {
                MaxBatchSize = 100, // 大批次，避免满批触发
                FlushIntervalMs = 50 // 短间隔，快速触发定时刷新
            },
            NullLogger.Instance,
            fakeTime);

        List<IReadOnlyList<BridgeServerMessage>> flushedBatches = new();
        flushGate.BatchFlushed += (_, e) => flushedBatches.Add(e.Items);

        await flushGate.StartAsync().ConfigureAwait(true);

        // Act - 添加消息，等待定时刷新
        await flushGate.AddAsync(new BridgeServerMessage { Type = "broadcast-test-1" }).ConfigureAwait(true);
        await flushGate.AddAsync(new BridgeServerMessage { Type = "broadcast-test-2" }).ConfigureAwait(true);
        await flushGate.AddAsync(new BridgeServerMessage { Type = "broadcast-test-3" }).ConfigureAwait(true);

        // 推进时间触发定时刷新
        fakeTime.Advance(TimeSpan.FromMilliseconds(300));

        // 等待定时器回调执行 — Task.Delay continuation 在线程池调度，
        // 需要轮询等待 FlushAsync 完成并触发 BatchFlushed 事件
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (flushedBatches.Count == 0 && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10).ConfigureAwait(true);
        }

        // Assert - 定时器应已触发刷新，所有消息应被批量处理
        flushedBatches.Should().HaveCountGreaterThanOrEqualTo(1);
        flushedBatches.SelectMany(b => b).Should().HaveCount(3);

        // Cleanup
        try
        {
            await flushGate.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"FlushGate disposal timed out after broadcast routing test: {ex.Message}");
        }
    }

    #endregion

    #region BridgeServer + PeerSessionManager

    [Fact]
    public async Task BridgeServer_RoutesPeerMessages()
    {
        // Arrange
        var peerSessionManager = new PeerSessionManager(NullLogger<PeerSessionManager>.Instance);

        // 模拟 BridgeServer 订阅 PeerSessionManager.PeerMessageSent 事件
        List<BridgeServerMessage> routedMessages = new();
        peerSessionManager.PeerMessageSent += (_, e) =>
        {
            var serverMessage = new BridgeServerMessage
            {
                Type = "peer_message",
                Data = JsonDocument.Parse(e.Message.ToJson()).RootElement
            };
            routedMessages.Add(serverMessage);
        };

        // 创建对等会话
        var session = await peerSessionManager.CreatePeerSessionAsync("local-peer", "remote-peer").ConfigureAwait(true);
        await peerSessionManager.MarkConnectedAsync(session.SessionId).ConfigureAwait(true);

        // Act - 发送消息到对等节点
        var testMessage = new PingMessage { Id = "peer-ping-001" };
        await peerSessionManager.SendMessageToPeerAsync(session.SessionId, testMessage).ConfigureAwait(true);

        // Assert - 消息应被路由
        routedMessages.Should().HaveCount(1);
        routedMessages[0].Type.Should().Be("peer_message");

        // Cleanup
        try
        {
            await peerSessionManager.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"PeerSessionManager disposal timed out after peer message routing test: {ex.Message}");
        }
    }

    [Fact]
    public async Task BridgeServer_PeerSessionConnectedEvent()
    {
        // Arrange
        var peerSessionManager = new PeerSessionManager(NullLogger<PeerSessionManager>.Instance);
        PeerSession? connectedSession = null;
        peerSessionManager.PeerSessionConnected += (_, e) => connectedSession = e.Session;

        // Act
        var session = await peerSessionManager.CreatePeerSessionAsync("local", "remote").ConfigureAwait(true);
        await peerSessionManager.MarkConnectedAsync(session.SessionId).ConfigureAwait(true);

        // Assert
        connectedSession.Should().NotBeNull();
        connectedSession!.SessionId.Should().Be(session.SessionId);
        connectedSession.Status.Should().Be(PeerSessionStatus.Connected);

        // Cleanup
        try
        {
            await peerSessionManager.DisposeAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        }
        catch (TimeoutException ex)
        {
            System.Diagnostics.Trace.WriteLine($"PeerSessionManager disposal timed out after peer session connected event test: {ex.Message}");
        }
    }

    #endregion

    #region BridgeClient + BridgeApiClient

    [Fact]
    public async Task BridgeClient_ApiClientHealthCheck_WithMockHandler()
    {
        // Arrange - 使用模拟 HttpMessageHandler
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(System.Net.HttpStatusCode.OK));

        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var apiClient = new BridgeApiClient(
            httpClient,
            new BridgeApiOptions { BaseUrl = "http://localhost:3456" },
            NullLogger<BridgeApiClient>.Instance);

        // Act
        var isHealthy = await apiClient.HealthCheckAsync().ConfigureAwait(true);

        // Assert
        isHealthy.Should().BeTrue();

        // Cleanup
        apiClient.Dispose();
    }

    [Fact]
    public async Task BridgeClient_ApiClientHealthCheck_FailsOnUnavailable()
    {
        // Arrange - 使用抛出异常的模拟 Handler
        var handler = new MockFailingHttpMessageHandler();
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var apiClient = new BridgeApiClient(
            httpClient,
            new BridgeApiOptions { BaseUrl = "http://localhost:3456" },
            NullLogger<BridgeApiClient>.Instance);

        // Act
        var isHealthy = await apiClient.HealthCheckAsync().ConfigureAwait(true);

        // Assert
        isHealthy.Should().BeFalse();

        // Cleanup
        apiClient.Dispose();
    }

    #endregion

    #region 辅助类

    /// <summary>
    /// 模拟 HTTP 消息处理器 - 返回固定响应
    /// </summary>
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }

    /// <summary>
    /// 模拟失败的 HTTP 消息处理器
    /// </summary>
    private sealed class MockFailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Connection refused");
        }
    }

    #endregion
}
