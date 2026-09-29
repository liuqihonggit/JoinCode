# TASK031 基建确定性测试补充(新批次) — 10模块检测报告+任务拆分

> **来源**:TASK030 交接文档方法论复用(`D:\Users\54076\Desktop\TASK030-交接文档-基建确定性测试方法论.md`)
> **检测时间**:2026-09-30
> **检测方式**:10个并行explore子代理,覆盖未做确定性测试补充的基建模块
> **状态**:阶段1全部完成(1030测试+5bug修复,PR #340+#343已合并),阶段2待执行

---

## 一、检测覆盖范围(10模块)

| # | 模块 | 路径 | 源文件数 | 过长方法数 | P0缺测方法数 | 检测task_id |
|---|------|------|----------|-----------|-------------|-------------|
| 1 | kit/brain | kit/brain/ | 175 | 20 | 18+ | ses_f12278f5effeMryaHGg7XkmJgh |
| 2 | kit/composition | kit/composition/ | 21 | 4 | 17+ | ses_f12277844ffei1uRYmdyWj2dDY |
| 3 | kit/hands | kit/hands/ | 275 | 56 | 15+ | ses_f12275f0affe3DzWuiZS3t6UmG |
| 4 | kit/mcp | kit/mcp/ | 146 | 63 | 40+ | ses_f12274768ffeGVzT1fP5TyMRK1 |
| 5 | kit/mcp_tool_dispatch | kit/mcp_tool_dispatch/ | 38 | 20 | 13+ | ses_f12272f96ffeFV5wyztOhsSWWm |
| 6 | kit/pipelines | kit/pipelines/ | 6 | 2 | 5+ | ses_f12271902ffe3ncB5mprg51tIa |
| 7 | llm/core | llm/core/ | 36 | 20 | 25+ | ses_f1226fe71ffeu3ztSfkFm2iJSl |
| 8 | llm/reasoning | llm/reasoning/ | 39 | 20 | 12+ | ses_f1226e478ffeem2LRhmQ6C1Kc3 |
| 9 | server/code_index | server/code_index/ | 31 | 20 | 22+ | ses_f1226c899ffetDTrRha3bQ6HoM |
| 10 | lib/bcl_bridge | lib/bcl_bridge/ | 2 | 0 | 1 | ses_f1226abdaffexPRNlJ1msZe2Ol |
| **合计** | — | — | **769** | **225** | **168+** | — |

---

## 二、各模块P0缺测关键方法(按风险/收益排序)

### 🔴 P0-A 核心算法零测试(最高风险)

| 模块 | 类.方法 | 文件:行号 | 缺测原因 | 风险 |
|------|---------|-----------|---------|------|
| kit/hands | `SedEditParser.ParseSubstitutionExpression`/`ConvertBreToEre`/`ConvertSedReplacement`/`IsValidSedFlags`/`BuildRegexFlags` | shell/services/SedEditParser.cs:80,202,265,160,310 | sed命令解析核心,5个private纯计算,完全无直接测试 | 命令解析错误致sed替换错误 |
| kit/hands | `SedValidator.CheckSedConstraints`/`IsSubstitutionCommand`/`ContainsDangerousOperations` | shell/services/SedValidator.cs | sed验证核心,完全无测试 | 危险sed操作漏检 |
| kit/hands | `SubprocessEnvCleaner.ScrubProcessEnvironment`/`BuildSensitiveEnvVars`/`IsSensitiveKey` | shell/services/SubprocessEnvCleaner.cs | 环境变量清洗,完全无测试 | 敏感环境变量泄露给子进程 |
| kit/hands | `ToolArgumentParser.Parse`/`SplitArguments`/`ParseValue` | tool_handlers/core/services/ToolArgumentParser.cs | 参数解析,完全无测试 | 参数解析错误 |
| kit/mcp | `McpToolCollapseClassifier.Classify` | auth/core/McpToolCollapseClassifier.cs:24 | 工具折叠分类,纯计算,0%覆盖 | 工具分类错误 |
| kit/mcp | `McpUnicodeSanitizer.PartiallySanitize`/`SanitizeRound` | auth/core/McpUnicodeSanitizer.cs:18,69 | Unicode净化,10轮迭代,0%覆盖 | Unicode攻击漏防 |
| kit/mcp | `McpToolBridge.BuildParameters`/`MapSchemaTypeToClrType` | bridge/McpToolBridge.cs:56,77 | 工具桥接,6路switch,0%覆盖 | 工具schema错误 |
| kit/mcp | `McpMessageExtensions.FromJson` | auth/core/McpMessageExtensions.cs:11 | 协议消息分派,4路,0%覆盖 | 协议消息解析错误 |
| kit/mcp | `McpJsonSerializer.SerializeObjectInternal` | mcp_protocol/McpJsonSerializer.cs:162 | 13类型分派,0%覆盖 | 序列化错误 |
| kit/mcp | `McpAuthProviderFactory.Create`+`ApiKey`/`Bearer`/`Basic` | client/auth/McpAuthProviders.cs:369 | 4路认证,0%覆盖 | 认证头构造错误 |
| kit/brain | `ContextFoldDecider`全部8方法 | context_fold/ContextFoldDecider.cs:16,57,83,114,162,200,210,236 | 上下文折叠决策核心纯函数,8方法零直接测试 | 折叠误触发/漏触发 |
| kit/brain | `ContextFoldExecutor.FoldAsync` | context_fold/ContextFoldExecutor.cs:32 | 折叠执行,86行,无测试 | 折叠执行错误 |
| kit/brain | `AnalyticsService`全部方法 | cost_tracking/services/core/AnalyticsService.cs | 整个类无测试 | 成本分析错误 |
| kit/brain | `BudgetGuard`全部方法 | cost_tracking/services/core/BudgetGuard.cs | 整个类无测试 | 预算告警错误 |
| llm/core | `JevQueryService.ConvertToDecision`/`BuildStateFromMessageList` | Adapters/LLM/QueryServices/Jev/JevQueryService.cs:132,80 | Jev决策服务核心,0%覆盖 | 决策类型错误 |
| llm/core | `ResponsesQueryService.EscapeJsonString` | Adapters/LLM/QueryServices/Responses/ResponsesQueryService.cs:438 | JSON转义,5种字符,0%覆盖 | JSON破坏 |
| llm/core | `StreamingFallbackDecorator.ShouldFallback` | Adapters/LLM/Fallback/StreamingFallbackDecorator.cs:169 | 5异常类型分类,0%覆盖 | fallback误触发/漏触发 |
| llm/core | `AnthropicQueryService.CreateAnthropicRequest` deferred tools过滤 | Adapters/LLM/QueryServices/Anthropic/AnthropicQueryService.cs:101-127 | 复杂双集合匹配,0%覆盖 | 工具丢失 |
| server/code_index | `IndexSnapshot.RemoveFileData`/`InsertSymbols`/`CorrectInheritsToImplements` | indexing/IndexSnapshot.cs:240,321,406 | 索引中枢写操作,0%直接覆盖 | 索引损坏 |
| server/code_index | `GraphAnalytics.LabelPropagation`/`BuildCommunities` | analytics/GraphAnalytics.cs:308,351 | 社区检测算法,0%直接覆盖 | 社区检测错误 |
| kit/mcp_tool_dispatch | `ToolHealthMonitor.MatchesPattern`/`ApplyTimeDecay` | core/execution/ToolHealthMonitor.cs:130,300 | 通配符匹配+时间衰减,0%直接覆盖 | 健康监控错误 |
| kit/mcp_tool_dispatch | `PermissionAwareToolExecutor.ExecuteAsync`/`HandlePendingConfirmationAsync` | core/execution/PermissionAwareToolExecutor.cs:49,129 | 主执行路径,0%覆盖 | 工具执行错误 |
| llm/reasoning | `JudgeAgent.DecideWithWeightedSystem` | Agents/JudgeAgent.cs:101 | 4分支裁决,0%直接覆盖 | 裁决错误 |
| llm/reasoning | `ReasoningEngine.ApplyVerdicts` | Engine/ReasoningEngine.cs:438 | 4分支状态转换,0%直接覆盖 | 状态转换错误 |

### 🔴 P0-B 纯函数零测试(易测,高收益)

| 模块 | 类.方法 | 文件:行号 | 说明 |
|------|---------|-----------|------|
| kit/pipelines | `ChatErrorHandlingMiddleware.GetEndpointHint` | middlewares/ChatErrorHandlingMiddleware.cs:82 | 4分支纯函数,private,改internal即可测 |
| kit/pipelines | `ChatErrorHandlingMiddleware.ClassifyException`剩余5分支 | middlewares/ChatErrorHandlingMiddleware.cs:56 | 403/500+/Socket/Timeout/TaskCanceled |
| kit/mcp | `GitHubRunLogFilter`全部11方法 | git_hub/GitHubRunLogFilter.cs + GitHubRunLogText.cs + GitHubRunStepExtractor.cs + GitHubRunListSummarizer.cs | GitHub日志处理,纯计算,0%覆盖 |
| kit/mcp | `McpStdioClient.ParseEndpoint` | client/transport/McpStdioClient.cs:117 | 引号/空格2路拆分 |
| kit/mcp | `McpHttpServer.IsInitializeRequest`/`ValidateOrigin` | mcp_protocol/McpHttpServer.cs:213,206 | 3路判定 |
| kit/mcp | `McpReconnectPolicy.Decide` | remote/core/McpReconnectPolicy.cs:13 | 5路状态机(已有测试,确认) |
| kit/composition | `ExecutionSettingsProvider.EffortLevel`/`ThinkingEnabled` | dependency_injection/core/ExecutionSettingsProvider.cs:43,89 | 双变量模式状态转换 |
| kit/composition | `McpToolSyncBridge.SerializeToolSchema` | dependency_injection/infrastructure/McpToolSyncBridge.cs:99 | JSON转义边界 |
| llm/reasoning | `WeightedDecisionSystem.CalculateFinalConfidence` | Weight/Calculator/WeightedDecisionSystem.cs:99 | 4因子加权纯计算 |
| llm/reasoning | `BayesianEvidenceUpdater.UpdateGaussian` | Weight/Bayesian/BayesianEvidenceUpdater.cs:73 | 高斯共轭更新+clamp |
| llm/reasoning | `ChainWeightPropagator.CalculateChainScore` | Weight/Chain/ChainWeightPropagator.cs:18 | 双向传播 |
| server/code_index | `SymbolSearcher.ParseQueryTokens`/`MatchSingleToken` | indexing/SymbolSearcher.cs:190,209,218 | 分词+glob匹配 |
| server/code_index | `CsprojParser.ReplaceMsBuildVariables` | parsing/CsprojParser.cs:122 | MSBuild变量迭代替换 |
| server/code_index | `SolutionParser.ParseProjectLine`/`SplitQuotedParts` | parsing/SolutionParser.cs:91,126 | .sln行解析 |
| lib/bcl_bridge | `BclFileIO.DetectFromBOM` | lib/bcl_bridge/BclFileIO.cs:35 | 4分支BOM检测,private改internal |

### 🟡 P1 长方法拆分(中风险,高收益)

| 模块 | 长方法 | 行数 | 拆分目标 |
|------|--------|------|---------|
| kit/brain | `PlanModeManager.ExitPlanModeAsync` | 138 | 拆审批流程+权限恢复 |
| kit/brain | `QueryEngine.ExecuteCoreLoopAsync` | 133 | 拆重试+钩子链 |
| kit/brain | `CodeContentCompressor.CompressAsync` | 107 | 拆行分类+方法体跟踪 |
| kit/hands | `AgentStreamExecutionMiddleware.InvokeAsync` | 123 | 拆chunk switch分发 |
| kit/hands | `ShellSedInterceptMiddleware.HandleSedEditAsync` | 121 | 拆sed解析+替换 |
| kit/mcp | `McpServer.RunAsync` | 287 | 拆主循环+消息分发 |
| kit/mcp | `McpClientToolHandlers.McpConnectAsync` | 135 | 拆配置构造+工厂分派+响应构造 |
| kit/mcp | `GitHubRunLogFilterRunner.FilterFailedTestsAsync` | 117 | 拆4态状态机+去重+Rust输出 |
| llm/core | `ResponsesQueryService.GetStreamEventContentsAsync` | 230 | 拆7 SSE case+两阶段工具加载 |
| llm/core | `AnthropicQueryService.SendAnthropicStreamingRequestAsync` | 169 | 拆4 SSE case+server_tool_use追踪 |
| server/code_index | `CodeIndexer.SearchComprehensiveAsync` | 108 | 拆token预算截断+4类别收集 |
| server/code_index | `GraphAnalytics.FindPathAsync` | 78 | 拆双向BFS+路径重建 |
| server/code_index | `IndexSnapshot.RemoveFileData` | 80 | 拆三段独立移除 |
| kit/composition | `ServiceRegistration.AddBridgeServices` | 145 | 拆5个管道构建 |
| kit/composition | `ServiceRegistration.AddInfrastructureServices` | 93 | 拆7个环境切换分支 |

### 🟢 P2 mock测试(低风险,中收益)

- 各模块IO/异步依赖方法(需mock,非确定性测试范畴)
- 详见各模块检测报告

---

## 三、三阶段任务拆分

### 阶段1:P0纯逻辑零测试(只改private→internal,补测试)

**目标**:覆盖所有P0-A + P0-B方法,不改生产代码逻辑(仅private→internal)

**子任务(按模块并行,不同测试项目不冲突)**:

| 子任务 | 模块 | 测试项目 | 预计测试数 | 优先级 |
|--------|------|---------|-----------|--------|
| 1.1 | kit/hands (SedEditParser+SedValidator+SubprocessEnvCleaner+ToolArgumentParser) | kit/hands.shell.tests + kit/hands.tool_handlers.tests | 80+ | 🔴最高 |
| 1.2 | kit/mcp (协议构造+工具元数据+GitHub日志+认证Provider) | kit/mcp.tests | 100+ | 🔴最高 |
| 1.3 | kit/brain (ContextFoldDecider+ContextFoldExecutor+AnalyticsService+BudgetGuard) | kit/brain.context.tests + kit/brain.other.tests | 60+ | 🔴最高 |
| 1.4 | llm/core (JevQueryService+EscapeJsonString+ShouldFallback+deferred tools) | llm/llm.tests | 40+ | 🔴最高 |
| 1.5 | server/code_index (IndexSnapshot+LabelPropagation+BuildCommunities+分词) | server/code_index.tests | 50+ | 🔴最高 |
| 1.6 | kit/mcp_tool_dispatch (MatchesPattern+ApplyTimeDecay+PermissionAwareToolExecutor) | kit/mcp_tool_dispatch.tests + test/unit/mcp_tool_dispatch.tests | 30+ | 🔴高 |
| 1.7 | llm/reasoning (DecideWithWeightedSystem+ApplyVerdicts+CalculateFinalConfidence+UpdateGaussian) | llm/reasoning.tests | 30+ | 🔴高 |
| 1.8 | kit/pipelines (GetEndpointHint+ClassifyException剩余分支) | test/unit/host.tests(或新建kit/pipelines.tests) | 15+ | 🟡中 |
| 1.9 | kit/composition (ExecutionSettingsProvider+SerializeToolSchema+配置验证) | kit/composition.tests | 20+ | 🟡中 |
| 1.10 | lib/bcl_bridge (DetectFromBOM) | 新建lib/bcl_bridge.tests | 8+ | 🟢低(极小) |

**并行策略**:
- 可并行:1.1+1.2+1.3+1.4+1.5(不同测试项目)
- 可并行:1.6+1.7+1.8+1.9+1.10(不同测试项目)
- 同测试项目内串行(如1.1的SedEditParser和SedValidator)

### 阶段2:P1长方法拆分(拆出internal子方法+补测试)

**目标**:拆分15个关键长方法为internal子方法,补确定性测试,保持行为不变

**子任务**:

| 子任务 | 模块 | 拆分目标 | 预计拆分方法数 |
|--------|------|---------|---------------|
| 2.1 | kit/brain | PlanModeManager/QueryEngine/CodeContentCompressor | 6+ |
| 2.2 | kit/hands | AgentStreamExecutionMiddleware/ShellSedInterceptMiddleware | 4+ |
| 2.3 | kit/mcp | McpServer/McpClientToolHandlers/GitHubRunLogFilterRunner | 8+ |
| 2.4 | llm/core | ResponsesQueryService/AnthropicQueryService | 6+ |
| 2.5 | server/code_index | CodeIndexer/GraphAnalytics/IndexSnapshot | 8+ |
| 2.6 | kit/composition | ServiceRegistration.AddBridgeServices/AddInfrastructureServices | 13+ |

### 阶段3:P2 mock测试(可选,IO/异步方法)

**目标**:用mock消除IO/异步依赖,使测试确定性

**跳过原则**:若某方法即使mock仍涉及时序,跳过并注释说明

---

## 四、验收标准

### 阶段1验收
- [ ] 所有P0-A + P0-B方法有直接单元测试
- [ ] private→internal只改可见性不改逻辑(除bug修复)
- [ ] 每个测试项目编译通过(0警告0错误)
- [ ] 每个测试项目全量测试通过
- [ ] InternalsVisibleTo已确认
- [ ] GlobalUsings:.cs文件内无using
- [ ] git提交(主代理统一)

### 阶段2验收
- [ ] 15个长方法拆分为internal子方法
- [ ] 行为不变(全量回归通过)
- [ ] 每个internal子方法有确定性测试
- [ ] 编译+测试通过

### 阶段3验收
- [ ] 可mock方法有mock测试
- [ ] 不可mock方法有跳过注释说明原因

---

## 五、执行顺序(按风险/收益降序)

```
阶段1(并行5+5):
  批次A: 1.1(kit/hands) + 1.2(kit/mcp) + 1.3(kit/brain) + 1.4(llm/core) + 1.5(server/code_index)
  批次B: 1.6(kit/mcp_tool_dispatch) + 1.7(llm/reasoning) + 1.8(kit/pipelines) + 1.9(kit/composition) + 1.10(lib/bcl_bridge)
  → 每批次完成后PR(auto-merge squash)

阶段2(并行3+3):
  批次C: 2.1 + 2.2 + 2.3
  批次D: 2.4 + 2.5 + 2.6
  → 每批次完成后PR

阶段3(可选):
  按需mock测试
```

---

## 六、验证流程(每步必须)

```
①编译:dotnet build {csproj} -c Debug  → 0警告0错误
②测试:dotnet test {csproj} -c Debug --no-build → 全绿
③提交:git add + git commit(主代理统一)
④PR前:dotnet build build/sln/JoinCode.slnx -c Debug → 0警告0错误
⑤PR:gh pr create + gh pr merge {N} --auto --squash
```

---

## 七、注意事项(避坑,复用TASK030经验)

### 必须遵守
1. **传用户原始查询verbatim给developer-test-agent**:不重写、不扩展、不分解
2. **并行子代理禁止git commit**:由主代理统一提交
3. **private→internal只改可见性不改逻辑**:除非是bug修复
4. **bug立即修复**:发现bug停下先修复(AGENTS.md铁律)
5. **PR前编译JoinCode.slnx**:禁止带错提PR
6. **InternalsVisibleTo确认**:internal方法所在项目已对测试项目暴露
7. **GlobalUsings**:.cs文件内禁止写using
8. **禁止删除文件**:移到.xxx/目录

### 常见跳过原因
| 原因 | 示例 |
|------|------|
| 时序依赖 | QueryEngine.ExecuteCoreLoopAsync异步编排 |
| IO不可mock | EnvironmentProbeService.GetExecutorScoresAsync(Process/Env) |
| 高风险大重构 | McpServer.RunAsync主循环(287行) |
| 依赖不存在 | 某些DTO字段可能已变更 |

### 预期bug模式(基于TASK030经验)
| 模式 | 案例 |
|------|------|
| 集合操作结果丢弃 | 类似PermissionChecker.RemoveFromAutoApproved |
| 循环未跳出 | 类似BashSafeWrapperStripper |
| 边界参数 | 类似GetPipeTargetCommands(Enumerable.Range(0,-1)) |
| 实现与参考不同步 | sed解析与GNU sed行为差异 |

---

## 八、检测报告存档

各模块完整检测报告见10个explore子代理输出(task_id见第一节表格)。
本任务文档为整合摘要,执行时各子代理可参考对应模块的完整报告。

---

<!-- 🤖 Auto Decision: 2026-09-30 -->
<!-- 决策: 复用TASK030方法论,并行10个explore检测10个未覆盖模块,整合为TASK031 -->
<!-- 原因: 用户要求按交接文档寻找相同类型进行实现 -->
<!-- 替代方案: 串行检测(慢10倍,上下文浪费) -->
<!-- 验证: 10个explore全部完成,任务文档已生成 ✅ -->
