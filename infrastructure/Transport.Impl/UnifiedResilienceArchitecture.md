# 统一韧性架构设计：覆盖全通讯点的纵深防御

> 本文档定义覆盖 jcc 系统所有通讯点的统一韧性架构，替代原先仅覆盖 MCP 传输的 fallback 链设计。
> 原设计文档 `TransportFallbackChain.md` 保留作为 MCP 传输级 fallback 的详细参考，本设计在其基础上扩展为全系统韧性层。

---

## 0. 问题诊断

### 0.1 当前韧性覆盖审计

| # | 通讯点 | 传输 | 代码位置 | 超时 | 重试 | 熔断 | Fallback | 进程健康检查 |
|---|--------|------|----------|------|------|------|----------|-------------|
| 1 | **LLM API 调用** | HTTP | `OpenAIQueryService` → `IHttpClientProvider` | ⚠️ 仅流式看门狗 | ❌ | ❌ | ⚠️ 流式→非流式 | N/A |
| 2 | **MCP 客户端→服务器** | HTTP/SSE/WS/Stdio | `McpClientFactory` → `McpFallbackClient` | ✅ | ✅ | ✅ | ✅ | ❌ |
| 3 | **Bridge WebSocket** | WebSocket | `BridgeServer` (HttpListener) | ❌ | ❌ | ❌ | ❌ | N/A |
| 4 | **Bridge 子进程** | Stdio(NDJSON) | `BridgeSubprocessHandle` | ❌ | ❌ | ❌ | ❌ | ❌ |
| 5 | **Doctor 子进程** | Stdio | `PatientProcessManager` | ❌ | ❌ | ❌ | ❌ | ❌ |
| 6 | **Sandbox 卫星进程** | Stdio | `SandboxSatelliteHost` | ❌ | ❌ | ❌ | ❌ | ❌ |
| 7 | **MCP OAuth/Auth** | HTTP | `McpOAuthService` | ❌ | ❌ | ❌ | ❌ | N/A |
| 8 | **MCP 官方注册表** | HTTP | `McpOfficialRegistry` | ❌ | ❌ | ❌ | ❌ | N/A |
| 9 | **Voice 服务** | HTTP | `VoiceService` | ❌ | ❌ | ❌ | ❌ | N/A |
| 10 | **Named Pipe** | Pipe | `PipeQueryService` | ❌ | ❌ | ❌ | ❌ | ❌ |

**结论：10 个通讯点中仅 1 个完整覆盖（#2），1 个部分覆盖（#1），8 个完全裸奔。**

### 0.2 原设计问题

| 问题 | 说明 |
|------|------|
| **作用域太窄** | 仅覆盖 MCP 传输协议级 fallback，不是"所有通讯的韧性" |
| **缺少两 exe 通讯模型** | Stdio 子进程通讯（#4/#5/#6）是进程级双工，进程可挂死/崩溃/管道断裂，需要进程健康检查+超时杀进程+重启，不是传输协议 fallback |
| **韧性组件碎片化** | `CircuitBreakerState`(Infrastructure) + `TransportCircuitBreaker`(Transport.Impl) + `FixedCircuitBreakerMiddleware`(Pipeline) 三套熔断器，命名冲突，语义不一致 |
| **HTTP 通讯无统一韧性** | `IHttpClientProvider` 只管 HttpClient 创建，不管超时/重试/熔断；`ApiClient` 有 RetryPolicy 但其他 HTTP 调用点没有 |

---

## 1. 架构设计

### 1.1 核心原则

| 原则 | 说明 |
|------|------|
| **统一韧性原语** | 一套构建块（超时/重试/熔断/健康检查），适用于所有通讯类型 |
| **通讯类型适配器** | HTTP/Stdio/Pipe/WebSocket 各有薄适配层，应用统一原语 |
| **两 exe 模型** | Stdio 子进程通讯获得特殊处理：进程级健康 + 管道级超时 + 重启能力 |
| **AOT 兼容** | 不依赖 Polly，所有原语手写，支持 NativeAOT |
| **可组合** | 每个通讯点可按需组合原语（如 LLM=超时+重试+熔断，Bridge=超时+进程健康+重启） |
| **环境变量可配** | 所有阈值通过 `JCC_RESILIENCE_*` 环境变量配置 |

### 1.2 分层架构

```
┌─────────────────────────────────────────────────────────┐
│                    业务调用层                            │
│  QueryLoop / McpClientToolHandlers / BridgeMain / ...   │
├─────────────────────────────────────────────────────────┤
│                 韧性编排层（本设计核心）                   │
│  ResiliencePipeline = Timeout → Retry → CircuitBreaker  │
│  SubprocessResilience = ProcessHealth → PipeTimeout      │
│                         → Restart → CircuitBreaker       │
├─────────────────────────────────────────────────────────┤
│              通讯类型适配层                               │
│  ResilientHttpClient  │  ResilientSubprocess             │
│  ResilientPipe        │  ResilientWebSocket              │
├─────────────────────────────────────────────────────────┤
│              基础传输层（已有）                            │
│  IHttpClientProvider  │  IProcessService                 │
│  IMcpTransport        │  NamedPipeClientStream            │
└─────────────────────────────────────────────────────────┘
```

### 1.3 韧性原语定义

```csharp
/// <summary>
/// 统一韧性策略 — 适用于任何通讯点
/// </summary>
public sealed class ResiliencePolicy
{
    /// <summary>通讯点名称（用于日志和遥测）</summary>
    public required string Name { get; init; }

    /// <summary>总超时（含所有重试）</summary>
    public TimeSpan? TotalTimeout { get; init; }

    /// <summary>单次操作超时</summary>
    public TimeSpan? OperationTimeout { get; init; }

    /// <summary>重试配置</summary>
    public RetryConfig? Retry { get; init; }

    /// <summary>熔断器配置</summary>
    public CircuitBreakerConfig? CircuitBreaker { get; init; }

    /// <summary>健康检查配置（仅 Stdio 子进程）</summary>
    public HealthCheckConfig? HealthCheck { get; init; }
}

public sealed class RetryConfig
{
    /// <summary>最大重试次数（默认 3）</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>基础延迟（默认 1s）</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>最大延迟（默认 30s）</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>退避策略</summary>
    public BackoffStrategy Strategy { get; init; } = BackoffStrategy.ExponentialWithJitter;

    /// <summary>可重试的异常类型（默认：超时+网络+5xx）</summary>
    public Func<Exception, bool>? ShouldRetry { get; init; }
}

public enum BackoffStrategy
{
    Fixed,
    Linear,
    Exponential,
    ExponentialWithJitter
}

public sealed class CircuitBreakerConfig
{
    /// <summary>连续失败阈值（默认 5）</summary>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>熔断持续时间（默认 30s）</summary>
    public TimeSpan OpenDuration { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>半开状态允许的探测请求数（默认 1）</summary>
    public int HalfOpenMaxProbe { get; init; } = 1;
}

public sealed class HealthCheckConfig
{
    /// <summary>健康检查间隔（默认 10s）</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>单次健康检查超时（默认 5s）</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>连续失败后认为进程不健康（默认 3）</summary>
    public int FailureThreshold { get; init; } = 3;

    /// <summary>进程不健康时的动作</summary>
    public UnhealthyAction Action { get; init; } = UnhealthyAction.KillAndRestart;
}

public enum UnhealthyAction
{
    /// <summary>仅记录日志，不采取行动</summary>
    LogOnly,

    /// <summary>杀死进程，不重启</summary>
    Kill,

    /// <summary>杀死进程并重启</summary>
    KillAndRestart
}
```

---

## 2. 通讯点韧性方案

### 2.1 LLM API 调用（#1）— 最关键

**现状**：`OpenAIQueryService` 通过 `IHttpClientProvider` 发 HTTP，有 `StreamingFallbackDecorator`（流式→非流式），但无 HTTP 级重试/熔断。

**方案**：在 `IHttpClientProvider` 层加韧性，所有 HTTP 调用点自动受益。

```csharp
/// <summary>
/// 韧性 HTTP 客户端提供者 — 包装 IHttpClientProvider，添加超时+重试+熔断
/// 所有通过 IHttpClientProvider.GetClient() 的调用自动获得韧性保护
/// </summary>
public sealed class ResilientHttpClientProvider : IHttpClientProvider
{
    private readonly IHttpClientProvider _inner;
    private readonly ResiliencePolicy _policy;
    private readonly UnifiedCircuitBreaker _circuitBreaker;

    public HttpClient GetClient() => WrapWithResilience(_inner.GetClient());
    public HttpClient GetClient(string name) => WrapWithResilience(_inner.GetClient(name));
}
```

**韧性策略**：

| 原语 | 配置 | 环境变量 |
|------|------|----------|
| 操作超时 | 30s | `JCC_RESILIENCE_LLM_TIMEOUT_MS` (默认 30000) |
| 重试 | 3次，指数退避(1s/2s/4s)+抖动 | `JCC_RESILIENCE_LLM_MAX_RETRIES` (默认 3) |
| 熔断 | 5次失败→30s熔断 | `JCC_RESILIENCE_LLM_CB_THRESHOLD` (默认 5) |
| 流式fallback | 已有 `StreamingFallbackDecorator` | 保持不变 |

**可重试条件**：
- `TaskCanceledException`（超时）
- `HttpRequestException`（网络错误）
- HTTP 429（限流，尊重 Retry-After）
- HTTP 5xx（服务端错误）
- **不重试**：HTTP 4xx（客户端错误，除 429）、`OperationCanceledException`（用户取消）

**接入点**：DI 注册时用 `ResilientHttpClientProvider` 包装 `DefaultHttpClientProvider`。

---

### 2.2 MCP 客户端→服务器（#2）— 已有，需统一

**现状**：`McpTransportFallbackChain` + `TransportCircuitBreaker` 已实现传输级 fallback。

**方案**：保留 MCP 传输级 fallback 链不变，但将 `TransportCircuitBreaker` 替换为 `UnifiedCircuitBreaker`（统一熔断器），消除碎片化。

**额外增强**：MCP 客户端的 HTTP 调用也走 `ResilientHttpClientProvider`，获得 HTTP 级重试+熔断（与传输级 fallback 是两层防御）。

---

### 2.3 Bridge WebSocket（#3）

**现状**：`BridgeServer` 用 `HttpListener` + `WebSocket`，无任何韧性。

**方案**：WebSocket 连接级韧性。

| 原语 | 配置 | 说明 |
|------|------|------|
| 连接超时 | 10s | WebSocket 握手超时 |
| 读超时 | 30s | 单条消息读取超时（防对端挂死） |
| 重连 | 3次，指数退避 | 客户端断连后自动重连 |
| 心跳 | 15s ping/pong | 检测连接存活 |

**接入点**：`BridgeServer` 的 WebSocket 接受循环 + 客户端 `ClientWebSocket` 连接。

---

### 2.4 Bridge 子进程（#4）— 两 exe 模型核心

**现状**：`BridgeSubprocessHandle` 通过 `IInteractiveProcess` 通讯，无超时/健康检查/重启。

**方案**：**两 exe 韧性模型** — 进程级健康 + 管道级超时 + 重启。

```csharp
/// <summary>
/// 韧性子进程 — 包装 IInteractiveProcess，添加进程级健康检查+管道级超时+重启
/// 适用于所有两 exe 通讯场景（Bridge/Doctor/Sandbox）
/// </summary>
public sealed class ResilientSubprocess : IAsyncDisposable
{
    private readonly IInteractiveProcess _process;
    private readonly SubprocessResiliencePolicy _policy;
    private readonly ProcessHealthMonitor _healthMonitor;
    private readonly ProcessRestartManager _restartManager;

    /// <summary>带超时的 stdin 写入</summary>
    public async Task WriteStdinAsync(string data, CancellationToken ct = default);

    /// <summary>带超时的 stdout 读取</summary>
    public async Task<string?> ReadStdoutLineAsync(CancellationToken ct = default);

    /// <summary>进程是否健康（健康检查结果）</summary>
    public bool IsHealthy => _healthMonitor.IsHealthy;

    /// <summary>重启进程（杀死当前+启动新进程）</summary>
    public async Task RestartAsync(CancellationToken ct = default);
}
```

**两 exe 韧性策略**：

| 层 | 原语 | 配置 | 说明 |
|----|------|------|------|
| **进程级** | 存活检查 | 5s 间隔 | 检查 `HasExited`，进程崩溃立即感知 |
| **进程级** | 响应性检查 | 10s 间隔 | 发 ping → 等 pong，检测进程挂死（活着但不响应） |
| **进程级** | 自动重启 | 最多 3 次 | 进程不健康时 Kill + 重新 Spawn |
| **管道级** | 写超时 | 10s | `WriteStdinAsync` 超时→进程可能挂死 |
| **管道级** | 读超时 | 30s | `ReadStdoutLineAsync` 超时→进程可能挂死 |
| **管道级** | 熔断 | 5次失败→60s熔断 | 连续管道操作失败→停止尝试通讯 |

**环境变量**：

| 变量 | 默认 | 说明 |
|------|------|------|
| `JCC_RESILIENCE_SUBPROCESS_WRITE_TIMEOUT_MS` | 10000 | stdin 写超时 |
| `JCC_RESILIENCE_SUBPROCESS_READ_TIMEOUT_MS` | 30000 | stdout 读超时 |
| `JCC_RESILIENCE_SUBPROCESS_HEALTH_INTERVAL_MS` | 5000 | 健康检查间隔 |
| `JCC_RESILIENCE_SUBPROCESS_MAX_RESTARTS` | 3 | 最大重启次数 |
| `JCC_RESILIENCE_SUBPROCESS_CB_THRESHOLD` | 5 | 熔断失败阈值 |

**接入点**：`BridgeSubprocessHandle.CreateAsync` 内部用 `ResilientSubprocess` 包装 `IInteractiveProcess`。

---

### 2.5 Doctor 子进程（#5）— 同两 exe 模型

**方案**：复用 `ResilientSubprocess`，配置与 Bridge 子进程相同。

**接入点**：`PatientProcessManager` 内部用 `ResilientSubprocess`。

---

### 2.6 Sandbox 卫星进程（#6）— 同两 exe 模型

**方案**：复用 `ResilientSubprocess`，但 **UnhealthyAction = Kill**（不重启，沙箱进程不重启）。

**接入点**：`SandboxSatelliteHost` 内部用 `ResilientSubprocess`。

---

### 2.7 MCP OAuth/Auth HTTP（#7）

**方案**：走 `ResilientHttpClientProvider`，自动获得 HTTP 级超时+重试+熔断。

**特殊**：OAuth 重试需排除 401/403（认证失败不应重试）。

---

### 2.8 MCP 官方注册表 HTTP（#8）

**方案**：走 `ResilientHttpClientProvider`，标准 HTTP 韧性策略。

---

### 2.9 Voice 服务 HTTP（#9）

**方案**：走 `ResilientHttpClientProvider`，标准 HTTP 韧性策略。

---

### 2.10 Named Pipe（#10）

**方案**：Pipe 级韧性。

| 原语 | 配置 | 说明 |
|------|------|------|
| 连接超时 | 5s | `NamedPipeClientStream` 连接超时 |
| 读超时 | 30s | 单条消息读取超时 |
| 写超时 | 10s | 单条消息写入超时 |
| 重连 | 3次，指数退避 | Pipe 断开后自动重连 |

**接入点**：`PipeQueryService` 的 `ConnectCallback` 内部。

---

## 3. 统一熔断器（消除碎片化）

### 3.1 现状

| 类型 | 命名空间 | 位置 | 使用者 |
|------|----------|------|--------|
| `CircuitBreakerState` | `Core.Utils` | Infrastructure/Utils/Throttle | `FixedCircuitBreakerMiddleware` |
| `TransportCircuitBreaker` | `JoinCode.Transport` | Transport.Impl/Shared | MCP fallback chain |
| `FixedCircuitBreakerMiddleware<T>` | Infrastructure.Pipeline | Infrastructure/Pipeline | Web pipeline |

**问题**：三套熔断器，命名冲突（`CircuitBreakerState` 在两个命名空间），语义不一致。

### 3.2 统一方案

```csharp
/// <summary>
/// 统一熔断器 — 替代 CircuitBreakerState + TransportCircuitBreaker
/// 三态：Closed（正常）→ Open（熔断）→ HalfOpen（探测）
/// </summary>
public sealed class UnifiedCircuitBreaker
{
    public string Name { get; }
    public CircuitBreakerState State { get; }
    public int ConsecutiveFailures { get; }
    public int TotalFailures { get; }
    public int TotalSuccesses { get; }
    public DateTimeOffset? OpenedAt { get; }

    public bool TryProbe();           // 是否允许请求通过
    public void RecordSuccess();      // 记录成功
    public void RecordFailure();      // 记录失败
    public void Reset();              // 手动重置
}
```

**迁移计划**：

| 旧类型 | 迁移到 | 说明 |
|--------|--------|------|
| `Core.Utils.CircuitBreakerState` | `UnifiedCircuitBreaker` | `FixedCircuitBreakerMiddleware` 改用 `UnifiedCircuitBreaker` |
| `JoinCode.Transport.CircuitBreakerState`(enum) | 保留 enum，重命名为 `CircuitBreakerPhase` | 避免与类名冲突 |
| `TransportCircuitBreaker` | `UnifiedCircuitBreaker` | MCP fallback chain 改用 `UnifiedCircuitBreaker` |

**位置**：`infrastructure/Infrastructure/Utils/Resilience/UnifiedCircuitBreaker.cs`

---

## 4. 韧性编排器

### 4.1 HTTP 韧性

```csharp
/// <summary>
/// 韧性 HTTP 执行器 — 在 HttpClient.SendAsync 外层包装超时+重试+熔断
/// 不修改 HttpClient 本身，通过装饰器模式添加韧性
/// </summary>
public sealed class ResilientHttpExecutor
{
    private readonly ResiliencePolicy _policy;
    private readonly UnifiedCircuitBreaker _circuitBreaker;

    public async Task<HttpResponseMessage> ExecuteAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> operation,
        string operationName,
        CancellationToken ct = default);
}
```

**执行流程**：

```
1. 检查熔断器 → Open 则抛 CircuitBreakerOpenException
2. 创建总超时 CancellationTokenSource
3. 循环重试：
   a. 创建单次操作超时 CancellationTokenSource
   b. 执行 operation(ct)
   c. 成功 → RecordSuccess → 返回
   d. 失败 → 判断 ShouldRetry → 是则退避等待 → 否则 RecordFailure → 抛出
4. 重试耗尽 → RecordFailure → 抛出
```

### 4.2 子进程韧性

```csharp
/// <summary>
/// 子进程健康监控器 — 定期检查进程存活和响应性
/// </summary>
public sealed class ProcessHealthMonitor : IDisposable
{
    /// <summary>进程是否健康</summary>
    public bool IsHealthy { get; }

    /// <summary>最近一次健康检查时间</summary>
    public DateTimeOffset? LastCheckTime { get; }

    /// <summary>连续健康检查失败次数</summary>
    public int ConsecutiveFailures { get; }

    /// <summary>进程变为不健康时触发</summary>
    public event EventHandler<ProcessUnhealthyEventArgs>? Unhealthy;
}

/// <summary>
/// 进程重启管理器 — 管理进程的杀死+重启循环
/// </summary>
public sealed class ProcessRestartManager
{
    /// <summary>当前重启次数</summary>
    public int RestartCount { get; }

    /// <summary>最大重启次数</summary>
    public int MaxRestarts { get; }

    /// <summary>重启进程</summary>
    public async Task<IInteractiveProcess> RestartAsync(
        IInteractiveProcess currentProcess,
        Func<CancellationToken, Task<IInteractiveProcess>> spawnFunc,
        CancellationToken ct = default);
}
```

---

## 5. 遥测和诊断

### 5.1 统一韧性遥测

```csharp
/// <summary>
/// 韧性遥测报告 — 覆盖所有通讯点的韧性状态
/// </summary>
public sealed class ResilienceTelemetryReport
{
    /// <summary>HTTP 韧性状态（按通讯点名称索引）</summary>
    public Dictionary<string, HttpResilienceStatus> HttpEndpoints { get; }

    /// <summary>子进程韧性状态（按进程名称索引）</summary>
    public Dictionary<string, SubprocessResilienceStatus> Subprocesses { get; }

    /// <summary>Pipe 韧性状态</summary>
    public Dictionary<string, PipeResilienceStatus> Pipes { get; }

    /// <summary>WebSocket 韧性状态</summary>
    public Dictionary<string, WebSocketResilienceStatus> WebSockets { get; }
}

public sealed class HttpResilienceStatus
{
    public required string Name { get; init; }
    public required CircuitBreakerPhase CircuitBreakerState { get; init; }
    public required int TotalRequests { get; init; }
    public required int FailedRequests { get; init; }
    public required int RetriedRequests { get; init; }
    public required int CircuitBreakerTrips { get; init; }
    public required TimeSpan? LastFailureTime { get; init; }
}
```

### 5.2 诊断命令

`jcc doctor --resilience` 输出所有通讯点的韧性状态摘要。

---

## 6. 环境变量汇总

| 变量 | 默认 | 适用 | 说明 |
|------|------|------|------|
| `JCC_RESILIENCE_ENABLED` | 1 | 全局 | 是否启用韧性层（0=全部禁用，裸奔） |
| `JCC_RESILIENCE_LLM_TIMEOUT_MS` | 30000 | #1 | LLM API 单次操作超时 |
| `JCC_RESILIENCE_LLM_MAX_RETRIES` | 3 | #1 | LLM API 最大重试次数 |
| `JCC_RESILIENCE_LLM_CB_THRESHOLD` | 5 | #1 | LLM API 熔断失败阈值 |
| `JCC_RESILIENCE_LLM_CB_OPEN_MS` | 30000 | #1 | LLM API 熔断持续时间 |
| `JCC_RESILIENCE_HTTP_TIMEOUT_MS` | 15000 | #7/#8/#9 | 通用 HTTP 操作超时 |
| `JCC_RESILIENCE_HTTP_MAX_RETRIES` | 2 | #7/#8/#9 | 通用 HTTP 最大重试次数 |
| `JCC_RESILIENCE_HTTP_CB_THRESHOLD` | 5 | #7/#8/#9 | 通用 HTTP 熔断失败阈值 |
| `JCC_RESILIENCE_SUBPROCESS_WRITE_TIMEOUT_MS` | 10000 | #4/#5/#6 | 子进程 stdin 写超时 |
| `JCC_RESILIENCE_SUBPROCESS_READ_TIMEOUT_MS` | 30000 | #4/#5/#6 | 子进程 stdout 读超时 |
| `JCC_RESILIENCE_SUBPROCESS_HEALTH_INTERVAL_MS` | 5000 | #4/#5/#6 | 子进程健康检查间隔 |
| `JCC_RESILIENCE_SUBPROCESS_MAX_RESTARTS` | 3 | #4/#5 | 子进程最大重启次数 |
| `JCC_RESILIENCE_SUBPROCESS_CB_THRESHOLD` | 5 | #4/#5/#6 | 子进程熔断失败阈值 |
| `JCC_RESILIENCE_PIPE_CONNECT_TIMEOUT_MS` | 5000 | #10 | Pipe 连接超时 |
| `JCC_RESILIENCE_PIPE_READ_TIMEOUT_MS` | 30000 | #10 | Pipe 读超时 |
| `JCC_RESILIENCE_PIPE_MAX_RECONNECTS` | 3 | #10 | Pipe 最大重连次数 |
| `JCC_RESILIENCE_WS_CONNECT_TIMEOUT_MS` | 10000 | #3 | WebSocket 连接超时 |
| `JCC_RESILIENCE_WS_READ_TIMEOUT_MS` | 30000 | #3 | WebSocket 读超时 |
| `JCC_RESILIENCE_WS_PING_INTERVAL_MS` | 15000 | #3 | WebSocket 心跳间隔 |

---

## 7. 实现计划

### Phase 1：统一原语（基础设施层）

| 步骤 | 内容 | 位置 |
|------|------|------|
| 1.1 | `ResiliencePolicy` + `RetryConfig` + `CircuitBreakerConfig` + `HealthCheckConfig` | Infrastructure/Utils/Resilience/ |
| 1.2 | `UnifiedCircuitBreaker`（替代 CircuitBreakerState + TransportCircuitBreaker） | Infrastructure/Utils/Resilience/ |
| 1.3 | `ResilientHttpExecutor`（超时+重试+熔断编排） | Infrastructure/Http/ |
| 1.4 | `ResilientHttpClientProvider`（包装 IHttpClientProvider） | Infrastructure/Http/ |
| 1.5 | 迁移 `FixedCircuitBreakerMiddleware` 使用 `UnifiedCircuitBreaker` | Infrastructure/Pipeline/ |
| 1.6 | 迁移 MCP fallback chain 使用 `UnifiedCircuitBreaker` | Mcp/Transports/ |
| 1.7 | 单元测试 | 对应 tests/ |

### Phase 2：两 exe 模型（子进程韧性）

| 步骤 | 内容 | 位置 |
|------|------|------|
| 2.1 | `ProcessHealthMonitor`（存活+响应性检查） | Infrastructure/Process/ |
| 2.2 | `ProcessRestartManager`（杀死+重启） | Infrastructure/Process/ |
| 2.3 | `ResilientSubprocess`（包装 IInteractiveProcess） | Infrastructure/Process/ |
| 2.4 | `SubprocessResiliencePolicy` | Infrastructure/Process/ |
| 2.5 | 单元测试 | 对应 tests/ |

### Phase 3：业务接入 ✅

| 步骤 | 内容 | 接入点 | 状态 |
|------|------|--------|------|
| 3.1 | LLM API 韧性接入 | `QueryServiceBase` + `ResilientHttpExecutor` | ✅ |
| 3.2 | Bridge 子进程韧性接入 | `BridgeSubprocessHandle` 用 `ResilientSubprocess` | ✅ |
| 3.3 | Doctor 子进程韧性接入 | `PatientProcessManager` 用 `ResilientSubprocess` | ✅ |
| 3.4 | Sandbox 卫星进程韧性接入 | `SandboxSatelliteHost` 用 `ResilientSubprocess` | ⏳ 延后（卫星进程不重启） |
| 3.5 | MCP OAuth/Auth 韧性接入 | `IResilientHttpClientProvider` DI 注册 | ✅ |
| 3.6 | MCP 官方注册表韧性接入 | `McpOfficialRegistry` 用 `SendResilientAsync` | ✅ |
| 3.7 | Voice 服务韧性接入 | `VoiceService` 用 `SendResilientAsync` | ✅ |

### Phase 4：Pipe/WS 韧性 ✅

| 步骤 | 内容 | 接入点 |
|------|------|--------|
| 4.1 | Named Pipe 韧性 | `PipeQueryService` | ✅ |
| 4.2 | Bridge WebSocket 韧性 | `BridgeServer` | ✅ |

### Phase 5：遥测+诊断 ✅

| 步骤 | 内容 | 接入点 |
|------|------|--------|
| 5.1 | `ResilienceTelemetryReport` + `ResilienceTelemetryCollector` | Infrastructure/Utils/Resilience/ | ✅ |
| 5.2 | `jcc doctor --resilience` 命令 | 待接入 |

---

## 8. 与原 MCP Fallback 链的关系

| 方面 | 原 MCP Fallback 链 | 新统一韧性架构 |
|------|---------------------|----------------|
| **作用域** | 仅 MCP 传输 | 所有通讯点 |
| **fallback 语义** | 传输协议级降级（HTTP→SSE→WS） | 保留，MCP 传输级 fallback 不变 |
| **熔断器** | `TransportCircuitBreaker`（专用） | `UnifiedCircuitBreaker`（统一） |
| **重试** | 无（fallback 链本身就是重试） | `ResilientHttpExecutor` 提供 HTTP 级重试 |
| **两 exe 模型** | 无 | `ResilientSubprocess` 提供 |
| **层级关系** | 独立 | MCP fallback 链是统一韧性层的一个子集 |

**结论**：MCP 传输级 fallback 链保留，作为 MCP 通讯点的**传输层韧性**；统一韧性层在其之上/之下提供**HTTP 级韧性**和**进程级韧性**，形成纵深防御。

```
MCP 通讯点的纵深防御（三层）：
  ┌──────────────────────────────────────┐
  │  L1: HTTP 级韧性（ResilientHttpExecutor）  │  ← 超时+重试+熔断
  ├──────────────────────────────────────┤
  │  L2: 传输级 fallback（McpTransportFallbackChain）│  ← HTTP→SSE→WS 降级
  ├──────────────────────────────────────┤
  │  L3: 进程级韧性（ResilientSubprocess）         │  ← 健康检查+重启（Stdio 模式）
  └──────────────────────────────────────┘
```

---

## 9. AOT 兼容性

| 组件 | AOT 兼容 | 说明 |
|------|----------|------|
| `UnifiedCircuitBreaker` | ✅ | 纯值类型+枚举，无反射 |
| `ResilientHttpExecutor` | ✅ | 装饰器模式，不动态生成代码 |
| `ResilientSubprocess` | ✅ | 包装 IInteractiveProcess，无反射 |
| `ProcessHealthMonitor` | ✅ | 定时器+事件，无反射 |
| `ProcessRestartManager` | ✅ | 工厂函数，无反射 |
| `ResiliencePolicy` | ✅ | record 类型，Source Generator 友好 |

**不使用 Polly**：Polly 依赖反射和动态表达式，与 NativeAOT 不兼容。

---

## 10. 风险和缓解

| 风险 | 缓解 |
|------|------|
| 重试导致请求重复（非幂等操作） | `ResilientHttpExecutor` 默认仅对 GET/HEAD 重试，POST 需显式 `SkipRetry=false` |
| 熔断器误判（偶发失败导致熔断） | `HalfOpen` 探测机制 + 足够高的阈值（默认 5） |
| 子进程重启导致状态丢失 | `ProcessRestartManager` 触发 `BeforeRestart` 事件，调用方可保存状态 |
| 健康检查 ping/pong 协议不统一 | 各子进程定义自己的 ping/pong（Bridge 用 NDJSON notification，Doctor 用自定义协议） |
| 环境变量过多 | 全局 `JCC_RESILIENCE_ENABLED=0` 一键禁用；各通讯点有合理默认值 |
