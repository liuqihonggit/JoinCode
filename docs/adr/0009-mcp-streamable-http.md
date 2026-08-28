# 0009. MCP Streamable HTTP 2025-11-25

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

MCP（Model Context Protocol）协议有多个版本。旧版 `2024-11-05` 使用 SSE（Server-Sent Events）传输，`SseClientTransport`/`SseTransport` 实现复杂、有状态、不支持无状态服务端。新版 `2025-11-25` 采用 Streamable HTTP，更简洁、支持无状态模式。

## 决策

1. **MCP 协议版本固定 `2025-11-25`（Streamable HTTP）**
2. **旧 `2024-11-05` + SseClientTransport/SseTransport 已归档到 `services/Mcp/.xxx/`**
3. **客户端用 `HttpTransport`**，服务端用 `McpHttpServer`（HttpListener 实现）
4. **双模式支持**：
   - 无状态模式：不分配 `MCP-Session-Id`
   - 有状态模式：分配 `MCP-Session-Id` + DELETE 终止
5. **握手协商**：`MCP-Protocol-Version` 头
6. **SSE 推送**：GET 开 SSE 推送 `NotificationReceived`

## 替代方案

1. **保留旧 SSE 协议**：放弃。有状态、实现复杂、不支持无状态服务端，与项目"可无状态扩展"目标冲突。
2. **同时支持两版本**：放弃。维护两套传输实现成本高，且旧版已无新需求。
3. **用 WebSocket 替代 HTTP**：放弃。WebSocket 需保持长连接，无状态模式不适用。

## 后果

- 正面：协议简洁；支持无状态服务端（不分配 Session-Id）；HttpListener 实现轻量
- 负面：旧客户端需升级协议版本；SSE 推送需 GET 端点单独处理
- 中性：服务端 `McpHttpServer.cs` 位于 `services/Mcp/src/McpProtocol/`
