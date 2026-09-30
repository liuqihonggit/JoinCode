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
