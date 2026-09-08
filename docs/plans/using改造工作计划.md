# using 改造工作计划

> 目标：将 try-finally + Dispose/DisposeAsync 模式统一改为 `using`/`await using` 单行释放
> 原则：每改一处 → 编译 → 提交，渐进式推进
> 状态：**全部完成** — 30 处已改 + 5 处保持现状（Win32 Handle 局部变量不违规） + 4056 单元测试通过

## 第一批：明确可改（13 处，零风险纯替换）✅ 全部完成

### 1.1 await using 替代 DisposeAsync（3 处）

| # | 文件:行号 | 资源 | 状态 |
|---|-----------|------|------|
| ① | `core/execution/Hands/src/ToolHandlers/Handlers/SystemTools/Web/DownloadToolHandlers.cs:84` | session | ✅ |
| ② | `composition/Pipelines/src/Middlewares/ChatErrorHandlingMiddleware.cs:49` | enumerator | ✅ |
| ③ | `core/execution/Brain/src/Context/Services/Chat/StreamCrashSnapshotMiddleware.cs:55` | enumerator | ✅ |

### 1.2 using 替代 Dispose（2 处）

| # | 文件:行号 | 资源 | 状态 |
|---|-----------|------|------|
| ④ | `core/ai/Agents/src/Coordinator/DualModel/ModelCoordinator.cs:108` | planner | ✅ |
| ⑤ | `core/ai/Agents/src/Coordinator/DualModel/ModelCoordinator.cs:151` | executor | ✅ |

### 1.3 锁守卫 guard.Dispose → using（8 处）

| # | 文件:行号 | 资源 | 状态 |
|---|-----------|------|------|
| ⑥ | `services/Mcp/src/Client/Transport/McpStdioClient.cs:303` | guard | ✅ |
| ⑦ | `services/Mcp/src/Client/Transport/McpStdioClient.cs:331` | guard2 | ✅ |
| ⑧ | `services/Mcp/src/Client/Transport/McpNetworkClient.cs:80` | guard | ✅ |
| ⑨ | `services/Mcp/src/Client/Transport/McpNetworkClient.cs:100` | guard1 | ✅ |
| ⑩ | `services/Mcp/src/Client/McpClientBase.cs:149` | guard | ✅ |
| ⑪ | `services/Mcp/src/Client/McpClientBase.cs:186` | guard | ✅ |
| ⑫ | `services/Mcp/src/Client/Transport/McpFallbackClient.cs:75` | guard | ✅ |
| ⑬ | `services/Mcp/src/Client/Transport/McpFallbackClient.cs:94` | guard1 | ✅ |

## 第二批：null 条件 Dispose → using（C#8+ 支持 null）✅ 全部完成

> **关键发现**：C# 8+ 的 `using`/`await using` 声明支持 null 值，编译器自动生成 null 检查。
> 之前跳过的 5 处"null 条件 Dispose"理由不成立，全部可改。

| # | 文件:行号 | 资源 | 状态 | 改造方式 |
|---|-----------|------|------|----------|
| ⑭ | `app/JoinCode/Program.cs:116` | doctorClient | ✅ | `await using var doctorClient = ... ?: null;` |
| ⑮ | `infrastructure/Infrastructure/Plugins/Services/PluginManager.cs:193` | span | ✅ | `await using var span = ...;` |
| ⑯ | `infrastructure/Infrastructure/Plugins/Services/PluginManager.cs:293` | span | ✅ | 同 ⑮ |
| ⑰ | `infrastructure/Infrastructure/IO/Services/FileOps/ThrottledFileService.cs:80` | span（4处） | ✅ | 同 ⑮ |
| ⑱ | `infrastructure/Infrastructure/Http/ResilientHttpExecutor.cs:75` | totalTimeoutCts（4处） | ✅ | `using var totalTimeoutCts = ...;` |
| ⑲ | `core/execution/Hands/src/ToolHandlers/Handlers/DesktopTools/ProcessToolHandlers.cs:74` | foreach 迭代变量 | ✅ | 改为 for 循环 + `using var p = targets[i]` |

## 第三批：谁申请谁释放（参数违规）✅ 全部完成

> **原则**：谁申请谁释放。方法参数不应该在被调用方法内部释放。
> fire-and-forget 场景：调用方用 async lambda + using 管理资源所有权。

| # | 文件 | 违规参数 | 调用方 | 状态 | 改造方式 |
|---|------|----------|--------|------|----------|
| ⑳ | `core/execution/Hands/src/Network/MobileConnectService.cs` | client (TcpClient) | AcceptLoopAsync | ✅ | 调用方 `using var client` + `await`（串行处理） |
| ㉑ | `core/ai/Agents/src/Doctor/DoctorTcpServer.cs` | tcpClient (TcpClient) | RunAcceptLoopAsync | ✅ | `Task.Run` async lambda + `using var c = tcpClient` |
| ㉒ | `core/ai/Agents/src/Coordinator/Fork/ForkSubAgentManager.cs` | forkReleaser (IDisposable?) | ForkAsync | ✅ | async lambda + `using var r = capturedReleaser` |
| ㉓ | `core/search/CodeIndex/src/Incremental/FileWatcherIntegrationRegistry.cs` | watcher (IAsyncDisposable) | OnRepoUnregistered | ✅ | `Task.Run` async lambda + `await using var w` |

## 第四批：Win32 Handle → SafeHandle（6 处）

| # | 文件:行号 | Handle 类型 | 状态 | 说明 |
|---|-----------|-------------|------|------|
| ㉔ | `infrastructure/Infrastructure/Windows/JobObject/WindowsJobObjectSandbox.cs:114` | nint 进程句柄 | ✅ | 用 BCL `SafeProcessHandle` 代替裸 nint |
| ㉕ | `infrastructure/Infrastructure/IO/Services/Terminal/Core/TerminalCaptureService.cs:368` | int POSIX fd | ⏸️ | 局部变量不违规，Linux 代码，需创建 SafeFdHandle |
| ㉖ | `core/execution/Hands/src/ToolHandlers/Handlers/DesktopTools/DesktopOverlayToolHandlers.cs:49` | IntPtr GDI DC | ⏸️ | 局部变量不违规，ReleaseDC 需多参数 |
| ㉗ | `core/execution/Hands/src/Desktop/PulseOverlay/DesktopPulseOverlay.cs:178` | PAINTSTRUCT | ⏭️ | 局部变量不违规，EndPaint 需 `ref ps` |
| ㉘ | `core/execution/Hands/src/Desktop/GdiScreenCaptureService.cs:81` | GCHandle struct | ⏭️ | 局部变量不违规，struct 不实现 IDisposable |
| ㉙ | `core/execution/Hands/src/Desktop/GdiScreenCaptureService.cs:98` | 多 GDI 句柄交错 | ⏭️ | 局部变量不违规，多资源交错释放 |

> **注**：㉕㉖㉗㉘㉙ 都是局部变量在 finally 里释放，不违反"谁申请谁释放"原则。

## 已完成记录

| 提交 | 内容 |
|------|------|
| `eb3dfcfbd` | GUI 窗口关闭释放引擎资源（MainViewModel + MainWindow） |
| `af92a0c3f` | CLI 主路径退出释放 host |
| `f7ac38056` | host 释放统一改 using 单行模式（Program/McpCommand/DoctorSubCommand/Tui） |
| `dcc7f34e5` | 全局 JobObject 防护子进程孤儿化 + SSH 转发心跳保活 |
| `717b9d980` | try-finally+Dispose 统一改 using（第一批 13 处） |
| `ac5d85b0b` | ProcessToolHandlers foreach → for+using（第二批 ⑲） |
| `ed98fd8f2` | WindowsJobObjectSandbox SafeProcessHandle（第三批 ㉔） |
| `c8bfbbdd2` | doctorClient try-finally → await using（C#8+ 支持 null） |
| `c18f11efc` | PluginManager span try-finally → await using（2处） |
| `d7109d88c` | ThrottledFileService span try-finally → await using（4处） |
| `1a9cafbe7` | ResilientHttpExecutor totalTimeoutCts try-finally → using（4处） |
| `b596402ee` | MobileConnectService 谁申请谁释放 — 调用方 using 管理 client |
| `3eab42dc2` | DoctorTcpServer 谁申请谁释放 — lambda using 管理 tcpClient |
| `329a93bf4` | ForkSubAgentManager 谁申请谁释放 — lambda using 管理 releaser |
| `cae42c9a4` | FileWatcherIntegrationRegistry 谁申请谁释放 — lambda await using 管理 watcher |

## 测试验证

| 测试项目 | 通过 | 失败 |
|----------|------|------|
| Agents.Tests | 527 | 0 |
| Hands.ToolHandlers.Tests | 405 | 0 |
| Infra.Services.Tests | 490 | 0 |
| Infra.IO.Tests | 132 | 0 |
| Infra.Utils.Tests | 573 | 0 |
| Host.Tests | 909 | 0 |
| Integration.Tests | 390 | 0 |
| Tui.Tests | 178 | 0 |
| MockServer.Core.Tests | 67 | 0 |
| MockServer.E2E.Tests | 6 | 0 |
| CodeIndex.Tests | 379 | 0 |
| **合计** | **4056** | **0** |

## 统计

- 已改：**30 处**（13 第一批 + 1 foreach + 1 SafeHandle + 11 null 条件 + 4 谁申请谁释放）
- 保持现状：**5 处**（Win32 Handle 局部变量，不违规）
- 测试：4056 通过，0 失败

<!-- 🤖 Auto Decision: 2026-09-06 -->
<!-- 决策: C#8+ using/await using 声明支持 null 值，之前跳过的 5 处 null 条件 Dispose 全部可改 -->
<!-- 原因: 编译器自动生成 null 检查，`using var x = null;` 不会抛异常，跳过 Dispose -->
<!-- 验证: 创建临时项目验证 using null 行为，编译通过，4056 测试全部通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-09-06 -->
<!-- 决策: 谁申请谁释放原则 — 方法参数不在被调用方释放，fire-and-forget 用 async lambda + using -->
<!-- 原因: 参数是被调用方"借用"的，不应释放；lambda 捕获变量 = lambda 拥有所有权，可以 using -->
<!-- 改造: MobileConnectService 串行 await + using；DoctorTcpServer/ForkSubAgentManager/FileWatcher lambda + using -->
<!-- 验证: 编译通过，4056 测试全部通过 ✅ -->
