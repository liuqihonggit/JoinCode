# 传输层纵深防御 Fallback 链设计

> 本文档整理所有传输方式的 fallback 优先级链、已实现状态、以及统一纵深防御策略。

---

## 1. 三大通讯场景

| 场景 | 说明 | 入口 |
|------|------|------|
| **E2E ↔ jcc.exe** | 测试操控 jcc.exe（DualRoleConversationRunner） | `StdioProcessManager` (Testing.Common) |
| **Bridge ↔ 子进程** | jcc.exe 与 Bridge 子进程通讯 | `BridgeSubprocessHandle` → `IReplBridgeTransport` |
| **MCP ↔ 外部服务器** | jcc.exe 与外部 MCP 服务器通讯 | `IMcpTransport` (Stdio/SSE/HTTP/WS) |

---

## 2. Fallback 链优先级

### 原则

- **本地优先**：同机通讯走 IPC/Stdio，零网络开销
- **流式优先**：SSE/WebSocket 支持服务端推送，优于请求-响应式 HTTP
- **轻量优先**：Stdio 最轻（无端口），SSE 次之（HTTP 长连接），WebSocket 最重（全双工握手）
- **AOT 兼容**：所有传输实现必须兼容 NativeAOT（禁止反射 emit）

### 链路定义

#### 场景 A：E2E ↔ jcc.exe（本地子进程）

```
Stdio → SSE → (无更高级)
```

| 优先级 | 传输 | 触发降级条件 | 已实现 |
|--------|------|-------------|--------|
| 1 | **Stdio** | 进程无法启动 / stdin 管道断裂 | ✅ `StdioProcessManager` |
| 2 | **SSE** | Stdio 不可用（远程模式） | ✅ `SseAgentTransport` |

> 说明：E2E 测试场景下 jcc.exe 是本地子进程，Stdio 是唯一合理选择。SSE 仅在远程模式（`TransportMode.Sse`）使用。两者通过 `TransportMode` 枚举 + DI 一键切换，无需运行时 fallback。

#### 场景 B：Bridge ↔ 子进程（V1/V2 协议）

```
WebSocket(V1) → SSE(V2) → (进程重启)
```

| 优先级 | 传输 | 触发降级条件 | 已实现 |
|--------|------|-------------|--------|
| 1 | **WebSocket** (V1) | 永久关闭码(1002/4001/4003) / 10分钟重连预算耗尽 | ✅ `V1ReplBridgeTransport` |
| 2 | **SSE** (V2) | 永久拒绝码(401/403/404) / Epoch冲突(409) / 心跳认证失败 | ✅ `V2ReplBridgeTransport` |
| 3 | **进程重启** | V1+V2 均不可用 | ✅ `ConnectionManager.SwitchProtocolAsync` |

> 说明：V1(WebSocket) 和 V2(SSE) 是不同版本的 Bridge 协议，不是同级 fallback。`ConnectionManager.SwitchProtocolAsync` 支持协议切换。V1 内置指数退避重连（10分钟预算）+ OAuth Token 刷新（4003）+ 系统休眠检测。V2 内置 SSE 永久拒绝码检测 + Epoch 冲突处理 + 心跳认证失败检测。

#### 场景 C：MCP ↔ 外部服务器

```
Stdio → StreamableHTTP → SSE(Client) → WebSocket
```

| 优先级 | 传输 | 触发降级条件 | 已实现 |
|--------|------|-------------|--------|
| 1 | **Stdio** | 子进程不可用 / 需要远程连接 | ✅ `StdioTransport` |
| 2 | **Streamable HTTP** | 404 会话失效 / Step-Up 认证升级 | ✅ `HttpTransport` |
| 3 | **SSE** (Client) | 重连5次耗尽 / Step-Up 认证升级 | ✅ `SseClientTransport` |
| 4 | **WebSocket** | 连接关闭 / 认证失败 | ✅ `WebSocketTransport` |

> 说明：MCP 规范定义了 Stdio（本地）、SSE（旧版远程）、Streamable HTTP（新版远程）三种传输。WebSocket 是非标准扩展。Stdio 用于本地子进程 MCP 服务器，HTTP/SSE/WS 用于远程 MCP 服务器。当前各传输独立使用，由配置决定，无运行时自动 fallback。

---

## 3. 已实现的纵深防御机制

### 3.1 重连策略

| 传输 | 策略 | 参数 | 代码位置 |
|------|------|------|----------|
| SSE (Agent) | 指数退避 | MaxReconnectAttempts=3 | `SseAgentTransport.cs` |
| SSE (MCP Client) | 指数退避 | MaxAttempts=5 | `SseClientTransport.cs` |
| SSE (Bridge) | 固定延迟 | - | `SseBridgeTransport.cs` |
| V1 Bridge (WS) | 指数退避 | 10分钟预算 | `V1ReplBridgeTransport.cs` |
| V2 Bridge (SSE) | 永久拒绝码检测 | 401/403/404 | `V2ReplBridgeTransport.cs` |

### 3.2 认证防御

| 传输 | 机制 | 触发条件 | 代码位置 |
|------|------|----------|----------|
| V1 Bridge | OAuth Token 刷新 | WS 关闭码 4003 | `V1ReplBridgeTransport.cs` |
| V2 Bridge | 心跳认证失败检测 | 心跳 401 | `V2ReplBridgeTransport.cs` |
| MCP HTTP/SSE | Step-Up 认证升级 | 403 + insufficient_scope | `StepUpDetector.cs` |

### 3.3 协议防御

| 传输 | 机制 | 触发条件 | 代码位置 |
|------|------|----------|----------|
| V2 Bridge | Epoch 冲突处理 | 409 Conflict | `V2ReplBridgeTransport.cs` |
| MCP HTTP | 会话重建 | 404 响应 | `HttpTransport.cs` |
| ConnectionManager | 协议切换 | 显式调用 | `ConnectionManager.cs` (Transport.Impl) |

### 3.4 进程防御

| 传输 | 机制 | 代码位置 |
|------|------|----------|
| V1 Bridge | 系统休眠检测 | `V1ReplBridgeTransport.cs` |
| V1 Bridge Uploader | 批次丢弃(maxConsecutiveFailures) | `SerialBatchEventUploader.cs` |

---

## 4. 统一纵深防御策略（待实现）

### 4.1 运行时自动 Fallback（当前缺失）

**现状**：各传输方式由配置静态选择，无运行时自动降级。

**目标**：当首选传输失败时，自动尝试下一优先级传输。

**设计方案**：

```csharp
/// <summary>
/// 传输 fallback 链 — 按优先级依次尝试，首个成功即返回
/// </summary>
public sealed class TransportFallbackChain : IAgentTransport
{
    private readonly IAgentTransport[] _transports; // 按优先级排列
    private IAgentTransport? _activeTransport;
    private int _activeIndex;

    public async Task ConnectAsync(CancellationToken ct)
    {
        for (var i = 0; i < _transports.Length; i++)
        {
            try
            {
                await _transports[i].ConnectAsync(ct).ConfigureAwait(false);
                _activeTransport = _transports[i];
                _activeIndex = i;
                return;
            }
            catch (Exception ex) when (i < _transports.Length - 1)
            {
                _logger.LogWarning("传输 {Name} 连接失败，尝试下一个: {Error}",
                    _transports[i].GetType().Name, ex.Message);
            }
        }
        throw new InvalidOperationException("所有传输方式均失败");
    }
}
```

### 4.2 MCP 传输自动 Fallback（当前缺失）

**现状**：MCP 传输由 `mcp_connect` 配置的 `transport` 字段静态选择。

**目标**：当首选传输连接失败时，按 MCP 规范优先级自动降级：
- Stdio 不可用 → 尝试 Streamable HTTP（需 `url` 配置）
- HTTP 不可用 → 尝试 SSE（需 `url` 配置）
- SSE 不可用 → 尝试 WebSocket（需 `url` 配置）

### 4.3 E2E 超时纵深防御（已完成 L1-L10）

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

## 5. 文件索引

### Contracts 层（`foundation/Transport.Contracts/src/`）

| 文件 | 内容 |
|------|------|
| `ITransport.cs` | 字节载荷通用传输接口 |
| `IAgentTransport.cs` | Agent 字符串级传输接口 + TransportState/TransportChannel |
| `TransportMode.cs` | DI 切换枚举（Stdio/Sse） |
| `Bridge/IBridgeTransport.cs` | Bridge 传输接口 |
| `Bridge/IReplBridgeTransport.cs` | Bridge REPL 传输接口（含重连/认证回调） |
| `Bridge/IReplBridgeTransportFactory.cs` | Bridge 传输工厂接口 |
| `Bridge/ITransportManager.cs` | 传输管理器接口 |
| `Bridge/IConnectionManager.cs` | 连接管理器接口（含协议切换） |
| `Bridge/IMessageRouter.cs` | 消息路由器接口 |
| `Bridge/IFlushGate.cs` | 刷新门控接口 |
| `Bridge/ITokenRefreshScheduler.cs` | Token 刷新调度器接口 |
| `Bridge/BridgeTransportOptions.cs` | V1/V2 传输选项 |
| `Bridge/BridgeSubprocessStatus.cs` | 子进程状态 |
| `Bridge/BridgeMessage.cs` | Bridge 消息类型 |
| `Bridge/BridgeHandle.cs` | Bridge 句柄 |
| `Bridge/BridgeFault.cs` | Bridge 故障 |

### 实现层（`infrastructure/Transport.Impl/src/`）

| 文件 | 内容 |
|------|------|
| `TransportBase.cs` | 抽象基类（生命周期+发送锁+事件触发） |
| `ReconnectPolicy.cs` | 重连策略（指数退避） |
| `Stdio/StdioProcessManager.cs` | Stdio 进程管理器 |
| `Stdio/StdioAgentTransport.cs` | Stdio Agent 传输 |
| `Sse/SseAgentTransport.cs` | SSE Agent 传输（含重连） |
| `Sse/SseStreamParser.cs` | SSE 流解析器 |
| `Pipe/PipeHttpClient.cs` | 命名管道 HTTP 客户端 |
| `Pipe/PipeHttpMessageHandler.cs` | 命名管道 HttpMessageHandler |
| `Pipe/HttpRequestSerializer.cs` | HTTP 请求序列化器 |
| `Bridge/V1ReplBridgeTransport.cs` | V1 Bridge (WebSocket+HTTP POST) |
| `Bridge/V2ReplBridgeTransport.cs` | V2 Bridge (SSE+CCR POST) |
| `Bridge/SseBridgeTransport.cs` | Bridge SSE 传输 |
| `Bridge/WebSocketTransport.cs` | Bridge WebSocket 传输 |
| `Bridge/ConnectionManager.cs` | 连接管理器（协议切换） |
| `Bridge/ReplBridgeTransportFactory.cs` | Bridge 传输工厂 |
| `Bridge/SerialBatchEventUploader.cs` | 批次事件上传器 |
| `Bridge/TransportConfiguration.cs` | 传输配置 |
| `Bridge/TransportFatalError.cs` | 传输致命错误 |

### MCP 传输层（`services/Mcp/src/Transports/`）

| 文件 | 内容 |
|------|------|
| `IMcpTransport.cs` | MCP 传输接口 |
| `StdioTransport.cs` | MCP Stdio 传输 |
| `SseTransport.cs` | MCP SSE 服务端传输 |
| `SseClientTransport.cs` | MCP SSE 客户端传输（含重连+Step-Up） |
| `HttpTransport.cs` | MCP Streamable HTTP 传输 |
| `WebSocketTransport.cs` | MCP WebSocket 传输 |
| `StepUpDetector.cs` | Step-Up 认证检测器 |

### 测试辅助层（`tests/Unit/Testing.Common/Process/`）

| 文件 | 内容 |
|------|------|
| `StdioProcessManager.cs` | E2E 测试版 Stdio 进程管理器（含 TCS 信号+JSON-RPC 等待） |

---

## 6. 决策记录

<!-- 🤖 Auto Decision: 2026-07-31 -->
<!-- 决策: Fallback 链优先级: Stdio > StreamableHTTP > SSE > WebSocket -->
<!-- 原因: 本地优先(零网络) > 流式优先(服务端推送) > 轻量优先(无全双工开销), 对齐MCP规范传输优先级 -->
<!-- 替代方案: WebSocket优先(SSE降级) — 不采用,因为WS握手开销更大且MCP规范已将StreamableHTTP定为新版标准 -->
<!-- 验证: 文档整理完成,待实现运行时自动fallback ✅ -->
