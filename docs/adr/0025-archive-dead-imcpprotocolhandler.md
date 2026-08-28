# 0025. 归档 IMcpProtocolHandler 死接口

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组
- 取代：[0012](docs/adr/0012-two-itoolhandler-interfaces.md)
- 验证：Mcp 编译 0 警告 0 错误，171 单元测试全通过 ✅

## 背景

ADR 0012 决策"双 IToolHandler 接口不合并"，理由是协议层与业务层语义不同。但 2026-08-29 代码调查发现：

1. **`IMcpProtocolHandler` 有 0 个生产实现**：仅 1 个测试 FakeToolHandler。被 `McpServer` 的工具注册功能（`_tools` 字段、`RegisterTool`/`RegisterToolHandler`）引用，但该功能**无生产调用**（没有生产代码注册任何工具到 McpServer）。
2. **`McpServer` 不是死代码**：被 `McpHttpServer` 包装使用（`McpHttpServer` 持有 `McpServer _server` 实例，复用 `ProcessMessageAsync` 处理 JSON-RPC 消息）。但 McpServer 的**工具注册功能**是死的。
3. **`IMcpServer` 半死**：仅 `McpServer` 实现它，无外部代码引用 `IMcpServer` 类型。
4. 两个接口**无继承关系、无转换代码**，完全独立。

ADR 0012 为一个**0 实现的接口**做"不合并"决策，保留了导致混淆的无用接口（名字与 IToolHandler 相似、职责相似但 0 实现）。

## 决策

**归档 `IMcpProtocolHandler` + `IMcpServer`，修改 `McpServer` 去掉对它们的依赖**（按 ADR 0008 归档规范）。

具体操作：
1. 归档 `services/Mcp/src/McpProtocol/IMcpProtocolHandler.cs` → `.xxx/`
2. 归档 `services/Mcp/src/McpProtocol/IMcpServer.cs` → `.xxx/`
3. 修改 `McpServer.cs`：
   - 去掉 `: IMcpServer` 接口实现
   - 去掉 `IMcpProtocolHandler` 依赖（`_tools` 字段、`RegisterTool`/`RegisterToolHandler` 方法、`HandleListTools`/`HandleCallToolAsync` 中对 `_tools` 的引用）
   - 保留 `ProcessMessageAsync` 等消息处理功能（`McpHttpServer` 依赖）
4. 归档 `McpServerDefensiveTests.cs`（仅测 McpServer 工具注册）
5. 修改 `McpHttpServerTests.cs` / `McpHttpServerE2ETests.cs`：去掉对 McpServer 工具注册的测试

归档后：
- `IToolHandler`（Abstractions）成为唯一的工具处理器接口
- `McpServer` 保留消息处理功能，去掉死掉的工具注册功能
- `McpHttpServer` 继续依赖 `McpServer` 的消息处理

## 替代方案

1. **保留 IMcpProtocolHandler 但明确作用域**（ADR 0012 原方案）：放弃。0 实现的接口无保留价值，"明确作用域"不能消除"该用哪个"的困惑。
2. **归档 McpServer + IMcpProtocolHandler + IMcpServer 全部**：放弃。McpServer 被 McpHttpServer 依赖（复用 ProcessMessageAsync），归档 McpServer 会破坏 McpHttpServer。
3. **合并 IMcpProtocolHandler 到 IToolHandler**：放弃。两者类型不同（JsonElement vs ToolSchema），且 IMcpProtocolHandler 0 实现，合并死接口无意义，直接归档更干净。

## 后果

- 正面：消除双接口混淆；`IToolHandler` 成为唯一接口；减少维护面；新人不再困惑
- 负面：McpServer 需修改（去掉工具注册功能）；部分测试需归档或修改
- 中性：McpServer 的消息处理功能保留，McpHttpServer 不受影响；未来如需工具注册可基于 IToolHandler 重新实现
