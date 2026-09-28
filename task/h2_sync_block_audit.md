# H2 同步阻塞审计清单

扫描日期: 2026-09-29
总计: 69 处 `.GetAwaiter().GetResult()`，0 处 `.Wait()`，0 处 Task `.Result`

## 分类总览

| 类别 | 数量 | 风险 | 说明 |
|------|------|------|------|
| A. IFileSystem 写/读 | 18 | 中 | 同步方法中调用异步 IFileSystem |
| B. Lambda 同步委托 | 12 | 高 | 同步委托包装异步操作，接口约束 |
| C. GUI/TUI 阻塞 | 10 | 中 | GUI/TUI 线程中的异步操作 |
| D. TCS Task 等待 | 5 | 低 | 等待 TaskCompletionSource |
| E. Task.Run(...) | 5 | 中 | 同步上下文中运行异步操作 |
| F. ITerm/Tmux Backend | 6 | 高 | 终端后端同步化 |
| G. Pipeline/Process 执行 | 4 | 高 | 管道/进程执行阻塞 |
| H. 配置/定义加载 | 3 | 高 | 启动时加载阻塞 |
| I. Actor Ask/Reply | 1 | 高 | 违反双 Tell 无 Ask 架构 |
| J. 其他 | 5 | 中 | 杂项 |

## 详细清单

### A. IFileSystem 写/读阻塞（18 处）

同步方法中调用异步 IFileSystem API。改法：方法改 async + await。

| 文件 | 行 | 代码 |
|------|-----|------|
| kit/brain/context/services/context/ChatFileContextService.cs | 71 | `_fs.WriteAllText(filePath, sb.ToString())` |
| kit/hands/desktop/services/MacroRecorder.cs | 109 | `_fileSystem.WriteAllText(filePath, json)` |
| kit/hands/system_actuator/abstractions/ProcessOutputCollector.cs | 24 | `_fs.AppendAllText(...)` |
| kit/hands/system_actuator/abstractions/ProcessOutputCollector.cs | 47 | `_fs.WriteAllText(...)` |
| kit/hands/system_actuator/abstractions/ProcessOutputCollector.cs | 59 | `_fs.ReadAllText(...)` |
| kit/hands/system_actuator/instances/BashSystemActuator.cs | 225 | `fs.WriteAllText(snapshotPath, output)` |
| kit/mcp_tool_dispatch/core/execution/ToolHealthMonitor.cs | 337 | `_fs.WriteAllText(_configPath, json)` |
| kit/slash/agents/agent/MemoryCommand.cs | 230 | `fs.WriteAllText(path, "")` |
| lib/guard/configuration/services/BriefModeService.cs | 122 | `_fs.WriteAllText(path, json)` |
| lib/vault/state/transcript/core/TranscriptFileWriter.cs | 40 | `_fs.WriteAllText(probePath, "p")` |
| app/cli/core/commands/core/Program.cs | 171 | `SafeFileIO.WriteAllText(errorLog, errorContent)` |
| app/cli/core/commands/core/Program.cs | 379 | `SafeFileIO.WriteAllText(jsonPath, sb.ToString())` |
| app/cli/core/commands/core/Program.cs | 407 | `SafeFileIO.WriteAllText(txtPath, txt.ToString())` |
| app/cli/core/trust/TrustFolderManager.cs | 88 | `_fs.WriteAllText(_trustedFoldersPath, json)` |
| app/cli/entry/startup/core/ReplLoopStep.cs | 271 | `SafeFileIO.WriteAllText(errorLog, errorContent)` |
| app/cli/entry/startup/non_interactive/NonInteractiveExecuteStep.cs | 79 | `SafeFileIO.WriteAllText(errorLog, errorContent)` |
| lib/infrastructure/io/file_system/SyncFileReader.cs | 10,15,20,25,30 | 5 处同步读封装 |

### B. Lambda 同步委托阻塞（12 处）

同步委托 `() => xxxAsync().GetAwaiter().GetResult()` 包装异步操作。改法：接口改异步签名。

| 文件 | 行 | 代码 |
|------|-----|------|
| kit/slash/transport/BridgeMainCommand.cs | 189 | `GetAccessToken = () => GetAccessTokenAsync().GetAwaiter().GetResult()` |
| kit/slash/transport/BridgeMainCommand.cs | 208 | `GetAccessToken = () => GetAccessTokenAsync().GetAwaiter().GetResult()` |
| kit/slash/transport/BridgeMainCommand.cs | 210 | `CheckRemoteDialogAccepted = () => ...Async().GetAwaiter().GetResult()` |
| kit/slash/transport/BridgeMainCommand.cs | 211 | `MarkRemoteDialogSeen = () => ...Async().GetAwaiter().GetResult()` |
| lib/abstractions/abs_ai/prompts/system_prompt/SystemPromptSection.cs | 43 | `() => computeAsync().GetAwaiter().GetResult()` |
| lib/abstractions/abs_ai/prompts/system_prompt/SystemPromptSection.cs | 57 | `() => computeAsync().GetAwaiter().GetResult()` |
| server/bridge/transport/v1/init/V1EnvRegistrationMiddleware.cs | 23 | `() => ctx.Parameters.GetTrustedDeviceToken().GetAwaiter().GetResult()` |
| kit/hands/integration/core/ChromeIntegrationService.cs | 38 | `Task.Run(() => ...FindExecutableAsync("chrome")).GetAwaiter().GetResult()` |
| kit/hands/integration/core/ChromeIntegrationService.cs | 64 | `Task.Run(() => ReadDefaultEnabledAsync()).GetAwaiter().GetResult()` |
| kit/hands/integration/core/DesktopHandoffService.cs | 28 | `Task.Run(() => ...FindExecutableAsync("jcc-desktop")).GetAwaiter().GetResult()` |
| kit/hands/integration/core/IdeIntegrationService.cs | 286 | `Task.Run(() => ...FindExecutableAsync(command)).GetAwaiter().GetResult()` |
| kit/prompts/sections/system/EnvironmentSection.cs | 102 | `Task.Run(() => processService.ExecuteAsync(options)).GetAwaiter().GetResult()` |

### C. GUI/TUI 阻塞（10 处）

GUI/TUI 线程中的异步操作。改法：async void 事件处理器或 async Task。

| 文件 | 行 | 代码 |
|------|-----|------|
| app/gui/core/App.axaml.cs | 53 | `t.GetAwaiter().GetResult()` |
| app/gui/core/App.axaml.cs | 73 | `WriteAllText(...).GetAwaiter().GetResult()` |
| app/gui/core/App.axaml.cs | 86 | `WriteAllText(...).GetAwaiter().GetResult()` |
| app/gui/hosting/PlaceholderChatSession.cs | 35 | `configService.GetAsync(...).GetAwaiter().GetResult()` |
| app/gui/views/core/MainWindow.axaml.cs | 144 | `task.GetAwaiter().GetResult()` |
| app/gui/view_models/t_to_z/ViewModelDiagnosticsLogger.cs | 12 | `WriteAllText(...).GetAwaiter().GetResult()` |
| app/gui/view_models/t_to_z/ViewModelDiagnosticsLogger.cs | 25 | `WriteAllText(...).GetAwaiter().GetResult()` |
| app/tui/core/program/Program.cs | 19,23,28,45 | 4 处 |
| app/tui/core/services/TuiModeRunner.cs | 339,436,539 | 3 处 |

### D. TCS Task 等待（5 处）

等待 TaskCompletionSource。改法：改 async + await tcs.Task。

| 文件 | 行 | 代码 |
|------|-----|------|
| app/cli/adapters/CliPermissionConfirmationHandler.cs | 45 | `tcs.Task.GetAwaiter().GetResult()` |
| app/cli/core/display/ConsoleActor.cs | 94,104,113,128 | 4 处 |

### E. ITerm/Tmux Backend 阻塞（6 处）

终端后端同步化。改法：接口改 async。

| 文件 | 行 | 代码 |
|------|-----|------|
| llm/agents/Coordinator/Backend/ITerm2PaneBackend.cs | 52,79,112,133,160 | 5 处 |
| llm/agents/Coordinator/Backend/TmuxPaneBackend.cs | 227 | 1 处 |

### F. Pipeline/Process 执行阻塞（4 处）

管道/进程执行阻塞。改法：方法改 async + await。

| 文件 | 行 | 代码 |
|------|-----|------|
| app/cli/core/services/CliSession.cs | 175 | `spawnPipeline.ExecuteAsync(context, default)` |
| lib/clock/goal/core/goal_engine/GoalEngine.cs | 977 | `spawnPipeline.ExecuteAsync(context, default)` |
| lib/guard/security/power_shell/ast/PsAstParser.cs | 82 | `processService.ExecuteAsync(options)` |
| kit/hands/build/BuildQueueRouter.cs | 37 | `...AsTask().GetAwaiter().GetResult()` |

### G. 配置/定义加载阻塞（3 处）

启动时加载阻塞。改法：改 async 初始化。

| 文件 | 行 | 代码 |
|------|-----|------|
| kit/mcp/mcp_protocol/McpTcpServer.cs | 185 | `WriteResponseAsync(...)` |
| kit/mcp/core/commands/core/JccMcpServer.cs | 28 | `_registry.GetAllToolsAsync(...)` |
| llm/agents/Services/Support/AgentRoleProfileRegistry.cs | 139 | `_definitionProvider.GetAgentDefinitionsAsync()` |

### H. Actor Ask/Reply 阻塞（1 处）

违反双 Tell 无 Ask 架构。改法：改双 Tell 协议。

| 文件 | 行 | 代码 |
|------|-----|------|
| kit/prompts/services/MagicDocsManager.cs | 134 | `_actor.AskReplyAsync(reply).GetAwaiter().GetResult()` |

### I. 其他（5 处）

| 文件 | 行 | 代码 |
|------|-----|------|
| lib/infrastructure/io/services/terminal/ReplService.cs | 148 | `_processService.FindExecutableAsync(candidate)` |
| lib/infrastructure/reaper_scheduler/ReaperScheduler.cs | 76 | `ScanOnce()` |
| llm/agents/Services/Support/AgentMemoryService.cs | 184 | `GitWorkspaceResolver.FindGitRootAsync(...)` |
| server/bridge/transport/v1/init/V1EnvRegistrationMiddleware.cs | 23 | `GetTrustedDeviceToken()` |
| lib/abstractions/abs_ai/prompts/system_prompt/SystemPromptSection.cs | 43,57 | SystemPromptSection 同步计算委托 |

## 改造优先级

1. **P0（高危，先改）**: H. Actor Ask/Reply → 双 Tell 协议
2. **P1（高危）**: F. Pipeline/Process 执行 + G. 配置加载 + E. ITerm/Tmux Backend
3. **P2（中危）**: B. Lambda 同步委托（接口改异步签名，影响面大）
4. **P3（中危）**: A. IFileSystem 写/读（方法改 async，局部影响）
5. **P4（低危）**: C. GUI/TUI + D. TCS Task + I. 其他
