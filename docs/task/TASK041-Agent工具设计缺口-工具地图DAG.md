# TASK041 — Agent 工具设计缺口：工具地图 DAG

> 来源：《Agent 工具设计》文档"工具地图"整节
> 检索日期：2026-10-09

## 背景

设计要求：工具映射 DAG + 频率动态推荐；首次使用靠锚点入口 + 向量检索注入上下文；蜘蛛网用情景模式化解；遥测让热工具变冷冷工具变热；用户习惯驱动转移频率 + primary/fallback/refine 三角色。

## 已落地部分

| 设计点 | 落地位置 |
|--------|----------|
| 工具链超图（替代 DAG） | `abs_hands/tools/models/ToolHypergraphModels.cs` |
| 超边预设（5 条：file_ops/shell_exec/search_chain/git_chain/code_chain） | `ToolHypergraphPresets.cs` |
| 超图评分器 | `ToolHypergraphScorer.cs` |
| 通用 DAG 结构（用于任务调度，非工具映射） | `lib/structura/dag/` |
| 三元组（用于代码知识图谱，非工具转移） | `abs_perception/code_index/graph/GraphTriple.cs` |
| 向量检索（用于代码语义搜索） | `server/code_index/embedding/EmbeddingIndex.cs` |
| 混合查询路由（向量→符号→兜底） | `server/code_index/query/HybridQueryRouter.cs` |
| 余弦相似度 | `server/code_index/vector/VectorMath.cs:15` |
| GraphRAG 边扩展（向量召回+三元组注入） | `CodeIndexToolHandlers.cs:1171` ExpandByGraphAsync |
| 桌面情景模式（look/zoom/detect/click + suggested_next） | `DesktopScene{Look,Zoom,Detect,Click}ToolHandlers.cs` |
| 情景状态持久化 | `DesktopSceneStateStore.cs` |
| 遥测服务（Counter/Histogram/Span） | `lib/infrastructure/telemetry/TelemetryService.cs` |
| 工具遥测记录模式 | `ToolTelemetryHelper.cs` |
| 工具健康监控 Actor（Success/Fail/ConsecutiveFailures/Score） | `ToolHealthMonitor.cs` |
| 时间衰减（每小时 DecayTick） | `ToolHealthMonitor.cs:317` ApplyTimeDecay |
| 评分中间件 + 连续失败注入提示 | `ToolHealthScoringMiddleware.cs`（Order=850） |
| 调试工具（tool_score/tool_hypergraph/tool_score_reset） | `ToolScoreDebugToolHandlers.cs` |
| primary/fallback（Agent 级，ExecuteWithFallbackAsync） | `AgentCoordinator.cs:428` |
| 链路推荐（基于静态 ChainOrder） | `ToolHypergraphScorer.cs:106` GetChainRecommendations |
| 失败时注入推荐替代链路 | `OnErrorToolInjectionMiddleware.cs:84` |

## 缺口清单

### GAP-041-01 工具级转移频率记录 + refine 角色 ✅

- **当前状态**：已落地（NextToolFrequency 字段 + RecordTransitionAsync + ToolTransitionModel 三角色 + ToolRefineRecommender + GetChainRecommendations 频率优先）
- **实现位置**：
  - `lib/abstractions/abs_hands/tools/models/ToolHealthModels.cs`（NextToolFrequency 字段 + RecordTransitionAsync 接口）
  - `lib/abstractions/abs_hands/tools/models/ToolTransitionModel.cs`（ToolTransitionRole 枚举 + ToolTransitionCondition + ToolTransitionModel + ToolRefineRule + ToolRefineRecommender）
  - `kit/mcp_tool_dispatch/core/execution/ToolHealthMonitor.cs`（RecordTransitionCmd Actor 消息 + RecordTransitionAsync 实现）
  - `kit/mcp_tool_dispatch/core/middleware/ToolHealthScoringMiddleware.cs`（执行后记录 lastTool→currentTool 转移）
  - `kit/mcp_tool_dispatch/core/execution/ToolHypergraphScorer.cs`（GetChainRecommendations 频率优先回退静态）
  - `kit/mcp_tool_dispatch/core/handlers/ToolScoreDebugToolHandlers.cs`（tool_score 输出转移频率 + boost 状态）
- **测试**：`test/unit/mcp_tool_dispatch.tests/execution/ToolTransitionTest.cs`（14 个测试全通过）
- **验收标准**：调用 tool_score 能看到 NextToolFrequency ✅；频率足够时链路推荐来自学习数据 ✅；refine 角色在数据过大场景被触发 ✅

### GAP-041-02 工具锚点入口 + 向量检索用于工具选择 ✅

- **当前状态**：已落地（`[ToolAnchors]` 特性 + `ToolAnchorIndex` 词袋向量 + 余弦相似度匹配 + gh_run_view/gh_run_list/gh_pr_checks 已标注锚点）
- **实现位置**：
  - `lib/abstractions/abs_core/core_attributes/mcp/ToolAnchorsAttribute.cs`（`[ToolAnchors]` 特性定义）
  - `lib/abstractions/abs_hands/tools/models/ToolAnchorIndex.cs`（ToolAnchorEntry + ToolAnchorMatch + ToolAnchorIndex 词袋模型+余弦相似度）
  - `kit/mcp/git_hub/GitHubToolHandlers.Run.cs`（gh_run_view/gh_run_list 标注 `[ToolAnchors]`）
  - `kit/mcp/git_hub/GitHubToolHandlers.Pr.cs`（gh_pr_checks 标注 `[ToolAnchors]`）
- **测试**：`test/unit/mcp_tool_dispatch.tests/execution/ToolAnchorIndexTest.cs`（14 个测试全通过）
- **验收标准**：用户问"CI 为什么失败"时匹配 gh_run_view ✅；锚点匹配用余弦相似度阈值可配 ✅；锚点用特性标注禁止手写表 ✅

### GAP-041-03 频率驱动动态重构图 ✅

- **当前状态**：已落地（`RebuildFromTransitionsAsync` 从转移频率动态加超边 + 低频候选标记 + `RemoveLowFreqCandidates` 剔除 + 定时器每 2 小时重建）
- **实现位置**：`kit/mcp_tool_dispatch/core/execution/ToolHypergraphScorer.cs`（RebuildFromTransitionsAsync + RemoveLowFreqCandidates + LowFreqEdgeCandidates + _rebuildTimer）
- **测试**：`test/unit/mcp_tool_dispatch.tests/execution/ToolHypergraphRebuildTest.cs`（8 个测试全通过）
- **验收标准**：高频工具链路自动出现在 hypergraph ✅；低频超边被剔除 ✅

### GAP-041-04 通用 DAG 蜘蛛网化解框架 ✅

- **当前状态**：已落地（`ToolScenarioSubgraphResolver` 从全图+情景模式解析子图，过滤蜘蛛网边，GAP-040-01 ScenarioRegistry 已存在）
- **实现位置**：`kit/mcp_tool_dispatch/core/execution/ToolScenarioSubgraphResolver.cs`（ToolScenarioSubgraph + ToolScenarioSubgraphResolver）
- **依赖**：GAP-040-01 ScenarioRegistry 已落地（`lib/abstractions/abs_core/core_attributes/scenario/` + `gen/scenario.generator/`）
- **测试**：`test/unit/mcp_tool_dispatch.tests/execution/ToolScenarioSubgraphResolverTest.cs`（10 个测试全通过）
- **验收标准**：进入情景模式后工具推荐只来自子图 ✅；蜘蛛网边在情景外不可见 ✅

### GAP-041-05 主动加热冷工具 ✅

- **当前状态**：已落地（`BoostToolAsync` + `HeatColdToolsAsync` + boost TTL + `GetEffectiveScore` 含 boost + `ApplyTimeDecay` 清理过期 boost）
- **实现位置**：`lib/abstractions/abs_hands/tools/models/ToolHealthModels.cs`（BoostScore/BoostExpiry/IsBoostActive 字段）+ `kit/mcp_tool_dispatch/core/execution/ToolHealthMonitor.cs`（BoostToolAsync/HeatColdToolsAsync 方法 + BoostToolCmd/HeatColdToolsCmd Actor 消息）
- **测试**：`test/unit/mcp_tool_dispatch.tests/execution/ToolHealthMonitorBoostTest.cs`（12 个测试全通过）
- **验收标准**：冷工具在匹配情景时被主动加热 ✅；TTL 后回落 ✅

### GAP-041-06 "状态+条件+目的"自然语言状态描述 ✅
- **当前状态**：已落地（`ToolHealthRecord.GenerateStatusDescription` + `tool_score` 输出集成）
- **实现位置**：`lib/abstractions/abs_hands/tools/models/ToolHealthModels.cs:GenerateStatusDescription` + `kit/mcp_tool_dispatch/core/handlers/ToolScoreDebugToolHandlers.cs:GetToolScoreAsync`
- **测试**：`test/unit/mcp_tool_dispatch.tests/execution/ToolHealthRecordStatusDescriptionTest.cs`（16 个测试全通过）
- **验收标准**：tool_score 输出含自然语言状态描述 ✅

## 优先级汇总

| 优先级 | 缺口 | 复杂度 |
|--------|------|--------|
| P1 | GAP-041-01 转移频率 + refine 角色 | 高 |
| P1 | GAP-041-02 工具锚点 + 向量检索 | 高 |
| P2 | GAP-041-03 频率驱动动态重构图 | 高 |
| P2 | GAP-041-04 通用 DAG 蜘蛛网化解 | 高 |
| P2 | GAP-041-05 主动加热冷工具 | 中 |
| P2 | GAP-041-06 状态描述 | 低 |

## 关联

- 设计文档：《Agent 工具设计》"工具地图"整节
- 关联 ADR：[0019](../adr/0019-enum-enumvalue-source-generator.md)、[0086](../adr/0086-core-tech-selection-lock-design.md)
- 关联记忆：`能计算就计算不要构造表`、`底层可复用能力优先于工具层实现`
- 依赖：GAP-041-04 依赖 GAP-040-01（通用 scenario registry）
