# 死代码归档记录

> 本文档记录项目中移除的死配置/死接口/死代码的归档操作,非架构决策,仅保留审计追踪。
> 迁移自 ADR 0049(归档 MaxConcurrentAgents)和 ADR 0025(归档 IMcpProtocolHandler)。

## 1. 归档 MaxConcurrentAgents 死配置（原 ADR 0049）

- 日期：2026-09-02
- 关联：[ADR 0048](../adr/0048-subagent-concurrency-unified-config.md)（子代理并发统一配置）

### 背景

`AgentSettings.MaxConcurrentAgents`（`core/ai/Agents/src/Configuration/Settings/AgentSettings.cs:12`）定义了"最大并发 Agent 数=10"，但代码调查发现：

1. **全项目仅 1 处引用** — 即定义处本身，没有任何消费方读取该属性
2. **实际并发控制由其他配置承担**：
   - `TaskExecutor.ExecuteAgentsParallelAsync` 用 `ExecutionOptions.MaxConcurrentTasks=12`
   - `AgentExecutionEngine.ExecuteParallelAsync` 用 `ClusterExecutionOptions.MaxConcurrency` 或 `ParallelOptions.MaxDegreeOfParallelism`
3. **配置项语义误导** — 新人看到 `MaxConcurrentAgents=10` 会以为子代理并发上限是 10，但实际是 12（`MaxConcurrentTasks`）或运行时传入值

### 操作

1. 删除 `AgentSettings.MaxConcurrentAgents` 属性
2. 归档 `AgentSettings.cs` 旧版本到 `.xxx/`
3. 子代理并发控制统一到 `SubAgentConcurrencyOptions`（ADR 0048）
4. 检查 `settings.json` 是否有 `maxConcurrentAgents` 配置项，如有则迁移到 `subAgentConcurrency.maxConcurrentExecutions`

归档后：
- `AgentSettings` 仅保留 `AgentTimeoutSeconds`、`MaxRetryCount`、`EnableWorktreeIsolation`、`DefaultModelName`、`MaxContextLength`（非并发配置）
- 子代理并发上限唯一数据源为 `SubAgentConcurrencyOptions`（ADR 0048）

### 验证

Agents 编译 0 警告 0 错误，MaxConcurrentAgents 属性已删除，全项目无引用 ✅

## 2. 归档 IMcpProtocolHandler 死接口（原 ADR 0025）

- 日期：2026-08-29
- 取代：[ADR 0012](../adr/0012-two-itoolhandler-interfaces.md)（双 IToolHandler 接口不合并）

### 背景

ADR 0012 决策"双 IToolHandler 接口不合并"，理由是协议层与业务层语义不同。但 2026-08-29 代码调查发现：

1. **`IMcpProtocolHandler` 有 0 个生产实现**：仅 1 个测试 FakeToolHandler。被 `McpServer` 的工具注册功能引用，但该功能**无生产调用**。
2. **`McpServer` 不是死代码**：被 `McpHttpServer` 包装使用。但 McpServer 的**工具注册功能**是死的。
3. **`IMcpServer` 半死**：仅 `McpServer` 实现它，无外部代码引用 `IMcpServer` 类型。
4. 两个接口**无继承关系、无转换代码**，完全独立。

### 操作

1. 归档 `services/Mcp/src/McpProtocol/IMcpProtocolHandler.cs` → `.xxx/`
2. 归档 `services/Mcp/src/McpProtocol/IMcpServer.cs` → `.xxx/`
3. 修改 `McpServer.cs`：去掉 `: IMcpServer` 接口实现 + `IMcpProtocolHandler` 依赖（`_tools` 字段、`RegisterTool`/`RegisterToolHandler` 方法），保留 `ProcessMessageAsync` 等消息处理功能
4. 归档 `McpServerDefensiveTests.cs`（仅测 McpServer 工具注册）
5. 修改 `McpHttpServerTests.cs` / `McpHttpServerE2ETests.cs`：去掉对 McpServer 工具注册的测试

归档后：
- `IToolHandler`（Abstractions）成为唯一的工具处理器接口
- `McpServer` 保留消息处理功能，去掉死掉的工具注册功能
- `McpHttpServer` 继续依赖 `McpServer` 的消息处理

### 验证

Mcp 编译 0 警告 0 错误，171 单元测试全通过 ✅
