# 前缀缓存 × 上下文压缩：矛盾现状与改造计划

> 创建时间: 2026-08-06
> 范围: 只记录现场 + 给出改造蓝图，**未改任何代码**（除本会话已提交的 conversation 序列 hash，见 §0.3）

## 0. 背景

前缀缓存（Prefix Cache）与上下文压缩（Context Fold / Compact）存在**结构性矛盾**：

- 前缀缓存想要**只增不删**的稳定上行前缀 → 每轮近似全量 `cache_read` 命中，省 token。
- 上下文压缩要**替换头部旧消息**成摘要 → 前缀字节一变，下一轮**全量 miss**（约 1.25× 单价重建）。

### 0.1 现有网络层权威判定（已正确，无需改）
- `tests/MockServers/MockServer.Core/KestrelMockServer.cs:113-114` — 服务器端把真实 HTTP body `Parse` 后调 `ComputeCacheStats`。
- `TokenEstimator.ExtractConversationPrefix` + `PrefixCacheSimulator` 按**完整对话前缀**（含多轮增长）双向 `StartsWith` 计算命中。多轮正确。

### 0.2 客户端检测器（本会话已提交 69f74256f）
- `CacheBreakDetector.RecordPromptState/CheckCacheBreak` 新增可选 `conversation`：只比对快照时前 N 条消息的联合 hash，尾部追加/缩短不误报，篡改/插入报 `ConversationHistoryChanged`。
- 局限：只覆盖 `role+content` 文本，`Metadata(ToolCalls等)`/`ContentBlocks`(多模态) 未纳入；每次全量 O(n) 现算。

## 1. 现场链路（code 级）

```
PreChatMiddleware.RecordPromptStateAsync        // core/execution/Brain/src/Context/Services/Chat/PreChatMiddleware.cs:49
   ↓ 发请求
响应 usage  → ChatUsageProcessor.ProcessUsageAsync  // .../Services/Processor/ChatUsageProcessor.cs:36
   ├─ CheckCacheBreakAsync(promptSnapshot, usage)   // :40  判前缀失效
   ├─ RecordTurn(usage, cost, cacheBreak)           // :46  统计+成本
   └─ DecideAfterUsage(usage) → FoldIfNeededAsync   // :48-57  仅按 ratio 决策折叠
```

- `ContextFoldDecider.DecideAfterUsage`（`core/execution/Brain/src/ContextFold/ContextFoldDecider.cs:5`）只看 `usage.PromptTokens / ctxMax`：
  - `> 0.5 → FoldNormal`，`> 0.7 → FoldAggressive`，`> 0.8 → ForceSummary`。
  - **完全不看** `usage.CacheReadInputTokens` 或 `cacheBreakResult`。
- 折叠发生在 `CheckCacheBreak` **之后**，因此折叠本身不影响当轮判定。

## 2. 缺陷清单（现状问题）

| # | 严重度 | 缺陷 | 影响 | 证据 |
|---|--------|------|------|------|
| D1 | 高 | 折叠只看 token 占比，忽略缓存经济性 | 前缀健康命中时过早折叠，形成"缓存涨→全量 miss→重建→再涨→再折叠"锯齿，白扔增量性价比 | `ContextFoldDecider.cs:20-30` 无 cache 输入 |
| D2 | 中 | 压缩导致的全量 miss 被 `CacheEviction` 启发式**误报** | 观测/成本告警语义错乱，无法区分"摇塞"与"压缩前主动替换" | `CacheBreakDetector.ShouldReportCacheEviction`（identical 且 read=0） |
| D3 | 中 | README 与代码**矛盾** | 文档说"soft 50% 保护前缀"但与 `ContextFoldDecider` 0.5 即折叠相反；软/硬区间不存在 | `README.md:385` vs `ContextFoldThresholds.cs:5` |
| D4 | 低 | 前缀破坏的**种类**未细分（历史篡改 vs 压缩 vs 换模型） | 下游无法区分原因做不同处置 | `CacheBreakKind` 枚举仅 8 值 |
| D5 | 低 | 折叠后无"重建新前缀"策略 | 折叠后首个请求必然全量 miss，无法回到增量 | `ContextFoldExecutor` 替换后不重取快照 |

## 3. 改造蓝图（分阶段，渐进式）

### Phase 1 — 缓存感知折叠调度（解决 D1，核心收益）
- `DecideAfterFold` 增加 cache 输入（`usage.CacheReadInputTokens`）：当命中且 `ratio > FoldThreshold` 时，把折叠**推迟**到硬区间，仅在 **`ratio > HardFold` 或 `CacheRead == 0`** 时真正执行。
- 新增"推迟计数"封顶，避免无限推迟顶爆窗口；达到硬阈值无条件折叠。
- 目标：把缓存命中区间拉长，压缩只落在"非命中/硬冗余"的时机。

**Phase 2（压缩后建立新前缀，解 D2/D5）**
- 折叠后立即 `RecordPromptSnapshot` 重置快照；压缩摘要标记 `isCompactSummary` 固定在头部作为新前缀。
- 以"折叠"检测：`ConversationCount 变短 + CacheRead==0` 上报专用 `CompactionEntered`，而非 `CacheEviction`。

### Phase 3（文档-代码对齐，解 D3）
- 把软/硬阈值语义落到 `ContextFoldThresholds` + README 统一；或删除"软保护"表述改为真实行为。

### Phase 4（可选 — 增量 hash，配合本会话已提交的 conversation hash）
- `AppendOnlyLog` 维护滚动哈希（追加即 O(1) 摊轮），使 `Record/Check` 从每轮 O(n) 降到增量级。

## 4. 依赖与风险

| 项 | 说明 |
|----|------|
| Phase 1 之理出口 | 推迟折叠需封顶（`FoldLimit`），否则窗口膨胀 |
| 需接入方 | `IChatContextManager.DecideAfterFold` 契约（Abstractions）变更 → 全链路 regenome |
| 成本模型 | cache_read 0.1× / creation 1.25× 已在 `ComputeCostUsd` 计价，调度应参照 |
| 测试 | 每条用 TDD：🔴E2E(若接口变更) → 🔴单元 → 🟢单元 → 🟢刷新；`AutoCompactSoftThresholdTests` 可扩展 |

## 5. 决策记录

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 先出蓝图计划 md，不直接改代码 -->
<!-- 原因: 该问题是"缓存 vs 压缩"的量级权衡，涉及行为变更，需用户确认方向与阈值 -->
<!-- 替代方案: 直接重构（风险高，未获确认，弃用）-->
<!-- 验证: 计划文档产出，未编译改型，未提交 % -->