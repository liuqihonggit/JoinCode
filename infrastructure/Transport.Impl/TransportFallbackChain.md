# 传输层纵深防御 Fallback 链设计（L1-L10 完整层）

> 本文档定义传输层 fallback 链的完整纵深防御体系，覆盖客户端和服务端，含熔断器和遥测。

---

## 1. 设计决策

| 决策 | 选择 | 原因 |
|------|------|------|
| 服务端 fallback 语义 | 首选传输启动 + 运行时异常降级启动下一传输 | 服务端同一时刻只需一种传输在服务 |
| 客户端 fallback 触发时机 | 初始连接 + 运行时断连 + 熔断器 | 最完整防御，对齐用户选择 |
| Stdio 加入链条件 | 有 `command` 才加入 | Stdio 需要本地子进程，无 command 时无意义 |
| 纵深防御层级 | L1-L10 完整层 | 含熔断器+遥测，企业级纵深防御 |
| Fallback 链优先级 | Stdio > StreamableHTTP > SSE > WebSocket | 本地优先 > 流式优先 > 轻量优先，对齐 MCP 规范 |

---

## 2. 三大通讯场景

| 场景 | 说明 | 入口 | Fallback 语义 |
|------|------|------|---------------|
| **E2E ↔ jcc.exe** | 测试操控 jcc.exe | `StdioProcessManager` | 仅 Stdio/SSE，由 `TransportMode` 静态切换，无需运行时 fallback |
| **Bridge ↔ 子进程** | jcc.exe 与 Bridge 通讯 | `IReplBridgeTransport` | V1(WS) ↔ V2(SSE) 协议切换，已有 `ConnectionManager.SwitchProtocolAsync` |
| **MCP ↔ 外部服务器** | jcc.exe 与 MCP 服务器通讯 | `IMcpTransport` | **本次实现重点**：运行时自动 fallback |

> E2E 和 Bridge 场景已有足够的防御机制，本次重点实现 MCP 场景的客户端和服务端 fallback。

---

## 3. Fallback 链定义

### 3.1 客户端链（MCP Client → 外部 MCP Server）

```
Stdio(有command时) → StreamableHTTP(有url时) → SSE(有url时) → WebSocket(有url时)
```

| 优先级 | 传输 | 加入链条件 | 触发降级条件 | 已实现 |
|--------|------|-----------|-------------|--------|
| 1 | **Stdio** | `config.Command != null` | 子进程启动失败 / stdin 管道断裂 | ✅ `StdioTransport` |
| 2 | **Streamable HTTP** | `config.Endpoint != null` | 连接超时 / 404 会话失效 / Step-Up 认证升级 | ✅ `HttpTransport` |
| 3 | **SSE** (Client) | `config.Endpoint != null` | 连接超时 / 重连5次耗尽 / Step-Up 认证升级 | ✅ `SseClientTransport` |
| 4 | **WebSocket** | `config.Endpoint != null` | 连接超时 / 关闭码 / 认证失败 | ✅ `WebSocketTransport` |

**关键规则**：
- Stdio 和 HTTP/SSE/WS 是两种不同场景，但可共存于同一链（如本地子进程失败后尝试远程 URL）
- 无 `command` 时链从 StreamableHTTP 开始
- 无 `url` 时链从 Stdio 开始（纯本地场景，无 fallback）
- `command` + `url` 都有时，完整链 Stdio → HTTP → SSE → WS

### 3.2 服务端链（MCP Server 接受客户端连接）

```
StreamableHTTP(有url时) → SSE(有url时) → Stdio(有command时)
```

| 优先级 | 传输 | 加入链条件 | 触发降级条件 | 已实现 |
|--------|------|-----------|-------------|--------|
| 1 | **Streamable HTTP** | `config.Endpoint != null` | 端口被占 / 监听失败 / 运行时异常 | ✅ `HttpTransport`(服务端) |
| 2 | **SSE** | `config.Endpoint != null` | HTTP 不可用 / 运行时异常 | ✅ `SseTransport` |
| 3 | **Stdio** | `config.Command != null` | HTTP/SSE 均不可用 | ✅ `StdioTransport` |

**服务端特殊规则**：
- 服务端优先 HTTP（新版 MCP 标准），SSE 是旧版兼容，Stdio 是最后兜底
- 运行时降级：当前传输异常时，自动启动下一优先级传输，同时停止当前传输
- 降级后客户端需重新连接（服务端无法透明迁移已有连接）

---

## 4. 纵深防御 L1-L10 层级定义

### L1：传输健康检查

**目标**：连接前快速判断传输是否可用，避免无效连接尝试。

```csharp
public interface ITransportHealthCheck
{
    /// <summary>传输类型标识</summary>
    string TransportType { get; }

    /// <summary>快速健康检查（不建立完整连接，超时 2s）</summary>
    Task<TransportHealthResult> CheckAsync(CancellationToken ct = default);
}

public sealed class TransportHealthResult
{
    public required bool IsAvailable { get; init; }
    public required string TransportType { get; init; }
    public string? UnavailableReason { get; init; }
    public TimeSpan CheckDuration { get; init; }
    public TransportUnavailabilityCategory? Category { get; init; }
}

/// <summary>
/// 传输不可用的分类 — 区分不同原因，指导 fallback 策略
/// </summary>
public enum TransportUnavailabilityCategory
{
    /// <summary>网络不可达（端口未监听、DNS 失败等）</summary>
    NetworkUnreachable,

    /// <summary>沙箱/OS 拦截（HttpListener 被沙箱策略阻止、权限不足等）</summary>
    SandboxBlocked,

    /// <summary>配置缺失（无 command、无 url 等）</summary>
    ConfigMissing,

    /// <summary>端口冲突（端口已被占用）</summary>
    PortConflict,

    /// <summary>依赖缺失（如 WebSocket 库未安装）</summary>
    DependencyMissing
}
```

**实现策略**：
- **Stdio**：检查 command 路径是否存在 + 可执行权限 → `ConfigMissing` / `SandboxBlocked`
- **HTTP**：尝试短暂启动监听器检测沙箱拦截 → `SandboxBlocked` / `PortConflict` / `NetworkUnreachable`
- **SSE**：同 HTTP（尝试短暂启动监听器）
- **WebSocket**：TCP 端口可达性检查 → `NetworkUnreachable`

**关键实战经验**：
> C# `HttpListener` 会被沙箱策略拦截（如 Linux namespace / Bubblewrap 沙箱禁止 HttpListener 使用），
> 导致 SSE 传输完全失效。健康检查必须能检测到这种"传输本身没问题但被环境拦截"的场景。
> 检测方法：**尝试短暂启动监听器（100ms），捕获 `HttpListenerException` 等权限异常**，
> 而非仅做 TCP 端口检查。检测到 `SandboxBlocked` 后立即 fallback 到下一传输（如基于 TcpListener 的实现）。

**位置**：`infrastructure/Transport.Impl/src/Shared/TransportHealthCheck.cs`

---

### L2：连接超时（每传输独立超时）

**目标**：每个传输尝试有独立超时，不会因一个慢传输阻塞整个链。

```csharp
public sealed class TransportFallbackConfig
{
    /// <summary>单个传输连接超时（默认 5s）</summary>
    public int ConnectTimeoutMs { get; init; } = 5000;

    /// <summary>健康检查超时（默认 2s）</summary>
    public int HealthCheckTimeoutMs { get; init; } = 2000;

    /// <summary>整个 fallback 链总超时（默认 30s）</summary>
    public int ChainTimeoutMs { get; init; } = 30000;

    /// <summary>是否启用 fallback 链（默认 true）</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>是否启用健康检查（L1，默认 true）</summary>
    public bool HealthCheckEnabled { get; init; } = true;

    /// <summary>是否启用熔断器（L9，默认 true）</summary>
    public bool CircuitBreakerEnabled { get; init; } = true;
}
```

**位置**：`infrastructure/Transport.Impl/src/Shared/TransportFallbackConfig.cs`

---

### L3：自动降级（核心 fallback 逻辑）

**目标**：首选传输失败时，自动尝试下一优先级传输。

#### 客户端：`McpTransportFallbackChain`

```csharp
/// <summary>
/// MCP 客户端传输 fallback 链 — 按 MCP 规范优先级依次尝试，首个成功即返回
/// 实现 IMcpTransport 接口，对调用方透明
/// </summary>
public sealed class McpTransportFallbackChain : TransportBase, IMcpTransport
{
    private readonly IMcpTransport[] _transports;       // 按优先级排列
    private readonly ITransportHealthCheck[] _healthChecks;
    private readonly TransportFallbackConfig _config;
    private readonly TransportCircuitBreaker[] _circuitBreakers; // L9
    private readonly TransportFallbackMetrics _metrics;          // L7
    private IMcpTransport? _activeTransport;
    private int _activeIndex = -1;

    public event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;
    public new event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;
    public event EventHandler<TransportFallbackEventArgs>? FallbackOccurred;  // L6

    public async override Task StartAsync(CancellationToken ct = default)
    {
        // L2: 链总超时
        using var chainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        chainCts.CancelAfter(_config.ChainTimeoutMs);

        for (var i = 0; i < _transports.Length; i++)
        {
            // L9: 熔断器检查
            if (_circuitBreakers[i].IsOpen)
            {
                _logger?.LogWarning("传输 {Type} 熔断中，跳过", _transports[i].GetType().Name);
                continue;
            }

            // L1: 健康检查
            if (_config.HealthCheckEnabled && _healthChecks.Length > i)
            {
                var health = await _healthChecks[i].CheckAsync(chainCts.Token).ConfigureAwait(false);
                if (!health.IsAvailable)
                {
                    _logger?.LogWarning("传输 {Type} 健康检查失败: {Reason}，尝试下一个",
                        _transports[i].GetType().Name, health.UnavailableReason);
                    _circuitBreakers[i].RecordFailure(); // L9
                    continue;
                }
            }

            // L2: 单传输超时
            using var transportCts = CancellationTokenSource.CreateLinkedTokenSource(chainCts.Token);
            transportCts.CancelAfter(_config.ConnectTimeoutMs);

            try
            {
                await _transports[i].StartAsync(transportCts.Token).ConfigureAwait(false);
                _activeTransport = _transports[i];
                _activeIndex = i;
                _circuitBreakers[i].RecordSuccess(); // L9
                _metrics.RecordConnection(i);         // L7
                WireEvents(_activeTransport);
                return;
            }
            catch (Exception ex) when (i < _transports.Length - 1)
            {
                _logger?.LogWarning(ex, "传输 {Type} 连接失败({TimeoutMs}ms超时)，尝试下一个",
                    _transports[i].GetType().Name, _config.ConnectTimeoutMs);
                _circuitBreakers[i].RecordFailure(); // L9
                _metrics.RecordFailure(i);            // L7
            }
        }

        throw new InvalidOperationException(
            $"所有传输方式均失败（尝试了 {_transports.Length} 种，熔断跳过 {CountCircuitOpen()} 种）");
    }
}
```

#### 服务端：`McpServerTransportFallbackChain`

```csharp
/// <summary>
/// MCP 服务端传输 fallback 链 — 首选传输启动，运行时异常降级启动下一传输
/// </summary>
public sealed class McpServerTransportFallbackChain : TransportBase, IMcpTransport
{
    private readonly IMcpTransport[] _transports;       // 按优先级排列
    private IMcpTransport? _activeTransport;
    private int _activeIndex = -1;

    public event EventHandler<McpMessageReceivedEventArgs>? MessageReceived;
    public new event EventHandler<McpTransportErrorEventArgs>? ErrorOccurred;
    public event EventHandler<TransportFallbackEventArgs>? FallbackOccurred;

    /// <summary>
    /// 启动首选传输，失败时降级到下一传输
    /// </summary>
    public async override Task StartAsync(CancellationToken ct = default)
    {
        for (var i = 0; i < _transports.Length; i++)
        {
            try
            {
                await _transports[i].StartAsync(ct).ConfigureAwait(false);
                _activeTransport = _transports[i];
                _activeIndex = i;
                WireEvents(_activeTransport);
                return;
            }
            catch (Exception ex) when (i < _transports.Length - 1)
            {
                _logger?.LogWarning(ex, "服务端传输 {Type} 启动失败，降级到下一传输",
                    _transports[i].GetType().Name);
            }
        }
        throw new InvalidOperationException("所有服务端传输方式均启动失败");
    }

    /// <summary>
    /// 运行时降级 — 当前传输异常时，启动下一优先级传输
    /// </summary>
    private async Task OnActiveTransportErrorAsync(Exception ex)
    {
        if (_activeIndex >= _transports.Length - 1) return; // 已是最后一个

        _logger?.LogWarning(ex, "服务端传输 {Type} 运行时异常，降级到 {NextType}",
            _transports[_activeIndex].GetType().Name,
            _transports[_activeIndex + 1].GetType().Name);

        // 停止当前传输
        await _activeTransport!.StopAsync(CancellationToken.None).ConfigureAwait(false);
        UnwireEvents(_activeTransport);

        // 启动下一传输
        var nextIndex = _activeIndex + 1;
        try
        {
            await _transports[nextIndex].StartAsync(CancellationToken.None).ConfigureAwait(false);
            _activeTransport = _transports[nextIndex];
            _activeIndex = nextIndex;
            WireEvents(_activeTransport);

            FallbackOccurred?.Invoke(this, new TransportFallbackEventArgs
            {
                FromTransportType = _transports[_activeIndex - 1].GetType().Name,
                ToTransportType = _transports[nextIndex].GetType().Name,
                Reason = ex.Message,
                IsServerSide = true
            });
        }
        catch (Exception fallbackEx)
        {
            _logger?.LogError(fallbackEx, "降级到 {Type} 也失败", _transports[nextIndex].GetType().Name);
        }
    }
}
```

**位置**：
- 客户端：`services/Mcp/src/Transports/Shared/McpTransportFallbackChain.cs`
- 服务端：`services/Mcp/src/Transports/Shared/McpServerTransportFallbackChain.cs`

---

### L4：日志追踪

**目标**：每次 fallback 都有完整日志，可追溯降级路径。

**日志事件**：
| 事件 | 级别 | 内容 |
|------|------|------|
| 健康检查失败 | Warning | 传输类型 + 失败原因 + 检查耗时 |
| 连接失败 | Warning | 传输类型 + 异常 + 超时值 + 是第几个尝试 |
| 连接成功 | Information | 传输类型 + 是第几个尝试 + 总耗时 |
| 运行时降级 | Warning | 从哪个传输 + 降到哪个 + 异常原因 |
| 熔断器打开 | Warning | 传输类型 + 连续失败次数 + 冷却期 |
| 熔断器半开 | Information | 传输类型 + 冷却期结束 + 尝试探测 |
| 熔断器关闭 | Information | 传输类型 + 探测成功 + 恢复正常 |
| 全部失败 | Error | 尝试总数 + 熔断跳过数 + 每个失败原因 |

---

### L5：配置开关

**目标**：fallback 链可通过配置和环境变量控制。

```csharp
public sealed class TransportFallbackConfig
{
    // 见 L2 定义

    /// <summary>环境变量：JCC_TRANSPORT_FALLBACK=0 禁用整个 fallback 链</summary>
    public static TransportFallbackConfig FromEnvironment()
    {
        var envDisable = Environment.GetEnvironmentVariable("JCC_TRANSPORT_FALLBACK");
        var envCircuitDisable = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CIRCUIT_BREAKER");
        var envTimeout = Environment.GetEnvironmentVariable("JCC_TRANSPORT_CONNECT_TIMEOUT_MS");

        return new TransportFallbackConfig
        {
            Enabled = envDisable != "0",
            CircuitBreakerEnabled = envCircuitDisable != "0",
            ConnectTimeoutMs = envTimeout is not null && int.TryParse(envTimeout, out var t) ? t : 5000,
        };
    }
}
```

**环境变量**：
| 变量 | 默认值 | 说明 |
|------|--------|------|
| `JCC_TRANSPORT_FALLBACK` | `1` | `0` 禁用整个 fallback 链 |
| `JCC_TRANSPORT_CIRCUIT_BREAKER` | `1` | `0` 禁用熔断器 |
| `JCC_TRANSPORT_CONNECT_TIMEOUT_MS` | `5000` | 单传输连接超时 |
| `JCC_TRANSPORT_CHAIN_TIMEOUT_MS` | `30000` | 整个链总超时 |
| `JCC_TRANSPORT_HEALTH_CHECK` | `1` | `0` 禁用健康检查 |

---

### L6：降级事件通知

**目标**：调用方可订阅 fallback 事件，用于 UI 提示、审计日志等。

```csharp
public sealed class TransportFallbackEventArgs : EventArgs
{
    public required string FromTransportType { get; init; }
    public required string ToTransportType { get; init; }
    public required string Reason { get; init; }
    public required bool IsServerSide { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public int FromPriority { get; init; }
    public int ToPriority { get; init; }
}
```

**订阅方式**：
```csharp
var chain = new McpTransportFallbackChain(transports, config);
chain.FallbackOccurred += (_, e) =>
    Console.WriteLine($"传输降级: {e.FromTransportType} → {e.ToTransportType} ({e.Reason})");
```

---

### L7：指标计数

**目标**：记录 fallback 频率、成功率、延迟等指标。

```csharp
public sealed class TransportFallbackMetrics
{
    private int[] _connectionAttempts;  // 每个传输的尝试次数
    private int[] _connectionSuccesses; // 每个传输的成功次数
    private int[] _connectionFailures;  // 每个传输的失败次数
    private int _totalFallbacks;        // 总降级次数
    private long _totalFallbackDurationMs;

    public void RecordConnection(int transportIndex) { ... }
    public void RecordFailure(int transportIndex) { ... }
    public void RecordFallback(int fromIndex, int toIndex, long durationMs) { ... }

    /// <summary>生成指标快照</summary>
    public TransportFallbackMetricsSnapshot GetSnapshot() { ... }
}

public sealed class TransportFallbackMetricsSnapshot
{
    public required int[] ConnectionAttempts { get; init; }
    public required int[] ConnectionSuccesses { get; init; }
    public required int[] ConnectionFailures { get; init; }
    public required int TotalFallbacks { get; init; }
    public required double AverageFallbackDurationMs { get; init; }
    public required DateTimeOffset SnapshotTime { get; init; }
}
```

**位置**：`services/Mcp/src/Transports/Shared/TransportFallbackMetrics.cs`

---

### L8：断连后 fallback

**目标**：运行中传输断连时，先尝试 fallback 到下一传输，而非立即重连同一传输。

```csharp
/// <summary>
/// 客户端断连后 fallback 逻辑 — 在 McpTransportFallbackChain 中
/// </summary>
private async Task OnActiveTransportConnectionLostAsync(Exception ex)
{
    _logger?.LogWarning(ex, "传输 {Type} 断连，尝试 fallback 到下一传输",
        _activeTransport!.GetType().Name);

    // 先尝试 fallback 到下一传输
    if (_activeIndex < _transports.Length - 1)
    {
        var nextIndex = FindNextAvailableTransport(_activeIndex + 1);
        if (nextIndex >= 0)
        {
            try
            {
                await _transports[nextIndex].StartAsync(CancellationToken.None).ConfigureAwait(false);
                SwitchActiveTransport(nextIndex);
                FallbackOccurred?.Invoke(this, new TransportFallbackEventArgs { ... });
                return; // fallback 成功
            }
            catch (Exception fallbackEx)
            {
                _logger?.LogWarning(fallbackEx, "Fallback 到 {Type} 失败", _transports[nextIndex].GetType().Name);
            }
        }
    }

    // fallback 失败或无下一传输 → 重连当前传输（原有重连逻辑）
    await ReconnectCurrentTransportAsync(ex);
}

/// <summary>找下一个非熔断的传输</summary>
private int FindNextAvailableTransport(int startIndex)
{
    for (var i = startIndex; i < _transports.Length; i++)
    {
        if (!_circuitBreakers[i].IsOpen) return i;
    }
    return -1;
}
```

---

### L9：熔断器

**目标**：某传输连续失败 N 次后跳过，冷却期后允许探测（半开状态）。

```csharp
/// <summary>
/// 传输熔断器 — 三态：Closed(正常) → Open(熔断) → HalfOpen(探测) → Closed/Open
/// </summary>
public sealed class TransportCircuitBreaker
{
    private readonly int _failureThreshold;     // 连续失败阈值（默认 3）
    private readonly TimeSpan _coolDownPeriod;  // 冷却期（默认 30s）
    private int _consecutiveFailures;
    private DateTimeOffset _openedAt;
    private CircuitBreakerState _state = CircuitBreakerState.Closed;

    public CircuitBreakerState State => GetCurrentState();
    public bool IsOpen => State == CircuitBreakerState.Open;

    public void RecordSuccess()
    {
        _consecutiveFailures = 0;
        _state = CircuitBreakerState.Closed;
    }

    public void RecordFailure()
    {
        _consecutiveFailures++;
        if (_consecutiveFailures >= _failureThreshold)
        {
            _state = CircuitBreakerState.Open;
            _openedAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>探测是否允许尝试（半开状态允许一次探测）</summary>
    public bool TryProbe()
    {
        if (_state != CircuitBreakerState.Open) return true;
        if (DateTimeOffset.UtcNow - _openedAt >= _coolDownPeriod)
        {
            _state = CircuitBreakerState.HalfOpen;
            return true; // 允许一次探测
        }
        return false; // 冷却期未过，拒绝
    }

    private CircuitBreakerState GetCurrentState()
    {
        if (_state == CircuitBreakerState.Open &&
            DateTimeOffset.UtcNow - _openedAt >= _coolDownPeriod)
        {
            return CircuitBreakerState.HalfOpen;
        }
        return _state;
    }
}

public enum CircuitBreakerState
{
    Closed,    // 正常：允许请求
    Open,      // 熔断：拒绝请求
    HalfOpen   // 半开：允许一次探测
}
```

**配置**：
| 参数 | 默认值 | 环境变量 |
|------|--------|----------|
| 失败阈值 | 3 | `JCC_TRANSPORT_CB_THRESHOLD` |
| 冷却期 | 30s | `JCC_TRANSPORT_CB_COOLDOWN_MS` |

**位置**：`infrastructure/Transport.Impl/src/Shared/TransportCircuitBreaker.cs`

---

### L10：遥测报告

**目标**：定期或按需生成 fallback 链健康报告，供运维和诊断。

```csharp
/// <summary>
/// 传输 fallback 链遥测报告
/// </summary>
public sealed class TransportFallbackTelemetry
{
    private readonly McpTransportFallbackChain _chain;
    private readonly TransportFallbackMetrics _metrics;
    private readonly TransportCircuitBreaker[] _circuitBreakers;

    /// <summary>生成完整遥测报告</summary>
    public TransportFallbackReport GenerateReport()
    {
        var metrics = _metrics.GetSnapshot();
        var circuitStates = _circuitBreakers.Select(cb => new CircuitBreakerReport
        {
            State = cb.State,
            ConsecutiveFailures = cb.ConsecutiveFailures,
            FailureThreshold = cb.FailureThreshold,
            CoolDownPeriod = cb.CoolDownPeriod,
            OpenedAt = cb.OpenedAt
        }).ToArray();

        return new TransportFallbackReport
        {
            ActiveTransportType = _chain.ActiveTransportType,
            ActiveTransportIndex = _chain.ActiveTransportIndex,
            Metrics = metrics,
            CircuitBreakers = circuitStates,
            Config = _chain.Config,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>格式化为可读文本（用于 /doctor 命令）</summary>
    public string FormatReport() { ... }
}

public sealed class TransportFallbackReport
{
    public required string? ActiveTransportType { get; init; }
    public required int ActiveTransportIndex { get; init; }
    public required TransportFallbackMetricsSnapshot Metrics { get; init; }
    public required CircuitBreakerReport[] CircuitBreakers { get; init; }
    public required TransportFallbackConfig Config { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
}
```

**集成点**：
- `DoctorModeRunner` 输出传输 fallback 遥测报告
- `SessionController` 定期 LogDebug 遥测快照

**位置**：`services/Mcp/src/Transports/Shared/TransportFallbackTelemetry.cs`

---

## 5. 文件清单（新增）

| 文件 | 层级 | 位置 | 说明 |
|------|------|------|------|
| `TransportFallbackConfig.cs` | L2/L5 | `infrastructure/Transport.Impl/src/Shared/` | 配置 + 环境变量 |
| `TransportHealthCheck.cs` | L1 | `infrastructure/Transport.Impl/src/Shared/` | 健康检查接口 + 实现 |
| `TransportCircuitBreaker.cs` | L9 | `infrastructure/Transport.Impl/src/Shared/` | 熔断器 |
| `McpTransportFallbackChain.cs` | L3/L4/L6/L8 | `services/Mcp/src/Transports/Shared/` | 客户端 fallback 链 |
| `McpServerTransportFallbackChain.cs` | L3/L4/L6 | `services/Mcp/src/Transports/Shared/` | 服务端 fallback 链 |
| `TransportFallbackMetrics.cs` | L7 | `services/Mcp/src/Transports/Shared/` | 指标计数 |
| `TransportFallbackTelemetry.cs` | L10 | `services/Mcp/src/Transports/Shared/` | 遥测报告 |
| `TransportFallbackEventArgs.cs` | L6 | `services/Mcp/src/Transports/Shared/` | 降级事件参数 |

## 6. 修改清单（已有文件）

| 文件 | 修改 | 原因 |
|------|------|------|
| `McpClientFactory.cs` | `CreateClient` 支持 fallback 链模式 | L3 客户端入口 |
| `McpClientToolHandlers.cs` | `mcp_connect` 传输选择逻辑适配 | L3 客户端入口 |
| `McpNetworkClient.cs` | `OnTransportError` → 触发 fallback | L8 断连后 fallback |
| `DoctorModeRunner.cs` | 输出 fallback 遥测报告 | L10 集成 |
| `SessionController.cs` | LogDebug 遥测快照 | L10 集成 |
| `ServiceRegistration.cs` (Mcp) | DI 注册 fallback 相关服务 | L5 配置 |

---

## 7. 实现顺序

| 步骤 | 内容 | 依赖 |
|------|------|------|
| 1 | `TransportFallbackConfig` + `TransportFallbackEventArgs` | 无 |
| 2 | `TransportHealthCheck` | 无 |
| 3 | `TransportCircuitBreaker` | 无 |
| 4 | `TransportFallbackMetrics` | 无 |
| 5 | `McpTransportFallbackChain`（客户端 L3/L4/L6/L8） | 步骤 1-4 |
| 6 | `McpServerTransportFallbackChain`（服务端 L3/L4/L6） | 步骤 1-4 |
| 7 | `TransportFallbackTelemetry`（L10） | 步骤 5-6 |
| 8 | `McpClientFactory` + `McpClientToolHandlers` 适配 | 步骤 5 |
| 9 | `DoctorModeRunner` + `SessionController` 集成 | 步骤 7 |
| 10 | DI 注册 | 步骤 8-9 |

---

## 8. 已实现的纵深防御机制（保留）

### 8.1 重连策略

| 传输 | 策略 | 参数 | 代码位置 |
|------|------|------|----------|
| SSE (Agent) | 指数退避 | MaxReconnectAttempts=3 | `SseAgentTransport.cs` |
| SSE (MCP Client) | 指数退避 | MaxAttempts=5 | `SseClientTransport.cs` |
| SSE (Bridge) | 固定延迟 | - | `SseBridgeTransport.cs` |
| V1 Bridge (WS) | 指数退避 | 10分钟预算 | `V1ReplBridgeTransport.cs` |
| V2 Bridge (SSE) | 永久拒绝码检测 | 401/403/404 | `V2ReplBridgeTransport.cs` |

### 8.2 认证防御

| 传输 | 机制 | 触发条件 | 代码位置 |
|------|------|----------|----------|
| V1 Bridge | OAuth Token 刷新 | WS 关闭码 4003 | `V1ReplBridgeTransport.cs` |
| V2 Bridge | 心跳认证失败检测 | 心跳 401 | `V2ReplBridgeTransport.cs` |
| MCP HTTP/SSE | Step-Up 认证升级 | 403 + insufficient_scope | `StepUpDetector.cs` |

### 8.3 协议防御

| 传输 | 机制 | 触发条件 | 代码位置 |
|------|------|----------|----------|
| V2 Bridge | Epoch 冲突处理 | 409 Conflict | `V2ReplBridgeTransport.cs` |
| MCP HTTP | 会话重建 | 404 响应 | `HttpTransport.cs` |
| ConnectionManager | 协议切换 | 显式调用 | `ConnectionManager.cs` |

### 8.4 进程防御

| 传输 | 机制 | 代码位置 |
|------|------|----------|
| V1 Bridge | 系统休眠检测 | `V1ReplBridgeTransport.cs` |
| V1 Bridge Uploader | 批次丢弃(maxConsecutiveFailures) | `SerialBatchEventUploader.cs` |

### 8.5 E2E 超时纵深防御（L1-L10，已完成）

| 层级 | 机制 | 状态 |
|------|------|------|
| L1 | CI 环境变量 `JCC_API_TIMEOUT_MS=30000` | ✅ |
| L2 | `SessionTurnResult.Timeout(timeoutMs)` + 显示实际超时值 | ✅ |
| L3 | `CoverageTestBase.RunScriptAsync` 加 provider 参数 | ✅ |
| L4 | `DualRoleConversationRunner` 超时消息含 turn/outputLen/provider | ✅ |
| L5 | `StdioProcessManager` 超时消息含 `_pidTag` + `IsRunning` | ✅ |
| L6 | MockServer 健康检查超时含供应商+进程状态 | ✅ |
| L7 | jcc.exe 就绪检查每10s进度日志含轮询数+stderrLen+provider | ✅ |
| L9 | 超时后诊断快照（stderr尾部+进程状态+MockServer状态） | ✅ |
| L10 | SessionController 首次请求 LogDebug 记录 API 超时配置 | ✅ |

---

## 9. 决策记录

<!-- 🤖 Auto Decision: 2026-07-31 -->
<!-- 决策: Fallback 链优先级: Stdio > StreamableHTTP > SSE > WebSocket -->
<!-- 原因: 本地优先(零网络) > 流式优先(服务端推送) > 轻量优先(无全双工开销), 对齐MCP规范传输优先级 -->
<!-- 替代方案: WebSocket优先(SSE降级) — 不采用,因为WS握手开销更大且MCP规范已将StreamableHTTP定为新版标准 -->

<!-- 🤖 Auto Decision: 2026-08-01 -->
<!-- 决策: 服务端 fallback = 首选+运行时降级, 客户端 = 连接+断连+熔断 -->
<!-- 原因: 服务端同一时刻只需一种传输在服务; 客户端需要最完整防御 -->
<!-- 替代方案: 服务端多传输并行监听 — 不采用,增加复杂度且MCP规范不要求 -->

<!-- 🤖 Auto Decision: 2026-08-01 -->
<!-- 决策: Stdio 有command才加入链 -->
<!-- 原因: Stdio需要本地子进程,无command时尝试无意义且浪费时间 -->
<!-- 替代方案: Stdio始终在链首失败快速跳过 — 不采用,不如直接不加入干净 -->

<!-- 🤖 Auto Decision: 2026-08-01 -->
<!-- 决策: 纵深防御 L1-L10 完整层 -->
<!-- 原因: 用户选择"连接+断连+熔断"需要到L9,加上L10遥测形成完整体系 -->
<!-- 替代方案: L1-L8增强层 — 不采用,缺少熔断器会导致反复尝试已知失败的传输 -->

<!-- 🤖 Auto Decision: 2026-08-01 -->
<!-- 决策: L1健康检查必须检测沙箱/OS拦截,不能只做TCP端口检查 -->
<!-- 原因: 实战踩坑: C# HttpListener被沙箱策略拦截导致SSE完全失效,换成TcpListener才好 -->
<!-- 检测方法: 尝试短暂启动监听器(100ms),捕获HttpListenerException等权限异常 -->
<!-- 不可用分类: NetworkUnreachable / SandboxBlocked / ConfigMissing / PortConflict / DependencyMissing -->
<!-- 替代方案: 仅TCP端口检查 — 不采用,无法检测HttpListener被沙箱拦截的场景 -->
