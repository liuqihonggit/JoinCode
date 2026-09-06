# using 改造工作计划

> 目标：将 try-finally + Dispose/DisposeAsync 模式统一改为 `using`/`await using` 单行释放
> 原则：每改一处 → 编译 → 提交，渐进式推进

## 第一批：明确可改（13 处，零风险纯替换）

### 1.1 await using 替代 DisposeAsync（3 处）

| # | 文件:行号 | 资源 | 状态 |
|---|-----------|------|------|
| ① | `core/execution/Hands/src/ToolHandlers/Handlers/SystemTools/Web/DownloadToolHandlers.cs:84` | session | ⬜ |
| ② | `composition/Pipelines/src/Middlewares/ChatErrorHandlingMiddleware.cs:49` | enumerator | ⬜ |
| ③ | `core/execution/Brain/src/Context/Services/Chat/StreamCrashSnapshotMiddleware.cs:55` | enumerator | ⬜ |

### 1.2 using 替代 Dispose（2 处）

| # | 文件:行号 | 资源 | 状态 |
|---|-----------|------|------|
'---|
| ④ | `core/ai/Agents/src/Coordinator/DualModel/ModelCoordinator.cs:108` | planner | ⬜ |
| ⑤ | `core/ai/Agents/src/Coordinator/DualModel/ModelCoordinator.cs:151` | executor | ⬜ |

### 1.3 锁守卫 guard.Dispose → using（8 处）

| # | 文件:行号 | 资源 | 状态 |
|---|-----------|------|------|
| ⑥ | `services/Mcp/src/Client/Transport/McpStdioClient.cs:303` | guard | ⬜ |
| ⑦ | `services/Mcp/src/Client/Transport/McpStdioClient.cs:331` | guard2 | ⬜ |
| ⑧ | `services/Mcp/src/Client/Transport/McpNetworkClient.cs:80` | guard | ⬜ |
| ⑨ | `services/Mcp/src/Client/Transport/McpNetworkClient.cs:100` | guard1 | ⬜ |
| ⑩ | `services/Mcp/src/Client/McpClientBase.cs:149` | guard | ⬜ |
| ⑪ | `services/Mcp/src/Client/McpClientBase.cs:186` | guard | ⬜ |
| ⑫ | `services/Mcp/src/Client/Transport/McpFallbackClient.cs:75` | guard | ⬜ |
| ⑬ | `services/Mcp/src/Client/Transport/McpFallbackClient.cs:94` | guard1 | ⬜ |

## 第二批：有条件可改（8 处，需处理 null/foreach/参数变量）

| # | 文件:行号 | 问题 | 状态 |
|---|-----------|------|------|
| ⑭ | `app/JoinCode/Program.cs:116` | doctorClient 条件创建可空 | ⬜ |
| ⑮ | `infrastructure/Infrastructure/Plugins/Services/PluginManager.cs:193` | span 可空 | ⬜ |
| ⑯ | `infrastructure/Infrastructure/Plugins/Services/PluginManager.cs:293` | span 可空 | ⬜ |
| ⑰ | `infrastructure/Infrastructure/IO/Services/FileOps/ThrottledFileService.cs:80` | span 可空（4处: 80/146/224/291） | ⬜ |
| ⑱ | `infrastructure/Infrastructure/Http/ResilientHttpExecutor.cs:75` | totalTimeoutCts 条件创建（4处: 75/87/136/147） | ⬜ |
| ⑲ | `core/execution/Hands/src/ToolHandlers/Handlers/DesktopTools/ProcessToolHandlers.cs:74` | foreach 迭代变量 | ⬜ |
| ⑳ | `core/execution/Hands/src/Network/MobileConnectService.cs:115` | 参数变量 | ⬜ |
| ㉑ | `core/ai/Agents/src/Doctor/DoctorTcpServer.cs:195` | 参数变量 + try-catch 吞异常 | ⬜ |

## 第三批：Win32 Handle → SafeHandle（6 处，架构级改造）

| # | 文件:行号 | Handle 类型 | 状态 |
|---|-----------|-------------|------|
| ㉒ | `infrastructure/Infrastructure/Windows/JobObject/WindowsJobObjectSandbox.cs:114` | nint 进程句柄 | ⬜ |
| ㉓ | `infrastructure/Infrastructure/IO/Services/Terminal/Core/TerminalCaptureService.cs:368` | int POSIX fd | ⬜ |
| ㉔ | `core/execution/Hands/src/ToolHandlers/Handlers/DesktopTools/DesktopOverlayToolHandlers.cs:49` | IntPtr GDI DC | ⬜ |
| ㉕ | `core/execution/Hands/src/Desktop/PulseOverlay/DesktopPulseOverlay.cs:178` | PAINTSTRUCT（建议保持） | ⏭️ |
| ㉖ | `core/execution/Hands/src/Desktop/GdiScreenCaptureService.cs:81` | GCHandle struct（无法 using） | ⏭️ |
| ㉗ | `core/execution/Hands/src/Desktop/GdiScreenCaptureService.cs:98` | 多 GDI 句柄交错（建议保持） | ⏭️ |

## 已完成记录

| 提交 | 内容 |
|------|------|
| `eb3dfcfbd` | GUI 窗口关闭释放引擎资源 |
| `af92a0c3f` | CLI 主路径退出释放 host |
| `f7ac38056` | host 释放统一改 using 单行模式 |
| `dcc7f34e5` | 全局 JobObject 防护子进程孤儿化 + SSH 转发心跳保活 |
