# 0025. 归档 IMcpProtocolHandler 死接口

- 状态：proposed
- 日期：2026-08-29
- 决策者：项目架构组
- 取代：[0012](docs/adr/0012-two-itoolhandler-interfaces.md)

## 背景

ADR 0012 决策"双 IToolHandler 接口不合并"，理由是协议层与业务层语义不同。但 2026-08-29 代码调查发现：

1. **`IMcpProtocolHandler` 有 0 个生产实现**：仅 1 个测试 FakeToolHandler
2. **`McpServer`（唯一使用者）也是死代码**：0 个生产实例化、0 个 DI 注册、仅 13 处测试 `new McpServer("test")`
3. **`McpHttpServer` 已替代 `McpServer`**：新的 HTTP 服务端（ADR 0009），独立实现，不依赖 IMcpProtocolHandler
4. 两个接口**无继承关系、无转换代码**，完全独立

ADR 0012 为一个**死接口**做"不合并"决策，保留了导致混淆的无用接口（名字与 IToolHandler 相似、职责相似但 0 实现）。

## 决策

**归档 `IMcpProtocolHandler` + `McpServer` + `IMcpServer` 到 `.xxx/`**（按 ADR 0008 归档规范），不保留无用的协议层接口。

归档范围：
- `services/Mcp/src/McpProtocol/IMcpProtocolHandler.cs`
- `services/Mcp/src/McpProtocol/McpServer.cs`
- `services/Mcp/src/McpProtocol/IMcpServer.cs`
- 相关测试文件（`McpServerDefensiveTests.cs` 等仅测 McpServer 的）

归档后：
- `IToolHandler`（Abstractions）成为唯一的工具处理器接口
- 需要工具注册的 MCP 服务端用 `McpHttpServer`（已不依赖 IMcpProtocolHandler）

## 替代方案

1. **保留 IMcpProtocolHandler 但明确作用域**（ADR 0012 原方案）：放弃。0 实现的接口无保留价值，"明确作用域"不能消除"该用哪个"的困惑。
2. **合并到 IToolHandler**：放弃。McpServer 本身是死代码，为死代码做合并无意义，先归档死代码再说。
3. **保留 McpServer 删 IMcpProtocolHandler**：放弃。McpServer 依赖 IMcpProtocolHandler 做工具注册，删接口不删使用者会编译失败，两者需一起归档。

## 后果

- 正面：消除双接口混淆；`IToolHandler` 成为唯一接口；减少维护面；新人不再困惑
- 负面：McpServer 相关测试需归档或重写为 McpHttpServer 测试
- 中性：如果未来需要 MCP stdio 服务端，可基于 McpHttpServer 和 IToolHandler 重新实现，不需要恢复旧接口
