# TASK031 — 守卫补全 + 单数据源统一 + 确定性/时序测试分家

> **来源**: TASK030 基建确定性测试补充的后续增强。用户 5 项要求:
> 1. 补全现有实现守卫不足的地方,尤其是取值范围
> 2. 单一职责化最好是单数据源,必须要相同的情况下才获取,并且补充测试
> 3. 要求确定性测试和时序测试分家
> 4. 参考 task0030 文档,包括验证之前已经完成的部分
> 5. 执行时候并行多个子代理
>
> **创建时间**:2026-09-30
>
> **状态**:待执行

---

## 一、TASK030 已完成部分验证结论(2026-09-30 验证)

**结论:阶段2 + 阶段3 已完成项 100% 落地,无虚假完成。**

- 阶段2(拆分长方法为 internal):33 个子项全部落地,关键 internal 方法签名均经验证存在
- 阶段3(mock 测试):8 个子项全部落地,测试方法数普遍超过清单声明数
- 2 项实现方式与清单字面描述不一致(ActorBase 纯函数改为类抽离、ExecuteUndoChainPure 改为整类 internal),但清单已注明实际方式
- 阶段1.1 structura 的 DagTests.cs(62 测试)和 ConcurrentDagTests.cs(30 测试)实际已存在(清单标 [ ] 未更新)

---

## 二、守卫不足清单(按风险等级排序)

### P0 - 高风险(可能导致崩溃或安全绕过,14 项)

| # | 文件:行号 | 方法 | 缺失检查 |
|---|-----------|------|----------|
| 1 | lib/guard/permission/permission2/tool_handlers/core/PermissionCheckContext.cs:137 | IsSensitivePath | path null/空检查 |
| 2 | lib/guard/permission/permission2/tool_handlers/core/PermissionCheckContext.cs:171 | IsDangerousCommand | command null 检查 |
| 3 | lib/guard/permission/permission2/tool_handlers/core/PermissionCheckContext.cs:185 | MatchesPattern | pattern null 检查 |
| 4 | lib/guard/permission/permission2/tool_handlers/core/PermissionCheckContext.cs:52 | ExtractPathFromArguments | arguments null 检查 |
| 5 | lib/guard/security/services/bash_ast_walker/BashAstSecurityWalker.CollectCommands.cs:4 | CollectCommands | node null 检查 |
| 6 | lib/guard/security/services/bash_ast_walker/BashAstSecurityWalker.WalkCommand.cs:4 | WalkCommand | node null 检查 |
| 7 | lib/guard/security/services/bash_validation/BashPermissionChecker.cs:12,43 | 构造函数+CheckPermission | 依赖 null + workingDirectory null |
| 8 | lib/guard/security/scanners/GitSecretScanner.cs:20 | ScanFileNamesAsync | stagedFiles null 检查 |
| 9 | lib/guard/security/auditing/CommandExecutionAuditor.cs:44,55 | 构造函数+Record | fs/entry null 检查 |
| 10 | lib/transport.impl/shared/TransportHealthCheck.cs:202 | TcpPortHealthCheck 构造函数 | host null + port [0,65535] |
| 11 | lib/structura/dag/Dag.cs:29,45,74 | AddNode/AddEdge/WouldCreateCycle | node/edge/id null 检查 |
| 12 | lib/vault/memdir/memdir2/core/MemoryStore.cs:42,116,129 | AddMemory/GetMemory/DeleteMemory | content/id null 检查 |
| 13 | lib/vault/memdir/services/MemoryRelevanceScorer.cs:13,23 | 构造函数+Calculate | clock/memory/query null |
| 14 | lib/transport.impl/stdio/StdioProcessManager.cs:40,74 | StartAsync/SendAsync | config/message null |
| 15 | server/code_index/core/ContentHash.cs:12,35 | ComputeContentHash/ReadFile | content/filePath/fs null |
| 16 | server/dream/task/DreamTaskPersistence.cs:75,90,138,165 | Save/Load/Delete/GetFilePath | task/taskId null + 路径注入 |

### P1 - 中风险(取值范围缺失,12 项)

| # | 文件:行号 | 方法 | 参数 | 应有范围 |
|---|-----------|------|------|----------|
| 1 | lib/async_lock/actor/ActorBackpressure.cs:38 | 构造函数 | MaxRetries/RetryQueueCapacity | [1, int.MaxValue] |
| 2 | lib/async_lock/actor/ActorBackpressure.cs:38 | 构造函数 | HighWatermark/CriticalWatermark | [0, Capacity] + High<Critical |
| 3 | lib/async_lock/actor/MessageRetryEngine.cs:31 | 构造函数 | retryQueueCapacity/maxRetries | [1, int.MaxValue] |
| 4 | lib/infrastructure/utils/system/CpuParallelism.cs:29 | GetDegree | maxDegree | [1, int.MaxValue] |
| 5 | server/code_index/core/TimeoutLock.cs:19,34,57 | 构造+Acquire | timeout | [Zero, +∞) |
| 6 | lib/transport.impl/stdio/StdioProcessManager.cs:87,135 | WaitForOutput/Error | timeout | [Zero, +∞) |
| 7 | lib/scheduling/services/TaskService.cs:98 | ListTasksAsync | offset/limit | [0, +∞) |
| 8 | lib/scheduling/storage/FileBasedTaskService.cs:131 | ListTasksAsync | offset/limit | [0, +∞) |
| 9 | lib/vault/memdir/memdir2/core/MemoryStore.cs:60,91,105 | Search/SearchByTags/SearchByType | limit | [0, +∞) |
| 10 | lib/vault/memdir/memdir2/search/MemorySearchHistory.cs:304 | GetRecentSearches | limit | [0, +∞) |
| 11 | server/dream/task/DreamTaskPersistence.cs:148 | CleanupCompletedAsync | keepCount | [0, +∞) |
| 12 | lib/plugins.infrastructure/plugins/PluginUnloadOptions.cs:84,92,100 | WithTimeout* | timeout/seconds/ms | [Zero, +∞) |
| 13 | lib/transport.impl/bridge/shared/auth/FlushGate.cs:6 | FlushGateOptions | MaxBatchSize/FlushIntervalMs/MaxWaitMs | [1, int.MaxValue] |
| 14 | lib/infrastructure/io/services/file_ops/FileEditor.cs:720,735 | ExtractOriginalContent/BuildUpdatedFileContent | allLines/startLine/count | null + 范围 |

### P2 - 低风险(阈值/百分比 [0,1] 缺失,4 项)

| # | 文件:行号 | 方法 | 参数 | 应有范围 |
|---|-----------|------|------|----------|
| 1 | lib/guard/security/services/classifiers/AutoModeClassifier.cs:32 | ClassificationResult | Confidence | [0.0, 1.0] |
| 2 | lib/guard/hooks/tool_permission/InteractiveHandler.cs:374 | ShouldAutoApprove | confidence/threshold | [0.0, 1.0] |
| 3 | lib/abstractions/abs_brain/context/resolution/ReferenceResolutionOptions.cs:222,262 | WithMinRelevanceScore/WithFuzzyMatchThreshold | score/threshold | [0.0, 1.0] |
| 4 | lib/abstractions/abs_brain/context/hierarchy/ContextHierarchyOptions.cs:83,77 | WithCompressionRatio/WithMaxLayers | ratio/layers | [0,1] / >0 |

---

## 三、重复数据源清单

### P0 - 高优先级(数据完全重复,3 项)

| # | 重复内容 | 位置列表 | 建议统一到 |
|---|----------|----------|-----------|
| 1 | MIME→扩展名映射(21 项) | McpOutputStorage.cs:108 + MimeTypeExtensionMapper.cs:18 + McpClientToolHandlers.cs:644 | MimeTypeExtensionMapper.cs(switch 性能优) |
| 2 | 二进制 Content-Type 检测 | BinaryContentTypeDetector.cs:13 + McpBinaryHelper.cs:10 | McpBinaryHelper.cs(Span 优化) |
| 3 | 删除命令名列表 | ShellDeleteDetector.cs:16 + CommandDangerClassifier.cs:341 + SwarmPermissionRequestProcessor.cs:160 | DangerousCommandCatalog 新增 FileDeletionCommands 派生属性 |

### P1 - 中优先级(数据部分重复,4 项)

| # | 重复内容 | 位置列表 | 建议统一到 |
|---|----------|----------|-----------|
| 4 | Git 只读子命令白名单 | CommandDangerClassifier.cs:229(25 个) + ReadOnlyCommandDetector.cs:46(38 个) | 新建 GitCommandCatalog.cs |
| 5 | 扩展名→语言映射 | LanguageMapCatalog.cs:16(19 个) + LspFileSync.cs:148(15 个) | LanguageMapCatalog 新增 ExtensionToLspLanguageId |
| 6 | 排除目录列表 | CodeIndexExcludedDirCatalog.cs + SessionInitStep.cs:136 + ProjectStructureRule.cs:34 + FileFilter.cs:15 + HotFileDetector.cs:22 + MarkdownWalker.cs:16 | 新建 ExcludedDirectoryCatalog.cs 分层集合 |
| 7 | VCS 目录排除 | VcsDirectoryExclusions.cs:10(6 个) + SecurityPatterns.cs:53(3 个) | VcsDirectoryExclusions.cs |

### P2 - 低优先级(常量已定义但消费方未使用,4 项)

| # | 内容 | 常量定义 | 未委托位置 |
|---|------|----------|-----------|
| 8 | MCP 协议版本 | JsonRpcConstants.McpProtocolVersion | McpMockServerConfig.cs:14 + InitializeModels.cs:7 |
| 9 | 环境变量名(7 个) | JccEnvVar 枚举 | QueryEngine/GitHubApiClient/DecomposabilityAnalyzer 等 7 处硬编码 |
| 10 | 超时值(50+ 处) | WorkflowConstants.Timeouts | 50+ 处 TimeSpan.FromSeconds(30/60/120) |
| 11 | 供应商 API 端点 | JccEndpoints | GitHubApiClient/FallbackProviderDefinition 等 |

---

## 四、单一职责违反清单

### P0 - 高优先级(2 项)

| # | 类 | 行数 | 职责数 | 建议拆分 |
|---|-----|------|--------|----------|
| 1 | ChatContextManager | 1022 | 9 | 拆为 Core+SessionStore+FoldService+RewindService+ToolSpecService+CacheService |
| 2 | ReadOnlyCommandDetector(4 文件) | 2551 | 9 | 拆为 Detector+Catalog+Metacharacter+FlagValidator+RegexValidator+ExpansionDetector |

### P1 - 中优先级(3 项)

| # | 类 | 行数 | 建议拆分 |
|---|-----|------|----------|
| 3 | NotebookToolHandlers | 1136 | 拆为 Handlers+CellOperations |
| 4 | PathConstraintValidator | 1007 | 拆为 Validator+RedirectionValidator+ExpansionDetector+RemovalChecker |
| 5 | AgentToolHandlers | 1020 | 拆为 Create+Query+Messaging |

---

## 五、确定性/时序测试分家清单

### 现状

- 14000+ 测试方法,47 个项目,无 [Category] 分类,无 flaky 标记,无重试机制
- 4 种命名约定并存: pure_functions/ / pure_logic/ / mcp_pure/ / *PureFunctionsTest.cs
- 67 处 [Trait("Category","Unit/Integration/Benchmark")],无 Deterministic/Timing/Flaky

### 混杂文件(P0 立即修)

| # | 文件 | 问题 | 修复 |
|---|------|------|------|
| 1 | kit/mcp.tests/core/mcp_pure/ToolInterventionManagerTests.cs | pure 目录含 Task.Delay(10) | 2 个过期测试移到 mcp/ |
| 2 | test/unit/abs.tests/security/BashSafeWrapperStripperTests.cs | 注释声称确定性但含 6 个超时保护 | 拆出 TerminationTests |

### 混杂文件(P1)

| # | 文件 | 问题 |
|---|------|------|
| 3 | lib/async_lock.tests/actor/ActorBaseBugReproTest.cs | 3 确定性 + 3 时序混在一个类 |
| 4 | lib/async_lock.tests/lock/AsyncLockDiagnosisTests.cs | 诊断确定性 + 互斥时序混杂 |
| 5 | lib/clock.tests/goal/graph/GoalGraphEngineTests.cs(1852 行) | 大文件混杂 |

### flaky 测试(无标记)

| 文件 | 方法 | 注释自述 |
|------|------|---------|
| ActorBaseBugReproTest.cs | Bug3_InputCount_NeverNegative_UnderConcurrency | "概率性红(高并发采样)" |
| WaitGraphRaceTest.cs | (竞态测试) | 文件名含 Race |
| ActorBackpressureTest.cs | WatermarkReached_EventFiresOn* | 水位线事件时序 |

### 分家方案

**短期(低成本)**: 添加 [Trait("Category","Deterministic/Timing/Flaky")] 标记,CI 矩阵筛选
**中期(中成本)**: 统一 pure/ 目录命名,拆分混杂文件
**长期(高成本)**: 时序密集项目拆为 .tests.pure / .tests.timing / .tests.flaky

---

## 六、执行策略(并行子代理)

### 阶段 A: 守卫补全(P0 高风险 → P1 中风险 → P2 低风险)

按模块并行(不冲突):
- A1: guard 模块守卫补全(PermissionCheckContext/BashAstSecurityWalker/BashPermissionChecker/GitSecretScanner/CommandExecutionAuditor)
- A2: structura + async_lock 守卫补全(Dag null/ActorBackpressure 范围/MessageRetryEngine 范围)
- A3: transport.impl + vault 守卫补全(TcpPortHealthCheck/StdioProcessManager/MemoryStore/MemoryRelevanceScorer)
- A4: server + scheduling + plugins 守卫补全(ContentHash/DreamTaskPersistence/TimeoutLock/TaskService/PluginUnloadOptions/FlushGate)
- A5: abstractions 阈值范围补全(ReferenceResolutionOptions/ContextHierarchyOptions/AutoModeClassifier)

### 阶段 B: 单数据源统一(P0 → P1 → P2)

按数据源并行(不冲突):
- B1: MIME 映射统一 + 二进制检测统一(委托 MimeTypeExtensionMapper + McpBinaryHelper)
- B2: 删除命令统一 + Git 子命令统一(委托 DangerousCommandCatalog + 新建 GitCommandCatalog)
- B3: 排除目录统一 + VCS 目录统一(新建 ExcludedDirectoryCatalog + 委托 VcsDirectoryExclusions)
- B4: 常量委托统一(MCP 版本 + 环境变量 + 超时值 + 供应商端点)

### 阶段 C: 确定性/时序测试分家

- C1: 添加 [Trait("Category","Deterministic/Timing/Flaky")] 标记(全项目)
- C2: 统一 pure/ 目录命名(迁移 pure_functions/pure_logic/mcp_pure → pure/)
- C3: 拆分混杂文件(P0 2 个 + P1 3 个)
- C4: flaky 测试加标记 + 重试机制

### 阶段 D: 单一职责拆分(P0 → P1)

- D1: ChatContextManager 拆分(1022 行 → 6 个类)
- D2: ReadOnlyCommandDetector 拆分(2551 行 → 6 个类)
- D3: NotebookToolHandlers + PathConstraintValidator + AgentToolHandlers 拆分

### 并行规则

- 阶段 A 内部:A1-A5 可并行(不同模块)
- 阶段 B 内部:B1-B4 可并行(不同数据源)
- 阶段 C 内部:C1 先行(全项目标记),C2-C4 可并行
- 阶段 D 内部:D1-D3 可并行(不同类)
- 跨阶段:A→B→C→D 串行(避免冲突)
- 并行子代理禁止 git commit,由主代理统一提交

---

## 七、验收标准

每个任务完成后必须满足:
1. 编译通过:dotnet build 对应 csproj Debug 模式,0 警告 0 错误
2. 测试通过:新增测试全部绿
3. 确定性:新增测试不依赖时序/IO,给定输入→断言输出
4. 守卫补全:每个补全的守卫有对应测试验证抛出正确异常
5. 单数据源:统一后原硬编码位置改为委托,无残留重复
6. 测试分家:确定性测试标 [Trait("Category","Deterministic")],时序测试标 Timing
7. git 提交:每个子任务独立提交

---

<!-- 🤖 Auto Decision: 2026-09-30 -->
<!-- 决策: 创建 TASK031 整合 4 个 explore 子代理检测结果,分 4 阶段(A 守卫/B 单数据源/C 测试分家/D 单一职责)推进 -->
<!-- 原因: 用户 5 项要求覆盖 4 个方向,需统一规划避免冲突,按风险/收益排序 -->
<!-- 替代方案: 逐个方向独立推进(跨方向进度慢,可能冲突) -->
<!-- 验证: 4 个 explore 子代理检测完成,任务清单已整合 ✅ -->

---

## 八、阶段 B1 完成记录(2026-09-30)

### 架构约束发现与方案调整

**原任务文档假设**:保留 `kit/hands` 的 MimeTypeExtensionMapper 和 `kit/mcp` 的 McpBinaryHelper 作为单数据源,消费方委托。

**实际架构约束**(七层架构 ADR 0081):
- `lib/infrastructure`(③层)不引用 `kit/hands`/`kit/mcp`(④层)→ McpOutputStorage 无法访问 kit 的类
- `kit/hands` 与 `kit/mcp` 互不引用 → BinaryContentTypeDetector 无法访问 McpBinaryHelper

**调整方案**:单数据源下沉到 `lib/infrastructure`(③层),所有消费方委托。kit 的原类保留公共 API 作为 thin wrapper。

### 新建文件(单数据源 + 测试)

| 文件 | 角色 |
|------|------|
| `lib/infrastructure/io/services/file_ops/MimeExtensionCatalog.cs` | MIME→扩展名单数据源(22 映射,GetExtension+TryGetExtension) |
| `lib/infrastructure/io/services/file_ops/BinaryContentTypeCatalog.cs` | 二进制 Content-Type 检测单数据源(Span+OrdinalIgnoreCase) |
| `test/unit/infra.tests/file_ops/MimeExtensionCatalogTests.cs` | 单数据源测试(9 方法) |
| `test/unit/infra.tests/file_ops/BinaryContentTypeCatalogTests.cs` | 单数据源测试(7 方法) |

### 修改文件(消费方委托改造)

| 文件 | 改造 |
|------|------|
| `lib/infrastructure/.../McpOutputStorage.cs` | 删除本地 22 项数组,委托 MimeExtensionCatalog.TryGetExtension,保留子类型回退 |
| `kit/hands/.../MimeTypeExtensionMapper.cs` | 委托 MimeExtensionCatalog.GetExtension(thin wrapper) |
| `kit/hands/.../BinaryContentTypeDetector.cs` | 委托 BinaryContentTypeCatalog.IsBinaryContentType(thin wrapper) |
| `kit/mcp/.../McpBinaryHelper.cs` | IsBinaryContentType 委托 BinaryContentTypeCatalog(thin wrapper) |
| `kit/mcp/.../McpClientToolHandlers.cs` | GetExtensionFromMimeType 委托 MimeExtensionCatalog.TryGetExtension,保留 png 回退 |
| `kit/mcp/GlobalUsings.cs` | 添加 `global using Infrastructure.IO.Services.FileOps;` |
| `kit/mcp.tests/GlobalUsings.cs` | 添加 `global using Infrastructure.IO.Services.FileOps;` |
| `kit/hands.api.tests/.../BinaryContentTests.cs` | 追加 SingleSourceDelegationTests(2 方法,验证委托一致) |
| `kit/mcp.tests/.../McpBinaryHelperTests.cs` | 追加大小写不敏感 + 委托一致性测试(2 方法) |

### 统一的数据源 + 委托关系

```
MimeExtensionCatalog (lib/infrastructure, 单数据源)
├── McpOutputStorage.ExtensionForMimeType (委托 TryGetExtension + 子类型回退)
├── MimeTypeExtensionMapper.GetExtension (委托 GetExtension)
└── McpClientToolHandlers.GetExtensionFromMimeType (委托 TryGetExtension + png 回退)

BinaryContentTypeCatalog (lib/infrastructure, 单数据源)
├── BinaryContentTypeDetector.IsBinaryContentType (委托)
└── McpBinaryHelper.IsBinaryContentType (委托)
```

### 行为统一说明

- **大小写不敏感**:原 McpBinaryHelper 区分大小写(Span 无 StringComparison),原 BinaryContentTypeDetector 不敏感(ToLowerInvariant)。统一为大小写不敏感(更健壮,对齐 BinaryContentTypeDetector 期望)
- **MIME 映射**:统一 22 项(含 gzip),原 MimeTypeExtensionMapper 21 项(缺 gzip)已补全
- **未知回退**:各消费方保留原回退策略(McpOutputStorage 子类型、McpClientToolHandlers png、MimeTypeExtensionMapper bin)

### 编译结果

- `lib/infrastructure`:0 警告 0 错误
- `kit/hands`:0 警告 0 错误
- `kit/mcp`:0 警告 0 错误
- 3 个测试项目:0 警告 0 错误

### 测试结果

- `infra.tests`(MimeExtensionCatalog + BinaryContentTypeCatalog):60 通过
- `hands.api.tests`(BinaryContent + SingleSourceDelegation):60 通过
- `mcp.tests`(McpBinaryHelper):26 通过

### 新增测试方法数量:20 个

### 遇到的 bug

- 并行编译 `kit/hands` + `kit/mcp` 时 artifacts 目录竞争导致 CS0006/MSB3030,改为串行编译解决

---

## 九、阶段 B3 完成记录(2026-09-30)

### 架构约束发现与方案调整

**原任务文档假设**:6 处消费方全部委托 ExcludedDirectoryCatalog。

**实际架构约束**:
- `gen/aot_safety.generator`(Roslyn 分析器)因独立性约束不引用 abstractions → `ProjectStructureRule.cs` 无法直接委托 `ExcludedDirectoryCatalog.SearchExcluded`

**调整方案**:
- 5 处可引用 abstractions 的消费方 → 委托 ExcludedDirectoryCatalog
- `ProjectStructureRule.cs`(分析器)→ 保持本地集合,加注释说明"与 ExcludedDirectoryCatalog.SearchExcluded 保持同步"(7 个目录已一致)

### 新建文件(单数据源 + 测试)

| 文件 | 角色 |
|------|------|
| `lib/abstractions/abs_core/core_utils/constants/directory/ExcludedDirectoryCatalog.cs` | 排除目录单数据源(分层集合:核心4/搜索7/热文件17/Markdown13/审计9 + 数组形式 + IsInExcludedDirectory) |
| `test/unit/abs.tests/constants/ExcludedDirectoryCatalogTests.cs` | 单数据源测试(22 方法,FluentAssertions 风格) |
| `test/unit/abs.tests/constants/VcsDirectoryExclusionsTests.cs` | VCS glob 模式 + SecurityPatterns 委托测试(12 方法) |
| `non_deliverables_tools/jcc_audit_ast_cli/GlobalUsings.cs` | jcc_audit_ast_cli 项目 GlobalUsings(新增 Constants using) |

### 修改文件(消费方委托改造)

| 文件 | 改造 |
|------|------|
| `server/code_index/core/CodeIndexExcludedDirCatalog.cs` | 删除本地 HashSet+数组,委托 ExcludedDirectoryCatalog.CodeIndexExcluded/CodeIndexExcludedArray/IsInExcludedDirectory |
| `app/cli/entry/startup/core/SessionInitStep.cs` | 删除本地 IsInExcludedDirectory 实现(13 行),委托 ExcludedDirectoryCatalog.IsInExcludedDirectory |
| `non_deliverables_tools/jcc_audit_ast_cli/core/FileFilter.cs` | 删除本地 8 项数组,委托 ExcludedDirectoryCatalog.AuditExcludedArray |
| `lib/infrastructure/hot_spot/HotFileDetector.cs` | 删除本地 FrozenSet.Create(16 项),委托 ExcludedDirectoryCatalog.HotFileExcluded(17 项,+.x) |
| `lib/infrastructure/utils/text/MarkdownWalker.cs` | 删除本地 12 项数组,委托 ExcludedDirectoryCatalog.MarkdownWalkExcludedArray(13 项,+.x) |
| `gen/aot_safety.generator/.../ProjectStructureRule.cs` | 保持本地集合(分析器约束),加注释说明与 ExcludedDirectoryCatalog.SearchExcluded 保持同步 |
| `lib/abstractions/abs_hands/code/VcsDirectoryExclusions.cs` | 新增 GlobPatterns 派生属性(12 模式:6 目录 + 6 glob) |
| `lib/abstractions/abs_guard/security/scanning/SecurityPatterns.cs` | VcsInternal 从硬编码 3 个改为委托 VcsDirectoryExclusions.GlobPatterns(6 个,补全 .bzr/.jj/.sl) |
| `server/code_index/GlobalUsings.cs` | 添加 `global using JoinCode.Abstractions.Constants;` |
| `lib/infrastructure/GlobalUsings.cs` | 添加 `global using JoinCode.Abstractions.Constants;` |
| `test/unit/abs.tests/GlobalUsings.cs` | 添加 Constants + Security.Scanning using |
| `server/code_index.tests/d_to_i/GlobalUsings.cs` | 添加 `global using JoinCode.Abstractions.Constants;` |
| `server/code_index.tests/a_to_c/CodeIndexExcludedDirCatalogTests.cs` | 追加委托一致性验证(3 方法:Assert.Same 引用 + Theory 行为一致) |

### 统一的数据源 + 委托关系

```
ExcludedDirectoryCatalog (lib/abstractions, 单数据源)
├── CodeIndexExcluded (核心 4)
│   ├── CodeIndexExcludedDirCatalog.ExcludedDirs/DefaultExcludedDirs/IsInExcludedDirectory (委托)
│   └── SessionInitStep.IsInExcludedDirectory (委托)
├── SearchExcluded (搜索 7)
│   └── ProjectStructureRule.ExcludedDirectories (本地副本,分析器约束,注释同步)
├── HotFileExcluded (热文件 17)
│   └── HotFileDetector.ExcludedDirectories (委托)
├── MarkdownWalkExcluded (Markdown 13)
│   └── MarkdownWalkerOptions.ExcludeDirs (委托数组形式)
└── AuditExcluded (审计 9)
    └── FileFilter.s_commonExcludedDirs (委托数组形式)

VcsDirectoryExclusions (lib/abstractions, 单数据源)
├── Names (6 VCS 目录) — RgEngine/SearchService 已委托
└── GlobPatterns (12 模式,新增派生属性)
    └── SecurityPatterns.VcsInternal (委托,补全 .bzr/.jj/.sl)
```

### 行为统一说明

- **.x 归档目录补全**:原 HotFileDetector(16)/MarkdownWalker(12)/FileFilter(8) 均不含 .x,委托后统一补全为 17/13/9(.x 是归档目录,所有扫描都应排除)
- **VCS 目录补全**:原 SecurityPatterns.VcsInternal 仅 3 个(.git/.svn/.hg),委托后补全为 6 个(.git/.svn/.hg/.bzr/.jj/.sl),与 VcsDirectoryExclusions.Names 一致
- **大小写不敏感**:所有集合保持 OrdinalIgnoreCase(FrozenSet 比较器)
- **数组形式**:为不依赖 FrozenSet 的消费方(jcc_audit_ast_cli)提供 *Array 属性

### 编译结果

- `lib/abstractions`:0 警告 0 错误
- `lib/infrastructure`:0 警告 0 错误
- `server/code_index`:0 警告 0 错误
- `app/cli`:0 警告 0 错误
- `non_deliverables_tools/jcc_audit_ast_cli`:0 警告 0 错误
- `gen/aot_safety.generator`:0 警告 0 错误
- `test/unit/abs.tests`:0 警告 0 错误
- `server/code_index.tests`:0 警告 0 错误

### 测试结果

- `abs.tests`(ExcludedDirectoryCatalog + VcsDirectoryExclusions):34 通过
- `code_index.tests`(CodeIndexExcludedDirCatalog 委托验证):21 通过(含新增 3 委托验证)

### 新增测试方法数量:37 个

- ExcludedDirectoryCatalogTests:22 个(集合内容 10 + 数组一致 5 + 分层关系 3 + IsInExcludedDirectory 4)
- VcsDirectoryExclusionsTests:12 个(Names 1 + GlobPatterns 3 + SecurityPatterns 委托 4 + IsVcsPath 2 + 其他 2)
- CodeIndexExcludedDirCatalogTests 追加:3 个(ExcludedDirs 委托 + DefaultExcludedDirs 委托 + IsInExcludedDirectory 委托 Theory)

### 遇到的 bug

- xUnit Assert.Contains 对 FrozenSet<string> 有二义性(FrozenSet 同时实现 ISet<T> 和 IReadOnlySet<T>)→ 改用 FluentAssertions `.Should().Contain()` 解决
- xUnit2017 分析器拦截 `Assert.True(collection.Contains(item))` → FluentAssertions 同样解决

<!-- 🤖 Auto Decision: 2026-09-30 -->
<!-- 决策: ExcludedDirectoryCatalog 放在 abstractions(底层),CodeIndexExcludedDirCatalog 改为委托它(上层委托下层) -->
<!-- 原因: 避免循环依赖,abstractions 是底层不引用 server/code_index -->
<!-- 替代方案: ExcludedDirectoryCatalog 委托 CodeIndexExcludedDirCatalog(方向反了,abstractions 不能引用 server) -->
<!-- 验证: 8 个项目编译通过,55 个测试全部通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-09-30 -->
<!-- 决策: ProjectStructureRule 保持本地集合,加注释说明与 ExcludedDirectoryCatalog.SearchExcluded 同步 -->
<!-- 原因: Roslyn 分析器独立性约束,gen/aot_safety.generator 不引用 abstractions -->
<!-- 替代方案: 在 aot_safety.shared 中新建副本(违反单数据源原则) -->
<!-- 验证: 集合内容已一致(7 个),注释明确约束 ✅ -->

<!-- 🤖 Auto Decision: 2026-09-30 -->
<!-- 决策: VcsDirectoryExclusions.GlobPatterns 用手动构建(非 LINQ SelectMany) -->
<!-- 原因: 确保 abstractions 不依赖 System.Linq(虽然 ImplicitUsings 包含,但显式构建更安全) -->
<!-- 替代方案: Names.SelectMany(d => new[] { d, d + "/**" }).ToFrozenSet() -->
<!-- 验证: 编译通过,GlobPatterns 12 个模式正确 ✅ -->

