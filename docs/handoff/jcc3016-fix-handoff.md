# JCC3016 修复工作交接文档

> 创建时间: 2026-09-23
> 最后更新: 2026-09-23
> 状态: JCC3016 已修复并提交，GetAwaiterPatternDetector 共享检测逻辑已就位，待修复 ast_cli 和语法分析器项目位置

## 1. 任务背景

### 起因
CI 失败根因: `E2eSettingsJsonHelper.WriteSettingsJsonToStateDir` 是 void 方法，内部调用 `SafeFileIO.WriteAllText`（返回 ValueTask）未 await — fire-and-forget 异步写入竞态条件。

### 演变
1. 实现 5 条分析器规则 JCC3015-3019 检测异步误用
2. Actor 模型异步化、transport 死锁修复、EmitAsync 重设计
3. 用户放弃同步化改造，改为逐个重新启用分析器修复违规
4. 创建共享工程 AotSafety.Shared，实现"同检同换"（检测和修复共享同一套逻辑）
5. JCC3016 已重新启用，需修复 9 个违规

## 2. 分析器规则说明

| 规则 | 检测 | 状态 |
|------|------|------|
| JCC3015 | async 方法内裸语句丢弃 Task | 启用 |
| JCC3016 | 同步方法内调用返回 Task 的方法未消费 | **已启用，待修复违规** |
| JCC3017 | async 方法无 await（async 冗余） | 禁用，待重新启用 |
| JCC3018 | Dispose 方法 fire-and-forget | 启用 |
| JCC3019 | Task 变量未使用 | 禁用，待重新启用 |
| JCC3020 | 同步方法返回 Task.Run 结果 | 启用 |

### JCC3016 检测逻辑
- **位置**: `gen/aot_safety.shared/RuleDetectors/SyncMethodAsyncCallDetector.cs`
- **条件**: 非 async 方法内调用返回 Task/Task 集合的方法，且未通过 await/赋值消费
- **豁免**: Task.Run（后台任务）、泛型方法实例化、lambda/local function 内
- **消费判定**: await 上下文 / 赋值表达式（包括 discard `_ =`）

## 3. 共享工程架构

```
gen/aot_safety.shared/              ← 共享工程（检测逻辑）
├── AotSafety.Shared.csproj
├── AotSafetyHelpers.cs             ← 类型系统辅助（IsTaskType/ReturnsTaskOrTaskCollection/...）
└── RuleDetectors/
    └── SyncMethodAsyncCallDetector.cs  ← JCC3016 纯检测方法

gen/aot_safety.generator/           ← 分析器工程（用 Compile Include 共享源码）
└── rules/concurrency/
    └── SyncMethodAsyncCallRule.cs  ← 调用 SyncMethodAsyncCallDetector

non_deliverables_tools/jcc_audit_ast_cli/  ← ast_cli 工具（用 ProjectReference 引用 shared）
└── fixers/
    └── AnalyzerDrivenFixer.cs      ← 调用 SyncMethodAsyncCallDetector 检测 + 应用修复
```

### 关键约束
- **分析器 DLL 不能有外部 DLL 依赖**: generator 用 `<Compile Include>` 直接包含 shared 源码编译进同一 DLL
- **ast_cli 用 ProjectReference**: ast_cli 不是分析器，运行时有正常依赖解析

## 4. 当前 9 个 JCC3016 违规

通过 `dotnet build` 确认，分布在 4 个项目:

| 项目 | 文件 | 行号 | 违规调用 |
|------|------|------|----------|
| app/gui | App.axaml.cs | 71, 84 | SafeFileIO.AppendAllText/WriteAllText |
| app/gui | ViewModelDiagnosticsLogger.cs | 10, 23 | SafeFileIO.AppendAllText |
| app/tui | Program.cs | 43 | SafeFileIO.AppendAllText |
| app/tui | TuiModeRunner.cs | 537 | SafeFileIO.AppendAllText |
| mock_server.e2e.tests | DualRoleConversationRunner.cs | 758, 1153 | IFileSystem.WriteAllText |
| host.tests | SessionResumeStepTests.cs | 39 | IFileSystem.WriteAllText |

### 修复方式
invocation → invocation.GetAwaiter().GetResult()

## 5. fix-jcc3016 CLI 当前问题

### 问题: 跨项目符号不可用
- `AnalyzerDrivenFixer` 用 MSBuildWorkspace 加载解决方案
- `LoadMetadataForReferencedProjects=false` 时，被引用项目的符号无法解析
  （如 SafeFileIO.AppendAllText 定义在 Infrastructure 项目，gui 项目引用时 GetSymbolInfo 返回 null）
- `LoadMetadataForReferencedProjects=true` 时，尝试构建引用项目，但 JCC3016 是 error 级别导致编译失败，DLL 不生成
- 默认 MSBuildWorkspace 设置加载 104 项目超时（>10 分钟）

### 解决方案: dotnet build 输出驱动
1. 运行 `dotnet build <target>` 捕获 JCC3016 错误输出
2. 解析 `error JCC3016:` 行，提取 (file, line, col)
3. 按文件分组违规位置
4. 对每个文件，用 SyntaxTree 解析（不需要 SemanticModel），按行:列定位 InvocationExpression
5. 替换为 `.GetAwaiter().GetResult()`
6. 写回文件

**优点**:
- 检测由编译器管线完成（分析器调用 SyncMethodAsyncCallDetector），SemanticModel 完整
- 替换只需要 SyntaxTree 按位置定位，不需要 SemanticModel
- 通用性: 任何新工程 `dotnet build` 都会触发分析器
- 幂等性: 已修复的不会产生 JCC3016 错误，不会重复替换

## 6. 已完成并提交的 commit

| commit | 内容 |
|--------|------|
| b414caf5a | E2E 测试 fire-and-forget 修复 |
| 2bb80639d | 5 条分析器规则 JCC3015-3019 实现 |
| c68259aab | 规则精度调整 |
| 5ef544a00 | await foreach 检测 |
| 8208ee7b6 | JCC3020 + Actor 异步化 + transport 死锁修复 |
| 5bf332f80 | JCC3016/3017/3019 禁用 + JCC3015 修复 |
| 3fd269e4c | 回退 Task 字段方案为 fire-and-forget |
| 3fa86f91a | 取消跟踪 build_log.txt |
| a331f1cda | ast_cli sync-async 命令 + 文件夹重构 |
| ffd6ad609 | BuildQueueRouterTests flaky 修复 |
| 4f110ea | 共享工程 AotSafety.Shared 提取 |

## 7. 已修改未提交（工作区脏）

### JCC3016 启用 + 扩展检测
- `SyncMethodAsyncCallRule.cs` — IsEnabledByDefault = true
- `AotSafetyHelpers.cs`（shared）— 新增 ReturnsTaskOrTaskCollection/IsTaskCollectionType
- `SyncMethodAsyncCallRule.cs` — 用 ReturnsTaskOrTaskCollection，IsConsumed 允许赋值

### EmitAsync 重设计
- `EventDispatchMode.cs` — Emit 返回 IReadOnlyList<Task>
- `EventDispatcherTests.cs` — await Task.WhenAll(EventDispatcher.Emit(...))

### 已修复的 JCC3016 违规文件（之前批次，.GetAwaiter().GetResult()）
- lib/guard/o_auth/TokenRefreshScheduler.cs — 改 async + await
- kit/brain/context/services/context/ChatFileContextService.cs
- kit/hands/desktop/services/MacroRecorder.cs
- kit/hands/system_actuator/instances/BashSystemActuator.cs
- kit/mcp_tool_dispatch/core/execution/ToolHealthMonitor.cs
- test/benchmarks/eyes.benchmarks/L1EvaluationTests.cs
- test/benchmarks/eyes.benchmarks/L2EvaluationTests.cs
- test/benchmarks/eyes.benchmarks/PerformanceBenchmarkTests.cs
- kit/hands.tool_handlers.tests/defense/FileBackupNodeTests.cs
- kit/hands.tool_handlers.tests/defense/FileStateGuardNodeTests.cs
- kit/hands.tool_handlers.tests/defense/FormatValidatorNodeTests.cs
- kit/hands.tool_handlers.tests/tools/ApplyPatchLogicTests.cs
- kit/hands.tool_handlers.tests/tools/BriefLogicTests.cs
- kit/hands.tool_handlers.tests/tools/FileEditLogicTests.cs
- kit/slash/agents/agent/MemoryCommand.cs
- app/cli/core/commands/core/Program.cs
- app/cli/core/trust/TrustFolderManager.cs
- app/cli/entry/startup/core/ReplLoopStep.cs
- app/cli/entry/startup/non_interactive/NonInteractiveExecuteStep.cs
- test/unit/infra.tests/Network/downloader/MetadataStoreTests.cs

## 8. 待完成工作

### 优先级 1: 重构 fix-jcc3016 + 修复 9 个违规
1. 重构 `AnalyzerDrivenFixer.cs` 为 `dotnet build` 输出驱动方案
2. 实现幂等性（已修复的不会产生 JCC3016 错误，自动跳过）
3. 运行 fix-jcc3016 批量修复 9 个违规
4. 编译验证通过
5. git commit

### 优先级 2: JCC3017 重新启用
1. `AsyncMethodWithoutAwaitRule.cs` IsEnabledByDefault = true
2. `dotnet build` 检测违规
3. 修复违规（async 方法无 await → 移除 async 或补 await）
4. 编译验证 + commit

### 优先级 3: JCC3019 重新启用
1. `TaskVariableUnusedRule.cs` IsEnabledByDefault = true
2. `dotnet build` 检测违规
3. 修复违规（Task 变量未使用 → 移除或消费）
4. 编译验证 + commit

### 优先级 4: 推送 PR #276
1. git push 到功能分支
2. CI 重新验证

## 9. 关键技术发现

1. **分析器 DLL 不能有外部 DLL 依赖**: 分析器加载到其他项目编译管线时，依赖 DLL 不在同一目录会 FileNotFoundException
2. **MSBuildWorkspace 跨项目符号问题**: LoadMetadataForReferencedProjects=false 时 GetSymbolInfo 返回 null
3. **JCC3016 是 error 级别**: dotnet build 会失败，DLL 不生成，MSBuildWorkspace 找不到 DLL
4. **dotnet build 时分析器能访问完整 SemanticModel**: 检测结果可靠
5. **JCC3017 await using var 漏检 bug**: `await using var x` 是 LocalDeclarationStatementSyntax，不是 UsingStatementSyntax（已修复）

## 10. 关键文件路径

### 共享工程
- `gen/aot_safety.shared/AotSafety.Shared.csproj`
- `gen/aot_safety.shared/AotSafetyHelpers.cs`
- `gen/aot_safety.shared/RuleDetectors/SyncMethodAsyncCallDetector.cs`

### 分析器规则
- `gen/aot_safety.generator/rules/concurrency/SyncMethodAsyncCallRule.cs` (JCC3016)
- `gen/aot_safety.generator/rules/concurrency/AsyncMethodWithoutAwaitRule.cs` (JCC3017)
- `gen/aot_safety.generator/rules/concurrency/TaskVariableUnusedRule.cs` (JCC3019)

### ast_cli
- `non_deliverables_tools/jcc_audit_ast_cli/core/Program.cs` — fix-jcc3016 / analyze-getawaiter / fix-getawaiter-getresult 命令入口
- `non_deliverables_tools/jcc_audit_ast_cli/fixers/AnalyzerDrivenFixer.cs` — JCC3016 修复器（dotnet build 输出驱动）
- `non_deliverables_tools/jcc_audit_ast_cli/fixers/GetAwaiterFixer.cs` — .GetAwaiter().GetResult() 修复器（文件遍历）
- `non_deliverables_tools/jcc_audit_ast_cli/audit/GetAwaiterAnalyzer.cs` — .GetAwaiter().GetResult() 分析器（文件遍历）

### slnx
- `build/sln/Generators.slnx`
- `build/sln/JoinCode.slnx`

---

## 11. GetAwaiterPatternDetector 共享检测逻辑（2026-09-23 新增）

### 修改内容

用户指出检测逻辑写错：**应判断直接包含的函数体（lambda 本身）是否 async，而非外层方法**。

之前 `IsInAsyncMethod` 向上查找最近的 `MethodDeclarationSyntax`，导致 async 方法内的同步 lambda 被错误归类为"异步"。修正后 `IsInAsyncContext` 查找最近的函数体（lambda/anonymous/method），判断该函数体自身的 async 修饰符。

### 修正前后对比

| 位置 | 修正前 | 修正后 |
|------|--------|--------|
| TuiModeRunner:339 (async方法内同步lambda) | 异步方法中（跳过） | **同步函数体中（可修复）** |
| BridgeMainCommand:189 (async方法内同步lambda) | 异步方法中（跳过） | **同步函数体中（可修复）** |

### 共享检测器 API

`gen/aot_safety.shared/RuleDetectors/GetAwaiterPatternDetector.cs`:
- `IsGetAwaiterGetResultPattern(node)` — 纯模式匹配
- `ExtractInnerExpression(node)` — 提取内部表达式
- `GetEnclosingFunction(node)` — 获取直接包含的函数体（lambda/anonymous/method）
- `IsInAsyncContext(node)` — 判断直接包含体是否 async（**非外层方法**）
- `IsFixableViolation(node)` — 可修复判定（同步函数体 + 不在 Main/属性getter 跳过列表）
- `ShouldSkip(node)` — 跳过 Main/属性getter（**不跳过 lambda**）

### 同检同换原则

检测器（`GetAwaiterAnalyzer`）和修复器（`GetAwaiterFixer`）**共享同一套 `IsFixableViolation` 判定**，禁止双套实现导致脱节。

### 当前检测结果

- 93 处 `.GetAwaiter().GetResult()`
  - 同步函数体中: 93 处
  - 异步函数体中: 0 处
  - 可修复: 86 处（跳过 Main 3 处 + 属性 getter 4 处 + SyncFileReader 文件级跳过）
  - 7 处之前被错误归到"异步"的 lambda 现在正确归类为"同步"

### 已提交 commit

| commit | 内容 |
|--------|------|
| 7b5e2807c | fix-jcc3016 dotnet build 输出驱动 + 修复全部 JCC3016 违规 |
| c67bbc685 | analyze-getawaiter 命令 |
| 0871dc1d8 | GetAwaiterPatternDetector 共享检测逻辑 |
| 50aa31880 | 修正检测逻辑：判断直接包含的函数体而非外层方法 |

## 12. 后续待完成：修复 ast_cli 和语法分析器项目位置

### 引用关系优化（已完成）

用户要求"优化引用关系"。已优化：

**优化前**（generator 硬编码每个 shared 源文件）：
```xml
<Compile Include=".../AotSafetyHelpers.cs" />
<Compile Include=".../SyncMethodAsyncCallDetector.cs" />
<Compile Include=".../GetAwaiterPatternDetector.cs" />
```
每新增检测器需手动添加 Compile Include，容易遗漏。

**优化后**（generator 通配符自动包含 RuleDetectors 目录）：
```xml
<Compile Include=".../AotSafetyHelpers.cs" />
<Compile Include=".../RuleDetectors/*.cs" />
```
新增检测器自动包含，无需手动修改 csproj。

### 当前引用关系（最终）

```
gen/aot_safety.shared/           ← 共享检测逻辑（独立 csproj）
├── AotSafetyHelpers.cs
├── GlobalUsings.cs
└── RuleDetectors/
    ├── SyncMethodAsyncCallDetector.cs
    └── GetAwaiterPatternDetector.cs

gen/aot_safety.generator/        ← 分析器工程
├── Compile Include: shared/AotSafetyHelpers.cs + shared/RuleDetectors/*.cs（通配符）
└── 不用 ProjectReference（分析器 DLL 不能有外部依赖）

non_deliverables_tools/jcc_audit_ast_cli/  ← ast_cli 工具
└── ProjectReference: shared（正常引用，运行时有依赖解析）
```

**约束**：
- generator 必须用 Compile Include（分析器 DLL 不能有外部 DLL 依赖，加载时 FileNotFoundException）
- ast_cli 用 ProjectReference（不是分析器，运行时正常解析依赖）
- shared 的 GlobalUsings.cs 不被 generator 包含（generator 有自己的 GlobalUsings.cs）
