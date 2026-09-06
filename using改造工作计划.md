# using 改造工作计划

> 目标：将 try-finally + Dispose/DisposeAsync 模式统一改为 `using`/`await using` 单行释放
> 原则：每改一处 → 编译 → 提交，渐进式推进
> 状态：**全部完成** — 15 处已改 + 9 处保持现状（有特别理由） + 3944 单元测试通过

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

## 第二批：有条件可改（8 处）

| # | 文件:行号 | 问题 | 状态 | 保持现状理由 |
|---|-----------|------|------|-------------|
| ⑭ | `app/JoinCode/Program.cs:116` | doctorClient 条件创建可空 | ⏸️ | null 安全：`await using` 不支持 null，需引入哨兵类型，收益小 |
| ⑮ | `infrastructure/Infrastructure/Plugins/Services/PluginManager.cs:193` | span 可空 | ⏸️ | ITelemetrySpan? 类型不兼容 `await using`（后续需调 `.SetTag`） |
| ⑯ | `infrastructure/Infrastructure/Plugins/Services/PluginManager.cs:293` | span 可空 | ⏸️ | 同 ⑮ |
| ⑰ | `infrastructure/Infrastructure/IO/Services/FileOps/ThrottledFileService.cs:80` | span 可空（4处） | ⏸️ | 同 ⑮ |
| ⑱ | `infrastructure/Infrastructure/Http/ResilientHttpExecutor.cs:75` | totalTimeoutCts 条件创建（4处） | ⏸️ | null 安全：条件创建的 CTS，`using` 不支持 null |
| ⑲ | `core/execution/Hands/src/ToolHandlers/Handlers/DesktopTools/ProcessToolHandlers.cs:74` | foreach 迭代变量 | ✅ | 改为 for 循环 + `using var p = targets[i]` |
| ⑳ | `core/execution/Hands/src/Network/MobileConnectService.cs:115` | 参数变量 | ⏸️ | 参数变量：需重构调用链，调用方用 using 管理 client 生命周期 |
| ㉑ | `core/ai/Agents/src/Doctor/DoctorTcpServer.cs:195` | 参数变量 + try-catch 吞异常 | ⏸️ | 参数变量 + Close 包在 try-catch 内防二次异常 |

## 第三批：Win32 Handle → SafeHandle（6 处）

| # | 文件:行号 | Handle 类型 | 状态 | 保持现状理由 |
|---|-----------|-------------|------|-------------|
| ㉒ | `infrastructure/Infrastructure/Windows/JobObject/WindowsJobObjectSandbox.cs:114` | nint 进程句柄 | ✅ | 用 BCL `SafeProcessHandle` 代替裸 nint |
| ㉓ | `infrastructure/Infrastructure/IO/Services/Terminal/Core/TerminalCaptureService.cs:368` | int POSIX fd | ⏸️ | Linux 代码（项目主要 Windows），需创建 SafeFdHandle |
| ㉔ | `core/execution/Hands/src/ToolHandlers/Handlers/DesktopTools/DesktopOverlayToolHandlers.cs:49` | IntPtr GDI DC | ⏸️ | ReleaseDC 需多参数（hWnd + hDC），SafeHandle 难封装 |
| ㉕ | `core/execution/Hands/src/Desktop/PulseOverlay/DesktopPulseOverlay.cs:178` | PAINTSTRUCT | ⏭️ | EndPaint 需 `ref ps`，SafeHandle 难携带状态 |
| ㉖ | `core/execution/Hands/src/Desktop/GdiScreenCaptureService.cs:81` | GCHandle struct | ⏭️ | struct 不实现 IDisposable，无法 using |
| ㉗ | `core/execution/Hands/src/Desktop/GdiScreenCaptureService.cs:98` | 多 GDI 句柄交错 | ⏭️ | 多资源交错释放，需多个 SafeHandle 协作 |

## 已完成记录

| 提交 | 内容 |
|------|------|
| `eb3dfcfbd` | GUI 窗口关闭释放引擎资源（MainViewModel + MainWindow） |
| `af92a0c3f` | CLI 主路径退出释放 host |
| `f7ac38056` | host 释放统一改 using 单行模式（Program/McpCommand/DoctorSubCommand/Tui） |
| `dcc7f34e5` | 全局 JobObject 防护子进程孤儿化 + SSH 转发心跳保活 |
| `717b9d980` | try-finally+Dispose 统一改 using（第一批 13 处） |
| `ac5d85b0b` | ProcessToolHandlers foreach → for+using（第二批 ⑲） |
| `ed98fd8f2` | WindowsJobObjectSandbox SafeProcessHandle（第三批 ㉒） |

## 测试验证

| 测试项目 | 通过 | 失败 |
|----------|------|------|
| Mcp.Tests | 198 | 0 |
| Hands.ToolHandlers.Tests | 405 | 0 |
| Agents.Tests | 527 | 0 |
| Brain.Context.Tests | 785 | 0 |
| JoinCodeGui.Tests | 411 | 0 |
| Host.Tests | 909 | 0 |
| Composition.Tests | 87 | 0 |
| Infra.IO.Tests | 132 | 0 |
| Infra.Services.Tests | 490 | 0 |
| **合计** | **3944** | **0** |

## 统计

- 已改：**15 处**（13 + 1 + 1）
- 保持现状：**9 处**（5 null 条件 + 2 参数变量 + 2 Win32 Handle），都有特别理由
- 测试：3944 通过，0 失败

<!-- 🤖 Auto Decision: 2026-09-06 -->
<!-- 决策: null 条件 Dispose 保持 try-finally，不引入 NullAsyncDisposable 哨兵 -->
<!-- 原因: ITelemetrySpan? 后续需调 .SetTag，await using 会丢失类型信息；null 安全用 if (x is not null) 更直观 -->
<!-- 替代方案: 引入 NullTelemetrySpan 公共类型 + ITelemetrySpan.Null 实例，但改造成本高收益小 -->
<!-- 验证: 编译通过，3944 测试全部通过 ✅ -->
