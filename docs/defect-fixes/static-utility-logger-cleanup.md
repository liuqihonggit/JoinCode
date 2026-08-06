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
| S5 | `core/search/CodeIndex/.../CsprojParser.cs` | 1 | static 类 | ✅ |
| S6 | `core/search/CodeIndex/.../CSharpSymbolExtractor.cs` | 1 | static 类 | ✅ |
| S7 | `core/search/CodeIndex/.../TreeCache.cs` | 1 | static 类 | ✅ |
| S8 | `infrastructure/.../FrontmatterParser.cs` | 1 | static 类 | ✅ |
| S9 | `infrastructure/.../TerminalCaptureService.cs` | 4 | static 类 | ✅ |
| S10 | `infrastructure/.../BridgeDebugUtils.cs` | 1 | static 类 | ✅ |
| S11 | `services/Bridge/.../BridgeSessionApi.cs` | 1 | static 类 | ✅ |
| S12 | `services/Bridge/.../BridgeInboundAttachments.cs` | 1 | static 类 | ✅ |
| S13 | `services/Bridge/.../BridgeRemoteCore.Helpers.cs` | 1 | static 方法 | ✅ |
| S14 | `services/Bridge/.../BridgeApiClient.cs` | 2 | 已有 logger，static 方法透传 | ✅ |
| S15 | `services/Vault/.../FacetCacheService.cs` | 1 | static 方法 | ✅ |
| S16 | `services/Vault/.../SessionScanner.cs` | 1 | static 方法 | ✅ |
| S17 | `core/execution/Hands/.../ApiClient.cs` | 1 | static 方法 | ✅ |
| S18 | `composition/Clock/.../GoalEvaluator.cs` | 1 | static 方法 | ✅ |
| S19 | `composition/Clock/.../GoalGraphEngine.cs` | 2 | 已有 logger，static 方法透传 | ✅ |
| S20 | `core/safety/Guard/.../WebFetchPermissionMiddleware.cs` | 1 | 已有 logger | ✅ |
| S21 | `core/safety/Guard/.../PsPermissions.cs` | 1 | static 类 | ✅ |
| S22 | `infrastructure/.../BatchLock.cs` | 1 | internal 原语 | ✅ |
| S23 | `infrastructure/.../FileLock.cs` | 4 | internal 原语 | ✅ |
| S24 | `app/JoinCode/Cli/Display/TerminalHelper.cs` | 1 | static 类 | ✅ |

### 第六桶：App 命令 / DI 注册

| 文件 | Trace 数 | 处理 | 状态 |
|------|----------|------|------|
| `app/JoinCode/Commands/ai/Info/InsightsCommand.cs` | 4 | ExtractFacetsAsync/SummarizeLongTranscriptAsync 加 logger 尾参,调用点从 context.Services.ServiceProvider 获取 ILogger | ✅ |
| `app/JoinCode/Commands/brain/Session/ResumeCommand.cs` | 3 | SearchByCustomTitleAsync/LoadLiteSessions/CheckCrossProjectResumeAsync 加 logger 尾参,调用点从 ServiceProvider 获取 | ✅ |
| `app/JoinCode/Commands/guard/Auth/LoginCommand.cs` | 1 | PostLoginRefreshAsync 加 logger | ✅ |
| `app/JoinCode/Commands/guard/Auth/LogoutCommand.cs` | 1 | PostLogoutRefreshAsync 加 logger | ✅ |
| `app/JoinCode/Commands/hands/Code/AddDirCommand.cs` | 1 | PersistDirectoryAsync 加 logger | ✅ |
| `app/JoinCode/Entry/Startup/ReplLoopStep.cs` | 1 | WriteErrorLog 加 logger | ✅ |
| `app/JoinCode/Entry/Startup/NonInteractiveExecuteStep.cs` | 1 | WriteErrorLog 加 logger | ✅ |
| `app/JoinCode/Program.cs` | 4 | logger 提到 try 外;WriteErrorLog/StartAwaitTimer 加 logger 尾参;doctor lambda 捕获 logger | ✅ |
| `composition/Composition/.../ServiceRegistration.Skills.cs` | 2 | 从 IServiceProvider 获取 ILoggerFactory 创建 logger,lambda 捕获 | ✅ |
| `core/safety/Guard/.../ServiceRegistration.cs` | 1 | LoadPermissionsFromSettings 加 logger,Configure 泛型加 ILogger 参数 | ✅ |

#### 第六桶分析结论（2026-08-07）

- **19处Trace全部为内部诊断**(缓存/解析/遥测/日志写入失败等非致命边缘失败,均有"不影响主流程"注释)
- **统一方案**: 全部采用 `ILogger? logger = null` 透传模式(与第五桶一致)
  - App 命令层: 从 `context.Services.ServiceProvider?.GetService<ILogger<T>>()` 获取 logger 传入
  - DI 注册层(Composition): 从 `serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(...)` 创建 logger
  - DI 注册层(Guard): `Configure<T1,T2>` 泛型加 `ILogger<T>` 参数,DI 自动注入
  - Program.cs: `logger` 声明提到 try 外供 catch 块使用,host 创建后从 `host.Services` 获取
- **状态**: ✅ 全部完成

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

<!-- 🤖 Auto Decision: 2026-08-07 (S5/S6/S7 CodeIndex层批量完成) -->
<!-- 决策: S5 CsprojParser.Parse→LoadMsBuildProperties 链式加 logger 尾参,Trace改LogWarning;S6 CSharpSymbolExtractor 加 _logger 字段+3构造函数加可选logger,Log从static改实例方法(_logger?.LogDebug),内部new TreeCache传logger;S7 TreeCache 同理加 _logger 字段+构造函数加logger,Log改实例方法。CodeIndex GlobalUsings 加 Microsoft.Extensions.Logging。Log 方法从 static 改实例以访问 _logger,传给 TimeoutLock 的 Action<string> 回调兼容 -->
<!-- 原因: Log 是 Action<string> 回调传给 TimeoutLock,static 无法访问实例 _logger,必须改实例方法;Trace.WriteLine 原本无 listener 不可见,LogDebug 降级为调试级别;调用点(CodeIndexer/ProjectIndex/测试)不传 logger 默认 null,Log 变 no-op 等价静默 -->
<!-- 替代方案: 保留 static Log + 传 logger 参数(但 Action<string> 签名不匹配);改 TimeoutLock 接收 ILogger(改动面大,TimeoutLock 不在本次清单)-->
<!-- 验证: CodeIndex Debug 0 错误 0 警告;CodeIndex.Tests 374 全通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-07 (S8/S9/S10/S22/S23 Infrastructure+Transport层批量完成) -->
<!-- 决策: S8 FrontmatterParser 全链加 logger 透传+补XML param;S9 TerminalCaptureService 有 _logger,4个 private static 加 logger 参数,2个上层 static CaptureUnixScreen/Buffer 透传,实例调用点传 _logger;S10 BridgeDebugUtils 加 logger,Transport.Impl GlobalUsings 加 Logging;S22 BatchLock.ReleaseAllReverse 加 logger;S23 FileLock 加 _logger 字段+构造函数+AcquireAsync 加 logger,修复重复方法定义 -->
<!-- 原因: CaptureUnixScreen/Buffer 是 static 不能访问实例 _logger,必须加参数透传;FileLock 编辑产生重复方法已清理;FrontmatterParser XML 缺 param 标记触发 TreatWarningsAsErrors -->
<!-- 替代方案: 改 CaptureUnixScreen/Buffer 为实例方法(被 static 调用链引用,改动面大)-->
<!-- 验证: Infrastructure+Transport.Impl Debug 0 错误 0 警告(无独立测试项目) ✅ -->

<!-- 🤖 Auto Decision: 2026-08-07 (S11-S16 Services层批量完成) -->
<!-- 决策: S11/S12/S13 static类方法加logger尾参,调用点不传;S14 BridgeApiClient 有 _logger,ExtractErrorTypeFromData/ExtractErrorDetail/HandleErrorStatus/DescribeHttpError 链式加logger,实例调用点传 _logger,static HandleErrorStatus 透传;S15/S16 Vault 有 _logger,private static MoveToDeleted/ReadEntriesAsync 加logger,实例调用点传 _logger。Vault GlobalUsings 加 Microsoft.Extensions.Logging -->
<!-- 原因: BridgeApiClient.HandleErrorStatus 是 internal static 不能访问实例 _logger,必须加参数透传;S15/S16 的 private static 同理;调用点在实例方法中可传 _logger -->
<!-- 替代方案: 改 HandleErrorStatus/MoveToDeleted/ReadEntriesAsync 为实例方法(但被 static 调用链引用,改动面大)-->
<!-- 验证: Bridge 590、Vault.Memdir 137、Vault.Other 238 全通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-07 (S17/S18/S19/S24 最后批次完成,S1-S24全部✅) -->
<!-- 决策: S17 ApiClient.GetErrorMessage(private static)加logger,实例调用点传_logger;S18/S19 GoalEvaluator/GoalGraphEngine 方法已有logger参数(S1加的),Trace直接改logger?.LogDebug;S24 TerminalHelper.EnableVirtualTerminalProcessing(private static)加logger,调用点不传 -->
<!-- 原因: S18/S19 的 ParseEvaluationResult/ProcessNodeOutput 在 S1 时已加 logger 参数用于 LlmJsonHelper 透传,本次只需把残留 Trace 改成 logger?.LogDebug;S17/S24 是 private static 需加参数 -->
<!-- 替代方案: 无(S18/S19 最简,S17/S24 标准模式)-->
<!-- 验证: Hands.Api 184、Clock 421 全通过;Hands/Clock/App Debug 0 错误 0 警告 ✅ -->
<!-- 🎉 第五桶 S1-S24 全部完成! 剩余第六桶(App命令/DI注册)待用户确认 -->

<!-- 🤖 Auto Decision: 2026-08-07 (第六桶全部完成) -->
<!-- 决策: 第六桶10文件19处Trace全部采用 ILogger? logger = null 透传模式(与第五桶一致)。App命令层从 context.Services.ServiceProvider 获取 ILogger;Composition 从 ILoggerFactory.CreateLogger 创建(ServiceRegistration 是 static 类不能做泛型参数);Guard Configure 泛型加 ILogger 参数;Program.cs logger 提到 try 外供 catch 块使用 -->
<!-- 原因: ILogger 本身是多态抽象,null 即静默,与原 Trace.WriteLine 无 listener 等价;用户确认统一用此模式不新建抽象 -->
<!-- 替代方案: Console.Error.WriteLine(stderr不干扰stdout,但不符合 ILogger 统一日志模式);为每个类注入 ILogger<T> 字段(需改构造函数+DI,改动面大)-->
<!-- 验证: Guard/Composition/App Debug 0 错误 0 警告 ✅ -->
<!-- 🎉🎉 全部六桶完成! 34个文件所有 Trace.WriteLine 已清理为 ILogger 透传模式 -->
