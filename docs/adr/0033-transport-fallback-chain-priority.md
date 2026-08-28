# 0033. 传输层 Fallback 链优先级

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

MCP 客户端支持多种传输协议（Stdio、StreamableHTTP、SSE、WebSocket），连接服务端时需决定尝试顺序。不同协议的可靠性、延迟、适用场景不同。

## 决策

**Fallback 链优先级：Stdio > StreamableHTTP > SSE > WebSocket**

- **Stdio**：最高优先级，本地进程通信，最可靠、最低延迟。有 command 才加入链
- **StreamableHTTP**：次优先，MCP 2025-11-25 规范（ADR 0009），支持无状态
- **SSE**：已归档到 `.xxx/`（旧 2024-11-05 规范），作为兼容兜底
- **WebSocket**：最低优先级，需保持长连接，无状态模式不适用

**服务端 fallback** = 首选 + 运行时降级
**客户端 fallback** = 连接 + 断连 + 熔断

定位文件：`docs/design/TransportFallbackChain.md`

## 替代方案

1. **仅用 StreamableHTTP**：放弃。本地进程通信用 Stdio 更高效，不应强制 HTTP。
2. **WebSocket 优先**：放弃。需保持长连接，无状态模式不适用，且 AOT 兼容性不如 HTTP。
3. **SSE 优先**：放弃。已归档到 `.xxx/`（ADR 0009），旧规范不再优先。

## 后果

- 正面：按可靠性排序，优先用最稳定的协议；Stdio 本地通信零网络开销
- 负面：多协议实现维护成本高；Fallback 链逻辑复杂
- 中性：Stdio 有 command 才加入链，无 command 跳过
