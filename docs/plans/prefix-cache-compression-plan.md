# 前缀缓存 × 上下文压缩：矛盾现状与改造计划

> 创建时间: 2026-08-06
> 范围: 只记录现场 + 给出改造蓝图，**未改任何代码**（除本会话已提交的 conversation 序列 hash，见 §0.2）

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
| D2 | 中 | 压缩导致的全量 miss 被 `CacheEviction` 启发式**误报** | 观测/成本告警语义错乱，无法区分"驱逐"与"压缩前主动替换" | `CacheBreakDetector.ShouldReportCacheEviction`（identical 且 read=0） |
| D3 | 中 | README 与代码**矛盾** | 文档说"soft 50% 保护前缀"但与 `ContextFoldDecider` 0.5 即折叠相反；软/硬区间不存在 | `README.md:385` vs `ContextFoldThresholds.cs:5` |
| D4 | 低 | 前缀破坏的**种类**未细分（历史篡改 vs 压缩 vs 换模型 vs TTL） | 下游无法区分原因做不同处置 | `CacheBreakKind` 枚举仅 8 值 |
| D5 | 低 | 折叠后无"重建新前缀"策略 | 折叠后首个请求必然全量 miss，无法回到增量 | `ContextFoldExecutor` 替换后不重取快照 |

## 3. 改造蓝图（分阶段，渐进式）

### Phase 1 — 缓存感知折叠调度（解决 D1，核心收益）
- `DecideAfterUsage` 增加 cache 输入（`usage.CacheReadInputTokens`）：当命中且 `ratio > FoldThreshold` 时，把折叠**推迟**到硬区间，仅在 **`ratio > HardFold` 或 `CacheRead == 0`** 时真正执行。
- 新增"推迟计数"封顶，避免无限推迟顶爆窗口；达到硬阈值无条件折叠。
- 目标：把缓存命中区间拉长，压缩只落在"非命中/硬冗余"的时机。

### Phase 2 — 压缩后重基线 + 种类细分（解决 D2/D5，上游已验证）
- 折叠/压缩后立即重置 cache-read 基线（等价上游 `notifyCompaction()`），使压缩 miss 不再被误报为 `CacheEviction`。
- `ConversationCount 变短 + CacheRead==0` 上报专用 `CompactionEntered`；配合 `timeSinceLastAssistantMsg` 区分 TTL/服务端/客户端变更。
- 压缩摘要标记 `isCompactSummary` 固定在头部作为新前缀（重建前缀锚点）。

### Phase 3 — 文档-代码对齐（解决 D3）
- 把软/硬阈值语义落到 `ContextFoldThresholds` + README 统一；或删除"软保护"表述改为真实行为。

### Phase 4 — 增量 hash（可选，配合已提交的 conversation hash）
- `AppendOnlyLog` 维护滚动哈希（追加即 O(1) 摊轮），使 `Record/Check` 从每轮 O(n) 降到增量级。

## 4. 依赖与风险

| 项 | 说明 |
|----|------|
| Phase 1 合理性出口 | 推迟折叠需封顶（`FoldLimit`），否则窗口膨胀 |
| 需接入方 | `IChatContextManager.DecideAfterUsage` 契约（Abstractions）变更 → 全链路 recompile |
| 成本模型 | cache_read 0.1× / creation 1.25× 已在 `ComputeCostUsd` 计价，调度应参照 |
| 测试 | 每条用 TDD：🔴E2E(若接口变更) → 🔴单元 → 🟢单元 → 🟢刷新；`AutoCompactSoftThresholdTests` 可扩展 |

## 5. 上游参考（Claude Code TS，claude-code-rev-main/src）

> 调查核实：上游同样存在"压缩 vs 前缀缓存"矛盾，且有成熟解法，可作 C# 实现对齐基准。

| 我的计划项 | 上游实现 | 文件 | 结论 |
|-----------|---------|------|------|
| Phase1 缓存感知折叠 | **无**（不看缓存），靠"晚折叠"化解 | `services/compact/autoCompact.ts:160-239` | 是本项目**创新点**，无上游背书；上游用"压到离窗 13k 才折"替代 |
| Phase2 折叠后重基线 | ✅ **`notifyCompaction()` 把 `prevCacheReadTokens=null`** | `services/api/promptCacheBreakDetection.ts:689` | **上游已工程验证，注释称漏缺致 20% 误报；优先必补** → 你项目 D2 的对应修复 |
| D4 种类细分 | ✅ 用 `timeSinceLastAssistantMsg` 区分 TTL5min/1h/超1h/服务端 | `promptCacheBreakDetection.ts:566-588` | 建议对齐：绝对降幅 ≥2000 + 相对降 >5% 才判 break，且跳过 haiku |
| 探测维度 | ✅ 额外 hash cache_control/模型/betas/effort/extraBody | `promptCacheBreakDetection.ts:274-294`，模型排除 `haiku` | 比本 C# 更细（含 scope/TTL 翻转、header/effort 变更） |
| microcompact 缓存编辑 | ✅ `notifyCacheDeletion()` | `promptCacheBreakDetection.ts:673` | C# 有对应 `MicrocompactService` 时须加同款重基线 |
| 压缩阈值 | 上游按 token 绝对值 `窗口-13000-20000(保留输出)` | `autoCompact.ts:28-49,62-91` | 你计划 C# 用比例 0.5/0.7/0.8 偏早，建议向"接近窗口"拉齐 |

**对齐建议（依上游）**：
1. **Phase2 必须做**：折叠/压缩后重置 cache-read 基线（相当于 `notifyCompaction`），消除压缩 miss 被判 `CacheEviction` 的误报——这是上游用真实 BQ 数据踩出来的必需项。
2. 判 break 用「相对 >5% 且绝对 ≥2k token 下降」替换现有"identical 且 read==0"过于粗糙的后置判定。
3. 归因细分：TTL(时间间隔) vs 服务端 vs 客户端变更，消除现有单一 `CacheEviction` 语义混淆。

## 6. 上游参考（DeepSeek-Reasonix Go 版，main-v2/v1.20）

> 调查核实：这个"压缩 vs 前缀缓存"矛盾在三家（Claude Code TS / Reasonix TS / **Reasonix Go**）中都存在；Go 版是**唯一把"缓存优先折叠"落地并带 E2E 验证的**，是本 C# 计划的黄金参照。

| 我的计划项 | Go 版实现 | 文件 | 结论 |
|-----------|----------|------|------|
| Phase1 缓存感知延迟折叠 | ✅ **软/硬分层阈值**：`soft=0.5` 只提示不改折叠、显式保留前缀 | `internal/agent/compact.go:87-112,125` | **Go 版已落地同款想法**，比 Claude Code 更接近本 Phase1；直接抄 |
| Phase1 推迟封顶 | ✅ **折叠卡死锁**：窗口太小时折叠赶不上增长→自动暂停、让前缀只增不改 | `compact.go:94,122-131,154-159` | 比"计数封顶"更强的兜底，命中率恢复 |
| Phase1 折叠前低成本剪裁 | ✅ **`snip=0.6` 先剪裁过期 tool_calls** 再摘要折叠 | `compact.go:113-121,133-143` | 剪裁比摘要省一轮 omitcall，可作为折叠前预步骤 |
| Phase2 折叠后重基线 | ✅ `session.RewriteVersion` → `CacheBreakKind` 属于 `log_rewrite` 而非 `CacheEviction` | `agent/session.go:20,92` + `cache_shape.go:26,75` | 与主张一致，已工程验证 |
| Phase2 归因细分 | ✅ `PrefixChangeReasons: system/tools/log_rewrite` + `CacheDiagnostics` 上报 | `cache_shape.go:66-93` | D4 的落地形态 |
| MCP 工具漂移保护 | ✅ `normalizeToolSchemas` 按名排序再 hash，化解工具重排漂移 | `cache_shape.go:51-64` | 比 TS 版 `drift.ts` 5 级更像本方案的 hash 判定；加进 D4 |
| 前缀稳定性测试 | ✅ `TestBuildComposesByteStableSystemPrompt` 保证系统提示词逐字节稳定 | `boot/prompt_stability_test.go` | 建议在 w2 补同款"两 Build 前缀逐字节一致"守卫 |
| 缓存 TTL（冷恢复） | ✅ 按 vendor 返回缓存 TTL（DeepSeek 24h / DashScope+Anthropic 5min） | `config/cache_policy.go:21-36` | 判断"服务端缓存是否已过期"，可并入 D4 归因 |

**对齐建议（依 Go 版）**：
1. **Phase1 采纳：`0.5` 只提示不折叠**，到 `0.8` 才真正折叠，加 `0.6` 剪裁、`0.9` 强制——Go 版已用 `TestCacheHitSurvivesTooSmallWindow`（`cachehit_e2e_test.go`) 实证命中率曲线不塌。
2. 补**折叠卡死锁**：窗口太小、折叠追不上增长时 pause，别让 `ContentFold` 只在比例上反复触发。
3. **缓存字节级/工具哈希归一化**加入 D4，配合已提交的 conversation 序列 hash。

## 7. 决策记录

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 先出蓝图计划 md，不直接改代码；并把上游 Claude Code 的参考结论并入计划 -->
<!-- 原因: "缓存 vs 压缩"是量级权衡，涉及行为变更，需用户确认方向与阈值；上游结论可作对齐基准 -->
<!-- 替代方案: 直接重构（风险高，未获确认，弃用）-->
<!-- 验证: 计划文档产出 + 上游调研核实，未改行为代码 -->

## 8. 决策记录（Reasonix 调研补充，2026-08-06）

<!-- 决策: 将 DeepSeek-Reasonix Go 版(上游 main-v2)分层折叠/折叠卡死锁/剪裁/log_rewrite 归因等结论并入本计划 §6 -->
<!-- 原因: Go 版已把本项目 Phase1+Phase2 的想方案<E2E 实测落地，是比 Claude Code TS 更完整、更贴近本项目的参照基准 -->
<!-- 替代方案: 仅 Claude 参考(缺"缓存优先折叠"背书)；仅 Reasonix TS 参考(旧、无 E2E)。Go 版兼有二者 -->
<!-- 验证: 在 reset 到 upstream 时于 reflog 找回"缓存设计.md"供归档比对；本会话未改行为代码 -->

## 9. 决策记录（Phase1 落地，2026-08-06）

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 落地 Phase1 缓存感知折叠 — DecideAfterUsage 增加 cache 输入，缓存命中且低于硬阈值时返回新枚举 Deferred 推迟折叠，DeferFoldLimit=3 封顶，达到硬阈值无条件 ExitWithSummary -->
<!-- 原因: 对齐 Reasonix Go 版"缓存优先折叠"（soft 只提示不折叠），拉长缓存命中区间，避免"折叠→全量miss→重建"锯齿 -->
<!-- 替代方案: 0.5 即折叠（现状，缓存健康时白扔增量性价比）；无封顶无限推迟（会顶爆窗口）-->
<!-- 验证: 新增 ContextFoldDecideAfterUsageTests 7 例全绿 + 既有 ContextFold 13 例/ ChatUsageProcessor 4 例不回归；Brain 编译 0 警告 0 错误 -->

## 10. 决策记录（Phase2 落地，2026-08-06）

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 落地 Phase2 压缩后重基线 — CacheBreakDetector 新增 NotifyCompaction()，折叠改写前缀后(manager FoldIfNeededAsync)调用重置缓存基线并标记待上报; 下次全量miss(CacheRead==0且Creation>0)归因新枚举 CacheBreakKind.CompactionEntered 而非 CacheEviction -->
<!-- 原因: 对齐 Reasonix Go 版 rewriteVersion→log_rewrite 与上游 Claude notifyCompaction; 消除"压缩导致的全量miss被误报驱逐"的语义错乱 -->
<!-- 替代方案: 直接把压缩miss当驱逐(现状，观测/成本告警误导); 仅重置基线不上报专用kind(失去可观测性) -->
<!-- 验证: 新增 CacheBreakDetectorCompactionTests 4 例 + SessionStats CompactionEnteredBreaks 全绿; 既有 25 例 CacheBreak/ContextFold + Brain.Context.Tests 723 例不回归; 编译 0 警告 0 错误 -->

## 11. 决策记录（Phase4 落地：折叠卡死守卫，2026-08-06）

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 新增折叠卡死守卫 — ContextFoldThresholds.StuckFoldLimit=2；ContextFoldDecider.IsFoldStuck()；ChatContextManager 追踪 _consecutiveNoProgressFolds，折叠动作执行但未产生任何缩减时累计，达到上限后 DecideAfterUsage 将 FoldNormal/FoldAggressive 降级为 None 暂停自动折叠；折叠成功即重置 -->
<!-- 原因: 对齐 Reasonix Go 版 compactStuck 防护；窗口过小时"折叠→无缩减→下轮再折叠"会每轮重试，造成循环损耗并干扰缓存前缀 -->
<!-- 替代方案: 不守卫(现状，窗口过小时折叠死循环); 用时间窗/绝对次数(无进展语义不精确) -->
<!-- 验证: 新增 ContextFoldStuckGuardTests 3 例(纯守卫) + CacheBreakMonitorTests 2 例(管理器级：暂停与重置)全绿；Brain.Context.Tests 725 例不回归；编译 0 警告 0 错误 -->

## 12. 决策记录（Phase5 落地：前缀字节稳定性守卫，2026-08-06）

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: ImmutablePrefixStableSortTests 新增 2 例字节级守卫 — 两 Build 相同输入 ToMessages() 经 ContentHash.ComputeConversation 逐字节一致，且顺序相反注册工具亦字节稳定 -->
<!-- 原因: 对齐 Reasonix Go 版 TestBuildComposesByteStableSystemPrompt；缓存前缀逐字节稳定是"缓存命中率不塌"的前提，防未来重构悄悄引入非确定性序列化 -->
<!-- 替代方案: 仅指纹(ContentHash)判定(不够，需覆盖序列化字节路径); 运行时断言 VerifyFingerprint(已有，缺的是测试级守卫) -->
<!-- 验证: ImmutablePrefixStableSortTests 10 例(含新增2例)全绿；编译 0 警告 0 错误 -->

## 13. 决策记录（Phase6 落地：剪裁优先于折叠，2026-08-06）

<!-- 🤖 Auto Decision: 2026-08-06 -->
<!-- 决策: 折叠前先 Snip — ContextFoldThresholds 增 MinSnipChars=1024/SnipHeadLines=40/SnipTailLines=40/SnipHeadChars=8000/SnipTailChars=8000；ContextFoldDecider.SnipStaleToolResults 用 ComputeTailBoundary(false) 定头部，对 role==Tool 且 ≥MinSnipChars 且未带 snipped: 前缀的消息做 RewriteSnipped(行数超 40+40 保头尾行，否则保头尾字符)，CompactInPlace 保留配对元数据；ChatContextManager.FoldIfNeededAsync 折叠前先 Snip，若剪裁后仍降到 FoldThreshold/AggressiveThreshold 之下则跳过摘要折叠并重置卡死计数，否则照常折叠且 Snip 计入 Folded 结果 -->
<!-- 原因: 对齐 Reasonix Go 版 prune_before_fold( prune.go 的 defaultToolResultSnipRatio=0.6，剪裁"免费"：可重派生、不丢消息、无摘要器调用)；先剪裁过期大工具结果再决定是否摘要，可省一轮摘要轮并保持折叠判定更准 -->
<!-- 替代方案: 仅摘要折叠(剪裁为空操作管道)；把剪裁并入 DecideAfterUsage(职责混乱，剪裁是结果维护非决策) -->
<!-- 验证: 新增 ContextFoldSnipTests 6 例(占位符改写/小结果跳过/保护区逐字保留/幂等/配对元数据保留/回归)全绿；Brain.Context.Tests 725 + PrefixCache 245 不回归；编译 0 警告 0 错误 -->

