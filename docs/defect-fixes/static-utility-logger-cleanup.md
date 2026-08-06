# 静态工具类 Trace.WriteLine → ILogger 清理任务

> 创建时间: 2026-08-06
> 前置: rebase main + 合并 w2 分支完成，工作区干净
> 依据: 前四桶完成实例类清理（57 文件）后，剩余的 static 类/方法中 `System.Diagnostics.Trace.WriteLine`（无 listener 不可见）共 34 个 src 文件。

## 目标

将静态类/静态方法内的 `Trace.WriteLine` 改为可见日志，沿用已建立的两条模式：
1. **静态方法透传 `ILogger? logger = null` 参数**（与 `DotEnvConfig.LoadFrom`、`MobileConnectService.HandleClientAsync`、`ShellExecutionMiddleware.CreateProgressTimer` 一致）
2. **已有 logger 实例类** 内静态方法 → 透传 `_logger`

## 执行顺序（每次一个文件：红测试 → 修改 → 编译 → 绿测试 → 文档 → 提交）

### 第五桶：静态工具类（核心）

| 序号 | 文件 | Trace 数 | 处理 | 状态 |
|------|------|----------|------|------|
| S1 | `foundation/Abstractions/.../Utils/LlmJsonHelper.cs` | 11 | static 类，public 方法加可选 `ILogger? logger = null`，private 透传；19 调用点传 `_logger` | ✅ |
| S2 | `core/safety/Guard/.../ConfigLoader.cs` | 6 | static 类，方法加 logger 参 | ✅ |
| S3 | `core/safety/Guard/.../SettingsLoader.cs` | 1 | static 类 | ✅ |
| S4 | `core/safety/Guard/.../PathValidator.cs` | 2 | static 类 | ✅ |
| S5 | `core/search/CodeIndex/.../CsprojParser.cs` | 1 | static 类 | ⏳ |
| S6 | `core/search/CodeIndex/.../CSharpSymbolExtractor.cs` | 1 | static 类 | ⏳ |
| S7 | `core/search/CodeIndex/.../TreeCache.cs` | 1 | static 类 | ⏳ |
| S8 | `infrastructure/.../FrontmatterParser.cs` | 1 | static 类 | ⏳ |
| S9 | `infrastructure/.../TerminalCaptureService.cs` | 4 | static 类 | ⏳ |
| S10 | `infrastructure/.../BridgeDebugUtils.cs` | 1 | static 类 | ⏳ |
| S11 | `services/Bridge/.../BridgeSessionApi.cs` | 1 | static 类 | ⏳ |
| S12 | `services/Bridge/.../BridgeInboundAttachments.cs` | 1 | static 类 | ⏳ |
| S13 | `services/Bridge/.../BridgeRemoteCore.Helpers.cs` | 1 | static 方法 | ⏳ |
| S14 | `services/Bridge/.../BridgeApiClient.cs` | 2 | 已有 logger，static 方法透传 | ⏳ |
| S15 | `services/Vault/.../FacetCacheService.cs` | 1 | static 方法 | ⏳ |
| S16 | `services/Vault/.../SessionScanner.cs` | 1 | static 方法 | ⏳ |
| S17 | `core/execution/Hands/.../ApiClient.cs` | 1 | static 方法 | ⏳ |
| S18 | `composition/Clock/.../GoalEvaluator.cs` | 1 | static 方法 | ⏳ |
| S19 | `composition/Clock/.../GoalGraphEngine.cs` | 2 | 已有 logger，static 方法透传 | ⏳ |
| S20 | `core/safety/Guard/.../WebFetchPermissionMiddleware.cs` | 1 | 已有 logger | ✅ |
| S21 | `core/safety/Guard/.../PsPermissions.cs` | 1 | static 类 | ✅ |
| S22 | `infrastructure/.../BatchLock.cs` | 1 | internal 原语 | ⏳ |
| S23 | `infrastructure/.../FileLock.cs` | 4 | internal 原语 | ⏳ |
| S24 | `app/JoinCode/Cli/Display/TerminalHelper.cs` | 1 | static 类 | ⏳ |

### 第六桶：App 命令 / DI 注册（保留 Trace 待用户确认）

| 文件 | Trace 数 | 原因 |
|------|----------|------|
| `app/JoinCode/Commands/ai/Info/InsightsCommand.cs` | 4 | App 命令层 |
| `app/JoinCode/Commands/brain/Session/ResumeCommand.cs` | 3 | App 命令层 |
| `app/JoinCode/Commands/guard/Auth/LoginCommand.cs` | 1 | App 命令层 |
| `app/JoinCode/Commands/guard/Auth/LogoutCommand.cs` | 1 | App 命令层 |
| `app/JoinCode/Commands/hands/Code/AddDirCommand.cs` | 1 | App 命令层 |
| `app/JoinCode/Entry/Startup/ReplLoopStep.cs` | 1 | 入口 |
| `app/JoinCode/Entry/Startup/NonInteractiveExecuteStep.cs` | 1 | 入口 |
| `app/JoinCode/Program.cs` | 4 | 入口 |
| `composition/Composition/.../ServiceRegistration.Skills.cs` | 2 | DI 注册 |
| `core/safety/Guard/.../ServiceRegistration.cs` | 1 | DI 注册 |

## 验证基线

- 每批：所属 slnx Debug `--no-incremental` 编译 0 错误 0 警告
- 受影响套件全绿（基线见 round3：Bridge 590、Mcp 139、Infra 561、Hands 622、Agents 265、Clock 421、Guard 444、Vault 375、CodeIndex 374、Scheduling 257、Host 707）

## 决策记录

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 静态类/静态方法沿用"透传 ILogger? 参数"模式清理 Trace；App 命令层与 DI 注册文件单独列为第六桶待用户确认 -->
<!-- 原因: 与 DotEnvConfig.LoadFrom 等既有模式一致，零 DI 破坏，AOT 安全；App 命令层 Console 场景 Trace 是否需日志待确认 -->
<!-- 替代方案: 静态类改实例类（破坏 19 个调用点，改动面大，不采用）-->
<!-- 验证: S1 已全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-06 (S1 完成) -->
<!-- 决策: S1 LlmJsonHelper 采用 public 方法加可选 ILogger? logger = null、private 透传；static 调用点加 logger 参并让非 static 调用点传 _logger；无 logger 可用处不传（BootstrapAgent 静态 ParseJudgment 仅加参） -->
<!-- 原因: 与既有模式一致，static 方法不可访问实例 _logger，必须显式透传；调用点升级为"能传就传"，保证日志不丢 -->
<!-- 替代方案: 调用点全部不传 logger（丢失全库 LLM JSON 诊断，不采用）-->
<!-- 验证: Foundation/Core/Services/Clock 全部 0 错误 0 警告；Reasoning 280、Agents 265、Clock 421、Mcp 139、Brain.Context 725、McpToolDispatch 198、Guard.Hooks 206 全部通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-06 (S2 完成) -->
<!-- 决策: S2 ConfigLoader 6个 static 方法加可选 ILogger? logger = null 尾参（在 cancellationToken 之后），6个 Trace.WriteLine 改 logger?.LogWarning(ex, ...)；ConfigurationService 5个调用点传 _logger；DotEnvConfig/ExecutionSettingsProvider/ProviderSetupStep/StartupWorkflow/Tests 无 logger 字段不传（可选参数默认 null，行为等价） -->
<!-- 原因: ConfigLoader 是实例类但 static 方法无法访问实例字段，必须显式透传；文件损坏 catch 块属警告级别；无 logger 调用点保持静默与原 Trace 不可见等价 -->
<!-- 替代方案: 给 ConfigLoader 加实例 _logger 字段（但 static 方法仍无法访问，不解决问题）；调用点全部传 null（多此一举，可选参数已默认 null）-->
<!-- 验证: Guard Debug 0 错误 0 警告；Guard.Config 783 全通过；App Debug 0 错误 0 警告 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-07 (S3/S4/S20/S21 Guard层批量完成) -->
<!-- 决策: S3 SettingsLoader.LoadAllSourcesAsync 加 logger 尾参;S4 PathValidator 两个 private static NormalizePath/NormalizePathWithBase 加 logger;S20 WebFetchPermissionMiddleware.ExtractWebFetchRuleContent(private static) 加 logger;S21 PsPermissions.CheckPermission(public static)→HandleParseFailure→MatchPrefixRule 链式透传 logger。所有调用点因类无 _logger 不传(可选默认null),PathValidator/WebFetch 内部 private 调用不传 -->
<!-- 原因: 4文件均无实例 _logger 字段,private static 无法访问实例字段;Trace 原本不可见(无 listener),logger null 等价静默;API 支持 logger 供未来注入 -->
<!-- 替代方案: 给 PathValidator/WebFetchPermissionMiddleware 加 _logger 字段(需改构造函数+DI,改动面大,留后续);PsPermissionChecker 加 _logger 传给 CheckPermission(同样需 DI 改动)-->
<!-- 验证: Guard Debug 0 错误 0 警告;Guard.Config 783、Guard.Security 4、Guard.Hooks 206 全通过 ✅ -->
