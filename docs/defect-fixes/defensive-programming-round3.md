# 防御性编程缺陷修复任务清单（第三轮）

> 创建时间: 2026-08-06
> 目标: 扩大扫描范围到 app/ 与 services/ 层（第二轮覆盖 infrastructure/core/services 其余部分）。
> 依据: 定向 grep 扫描（app + services 共 ~60 处 Trace.WriteLine 与 fire-and-forget + 逐文件确认）。

## 扫描结论

app/ 与 services/ 层经过前两轮加固后已高度防御化。主要残留为：

1. **不可见错误（LOW）**：catch/fallback 块用 `System.Diagnostics.Trace.WriteLine`（无 listener 即不可见），其中一部分类**已有 ILogger**，改为 `_logger?.LogXxx` 即可零风险保留可见性（与前两轮 L1/L2/F3 同主题）。
2. **fire-and-forget**：BridgeMain 会话监视、BridgeWorkPollLoop 轮询等均有内部 try/catch + 退避保护，未发现新断裂点。
3. **一处微守卫缺陷（MEDIUM）**：`BridgeWorkPollLoop.SetTransport` 中旧传输 `DisposeAsync()` 置于 try/catch **之外**，在 `Task.Run` 内抛异常会成为未观察异常被静默吞掉。

## 本轮修复列表

| 编号 | 严重度 | 文件:行 | 类型 | 问题 | 修复 |
|------|--------|---------|------|------|------|
| R1 | MEDIUM | `services/Bridge/src/Client/BridgeWorkPollLoop.cs:270-281,423` | 纵深防御守卫 | 旧传输 `DisposeAsync()` 在 `Task.Run` 内位于 try/catch 之外，异常成未观察异常 | 将 `CloseAsync`+`DisposeAsync` 同置于 try/catch，3 处 Trace→ILogger |
| R2 | LOW | `services/Mcp/src/Auth/OAuth/McpOAuthService.cs:127` | 不可见错误 | 已有 `_logger` 却用 Trace.WriteLine | 改 `_logger?.LogWarning` |
| R3 | LOW | `services/Bridge/src/Client/BridgeApiClient.cs:1136` | 不可见错误 | 已有 `_logger` 却用 Trace.WriteLine（标题同步失败） | 改 `_logger?.LogDebug` |

> R1 为行为保持、防御加固；R2/R3 为行为中性的日志可见性替换，与前两轮 L1/L2/F3 同主题。无新增单元测试（纯日志/守卫变更，已验证对应测试套件无回归）。

## 验证

- `Bridge.Tests` 594 通过 ✅
- `Mcp.Tests` 139 通过 ✅
- 改动项目 Debug 编译 0 错误 0 警告 ✅

## 决策记录

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 第三轮仅做低风险日志可见性 + 微守卫修复，不扩大横扫 -->
<!-- 原因: app/services 层经前两轮加固已高度防御化，剩余 Trace.WriteLine 多为 best-effort catch；仅在帧已有 ILogger 的类内统一改为 _logger，避免给无 logger 的类加注入（大改动低价值） -->
<!-- 替代方案: 给无 logger 类逐一加构造注入（改动面大、破坏 DI，收益低，不采用）-->
<!-- 验证: Bridge 594 + Mcp 139 通过，Debug 编译 0 错误 ✅ -->

---

## 第四轮（追加：处理剩余 Trace.WriteLine）

> 用户选择"处理剩余无 logger 类"。按 `classify_trace.py` 依 `has_logger/static` 分类，分两批：
> **A 组（已有 `_logger` 的实例类，零风险直接替换）** 与 **B 组（无 logger 需构造注入）**。

### A 组修复（已完成 ✅，提交 ab46a58ed）

10 个已含 `_logger` 的实例类，将 `Trace.WriteLine` 统一改为 `_logger?.LogWarning/Debug/Error`：

| 文件 | 处理 |
|------|------|
| `Hands/src/Api/Core/UsageTracker.cs` | `ExtractTokenUsage` 去 `static` 以访问实例 `_logger`，改 `LogWarning` |
| `Hands/src/Network/RemoteTriggerService.cs` | 2 处 `LogWarning` |
| `Scheduling/src/Cron/CronScheduler.cs` | 补 `_logger` 字段 + DI 构造链透传 logger（原 DI 构造器丢弃 logger 参数属潜在缺陷），2 处 Trace→Log |
| `Scheduling/src/Tasks/MonitorMcpTask.cs` | 1 处 `LogWarning` |
| `Vault/src/State/Transcript/TranscriptService.cs` | 2 处 Trace→Log |
| `Eyes/Lsp/Internal/Server/LspClient.cs` | 1 处 `LogWarning` |
| `Eyes/Lsp/Internal/Service/LspService.cs` | 1 处 `LogWarning` |
| `Mcp/Mcpb/Middleware/McpbExtractionMiddleware.cs` | 1 处 `LogDebug` |
| `Mcp/Mcpb/Middleware/McpbValidationMiddleware.cs` | 1 处 `LogDebug` |
| `Mcp/Remote/RemoteClientManager.cs` | 1 处 `LogWarning` |

### 验证（A 组）

- `dotnet build Core.slnx` / `Services.slnx` Debug `--no-incremental`：0 错误 0 警告 ✅
- `Scheduling.Tests` 257、`Mcp.Tests` 139、`Vault.Other` 238、`Vault.Memdir` 137、`Hands.Api` 184 全部通过 ✅
- 10 个 A 组文件 `Trace\.WriteLine` 经 `rg` 复核零残留 ✅

### 决策记录（A 组）

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: A 组 10 个已有 logger 类直接替换 Trace→ILogger；UsageTracker.ExtractTokenUsage 去 static；CronScheduler 补齐被丢弃的 DI logger 构造链 -->
<!-- 原因: has_logger 类零风险直接替换；CronScheduler 原 DI 构造器接收 logger 却未透传属静默缺陷一并修复 -->
<!-- 踩坑: 日志消息里的 `filePath ?? "?"` 会触发 CS8604（形参本已非空），去除空值合并后消除 -->
<!-- 验证: Core/Services Debug 全量编译 0 错误，五套件测试通过 ✅ -->

### B 组（已全部完成 ✅）

用户选定"B 组全部 22 类一次做完"。22 个 logger-less 实例类加可选 `ILogger<T>? logger = null` 构造注入（MS.DI 对可选参数自动回退默认值，兼容既有 `new X()` 调用），统一将 `Trace.WriteLine` 改为 `_logger?.LogXxx`：

| 层 | 类 | Trace 数 |
|----|----|----------|
| App | `CliSession`、`DotEnvConfig`(静态工厂加 `ILogger?` 参) | 3 |
| Composition | `AppEventBus` | 2 |
| Core-Agents | `TeamManager`、`DoctorSseClient` | 3 |
| Core-Hands | `UpgradeService`、`SkillService`、`ToolArgumentParser` | 4 |
| Core/Vault | `ConfigPersistentServiceBase`(抽象基类，`protected ILogger?`) | 3 |
| Core/CodeIndex | `FileWatcherIntegrationRegistry`（去 static 解耦 logger） | 2 |
| Foundation | `ToolUseContext`（方法级可选 `ILogger?` 参） | 1 |
| Infrastructure | `ExternalPluginHost`、`SshForwardedPort`、`SshSession`（已有 logger）、`ThreadSafeListenerList` | 4 |
| Services/Bridge | `BridgeWorkPollLoop`、`V1BridgeHandle`（已有 logger）、`V1WorkPollSetupMiddleware`（复用 `ctx.Logger`） | 10 |
| Services/Dream | `DefaultSessionScanner` | 1 |
| Services/Mcp | `McpResourceToolHandlers`、`SkillToolHandlers`、`SseTransport`+`SseClient` | 5 |

### 验证（B 组）

- 6 层 Debug `--no-incremental` 全量编译 0 警告 0 错误 ✅（Foundation/Infrastructure/Services/Core/Composition/App）
- 套件全部通过 ✅：Infra 565、Agents 271、Clock 421、CodeIndex 374、Host 707、Scheduling 257、Bridge 594、Mcp 139、Dream 174、Vault 238+137、Hands 184+109+220+119（`WebSocket_EchoHello` 曾因端口 9761 冲突单发失败，重跑即通过 = 环境性 flake，非代码回归）
- 22 个 B 组文件 `Trace\.WriteLine` 复核零残留 ✅

### 决策记录（B 组）

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 22 个无 logger 实例类统一加可选 ILogger 构造注入，Trace→ILogger -->
<!-- 原因: 可选参默认 null 使既有 new X() 与 MS DI（可选参自动回退默认值）双兼容，改动面经 compile+test 全绿验证；用户明确选择一次全部完成 -->
<!-- 特例: ToolUseContext 为 DTO 采方法级可选 ILogger 参避免类污染；ThreadSafeListenerList 等工具类亦可选 ILogger<泛型>；V1WorkPollSetupMiddleware 复用 ctx.Logger 免构造注入 -->
<!-- 踩坑: ① FileWatcherIntegrationRegistry 一处 Trace 在 static 方法内用 logger 需去 static; ② SseTransport 的"释放 SSE 输出流"实为内部 SseClient 类 → 需给 SseClient 加 logger -->
<!-- 验证: 6 层全量编译 0 错误 + 15 套件全绿 ✅ -->