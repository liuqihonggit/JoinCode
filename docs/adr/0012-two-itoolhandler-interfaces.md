# 0012. 双 IToolHandler 接口不合并

- 状态：superseded by 0025
- 日期：2026-08-29
- 决策者：项目架构组
- 取代原因：调查发现 IMcpProtocolHandler 有 0 个生产实现、McpServer 也是死代码，为死接口做"不合并"决策无意义。详见 [0025](docs/adr/0025-archive-dead-imcpprotocolhandler.md)

## 背景

项目中存在两个 `IToolHandler` 接口：
1. `McpProtocol.IToolHandler`：`InputSchema` 为 `JsonElement`，返回 `object`，MCP 协议内部类型
2. `Abstractions.IToolHandler`：`InputSchema` 为 `ToolSchema`，返回 `ToolResult`，有 `Kind`/`GroupName`/`onProgress`，主接口

曾考虑合并以减少概念重复。

## 决策

**两者不合并**（语义不同），但 `McpProtocol.IToolHandler` 重命名为 `IMcpProtocolHandler` 避免混淆。

理由：
- `IMcpProtocolHandler` 是 MCP 协议层内部类型，处理 JSON 协议细节
- `Abstractions.IToolHandler` 是业务主接口，携带工具元数据（Kind/GroupName）和进度回调
- 合并会把协议层细节泄漏到业务抽象层

同时合并三个 ResultBuilder（`ToolResultBuilder` / `ResultBuilder` / `McpResultBuilder`）为一个，将 `WithPdf`/`WithBinary`/`WithEntityMetadata` 全部合并到 `ToolResultBuilder`。

## 替代方案

1. **合并为单一 `IToolHandler`**：放弃。协议层 `JsonElement` 与业务层 `ToolSchema` 类型不同，合并后接口臃肿且类型不安全。
2. **`Abstractions.IToolHandler` 继承 `IMcpProtocolHandler`**：放弃。业务层不应依赖协议层类型。
3. **保留两个同名 `IToolHandler`**：放弃。同名易混淆，重命名为 `IMcpProtocolHandler` 消除歧义。

## 后果

- 正面：职责分离清晰；协议层与业务层解耦；命名消除混淆
- 负面：新人需理解两个接口的区别；ResultBuilder 合并后 `ToolResultBuilder` 承担更多职责
- 中性：`DelegateToolHandler` 内部补传 `toolName` 参数（委托需工具名路由，接口通过 `this.Name` 获取，语义不同）
