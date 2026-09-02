# 0056. 缓存破坏检测维度补齐 — 双阈值 + TTL 区分 + 多 agent 隔离

- 状态：accepted
- 日期：2026-09-02
- 决策者：项目架构组
- 关联：[0055](0055-system-prompt-section-injection-optimization.md)（P0 缓存破坏检测机制）
- 实现计划：[cache-break-detection-plan.md](../plans/refactor/cache-break-detection-plan.md)
- 验证：P0 已实现并测试通过（26 CacheBreak + 262 PrefixCache 测试全绿）

## 背景

jcc 已有 `CacheBreakDetector`（192 行，9 维度，243+11 测试），但与 TS 原版 `promptCacheBreakDetection.ts`（727 行，12+ 维度）对比存在 9 项缺陷：

### 核心漏报（D1）

`ShouldReportCacheEviction` 只看 `CacheReadInputTokens == 0`（硬阈值）。若缓存从 10000 降到 2000（降幅 80%），因 `CacheReadInputTokens = 2000 != 0`，**不报驱逐**，误判为"缓存健康"。TS 原版用双阈值：相对降幅 >5% **且** 绝对降幅 >2000，能捕获此场景。

### 噪声与误报（D2-D5）

- **D2**：无 `isExcludedModel`，haiku 等不同缓存行为的模型产生噪声
- **D3**：驱逐统一报 `CacheEviction`，无法区分 5min TTL / 1h TTL / 服务端路由
- **D5**：无 `cacheDeletionsPending`，cached microcompact 主动删除后的 token 下降被误报为驱逐

### 维度缺失（D6-D9）

- **D6**：无 `perToolHashes`，工具 schema 变更只报聚合，无法定位具体工具（TS BQ 数据：77% 是同集合内 schema 变更）
- **D7**：缺 `cacheControlHash`/`betas`/`effortValue`/`globalCacheStrategy`/`extraBodyHash`/`autoModeActive` 6 维度
- **D8**：无 diff 文件输出，调试无法对比前后 prompt 状态
- **D9**：单实例全局状态，多 agent 并发互相覆盖基线

## 决策（提议）

分 4 批渐进式对齐 TS，每批独立可提交，遵循 TDD 红绿循环：

### P0 — 漏报修复 + 噪声排除 + TTL 区分（最高价值）

1. **双阈值**：`CacheReadInputTokens < prevCacheRead * 0.95 && tokenDrop >= 2000`，新增 `_prevCacheReadTokens` 跟踪基线
2. **haiku 排除**：`IsExcludedModel(modelId)` 检查 `Contains("haiku")`
3. **TTL 时段区分**：新增 `CacheBreakKind.TtlExpiration5Min` / `TtlExpiration1Hour` / `ServerSideRouting`，通过 `Func<DateTimeOffset>? clock` 注入时间，`ClassifyCacheMiss` 按 gap 分类

### P1 — 维度补齐

`perToolHashes`（定位具体工具）+ `cacheControlHash`（scope/TTL 翻转）+ `effortValue` + `betas` + `globalCacheStrategy` + `extraBodyHash`

### P2 — 机制补齐

`notifyCacheDeletion`（预期删除不误报）+ `sanitizeToolName`（MCP 工具名脱敏）+ feature gate

### P3 — 可观测性

diff 文件输出（`XxHash32` 生成随机后缀，AOT 友好）+ 多 agent 隔离（`ConcurrentDictionary<string, AgentState>`，`MAX_TRACKED_SOURCES = 10`）

## 替代方案

### 方案 A：一次性全量对齐 TS（放弃）

- 一次性补齐 12+ 维度 + 双阈值 + TTL + agent 隔离 + diff 输出
- **放弃原因**：破坏面大（9 项缺陷同时改），违反渐进式原则（ADR [0007](0007-progressive-development.md)），难以定位回归
- **风险**：现有 243+11 个测试可能大面积破坏，且无法区分哪批改动引入的问题

### 方案 B：只修 P0 漏报，不补维度（放弃）

- 只改 `ShouldReportCacheEviction` 加双阈值，不补 P1-P3
- **放弃原因**：P0 修复了漏报但维度仍不全（D6-D9 仍在），无法定位具体工具 schema 变更、无法区分 TTL、多 agent 互相覆盖
- **适用场景**：若用户只想快速修复漏报，可只做 P0，但 TS 对齐目标无法达成

### 方案 C：用绝对阈值 5000 替代双阈值（放弃）

- 只用 `tokenDrop >= 5000`，不看相对降幅
- **放弃原因**：小基线场景（如 prevRead=6000, currRead=1000，降幅 83% 但绝对降幅 5000 刚好触发）边界敏感；大基线场景（如 prevRead=100000, currRead=95000，降幅 5% 但绝对降幅 5000 触发）误报
- **TS 选择双阈值的原因**：相对 5% 过滤正常波动，绝对 2000 过滤小基线噪声，两者 AND 关系

## 后果

- **正面**：
  - P0 修复漏报：缓存降幅 80% 的场景不再误判为"健康"，可正确归因
  - P0.3 TTL 区分：驱逐原因可区分 5min/1h/服务端，辅助诊断
  - P1 perToolHashes：工具 schema 变更可定位到具体工具名
  - P3 多 agent 隔离：并发子代理不再互相覆盖基线
- **负面**：
  - P0.1 改 `ShouldReportCacheEviction` 破坏现有测试 `CheckCacheBreakAsync_CacheEviction_Detected`（prevRead=80, currRead=0，绝对降幅 80 < 2000），需同步调整测试数据
  - P0.3 新增 `CacheBreakKind` 枚举值需 `--no-incremental` 全量重建
  - P3 改并发结构（`ConcurrentDictionary`），需验证线程安全
- **中性**：
  - P2 feature gate 增加配置项，但默认全开，不影响现有行为
  - P3 diff 文件写入 `.jcc/` 目录，需确保目录存在

## 阈值选择依据

| 阈值 | 值 | 来源 | 理由 |
|------|-----|------|------|
| 相对降幅 | 5% | TS `prevCacheRead * 0.95` | 过滤正常波动（±5% 是 Anthropic 缓存的正常抖动范围） |
| 绝对降幅 | 2000 tokens | TS `MIN_CACHE_MISS_TOKENS` | 过滤小基线噪声（<2000 tokens 的降幅不值得告警） |
| TTL 5min | 5 分钟 | TS `CACHE_TTL_5MIN_MS` | Anthropic 默认缓存 TTL |
| TTL 1h | 1 小时 | TS `CACHE_TTL_1HOUR_MS` | Anthropic 长效缓存 TTL |
| agent 上限 | 10 | TS `MAX_TRACKED_SOURCES` | 防止子代理无限增长内存（每个 entry ~300KB） |

<!-- 🤖 Auto Decision: 2026-09-02 -->
<!-- 决策: 分 4 批 P0-P3 渐进式对齐 TS，P0 优先修复双阈值漏报 -->
<!-- 原因: P0 的相对降幅 5% 漏报是最高价值修复（现状 80% 降幅都不报），P1-P3 是维度/机制/可观测性补齐 -->
<!-- 替代方案: 一次性全量对齐（风险高，破坏面大，违反渐进式原则）-->
<!-- 验证: ADR 已写，待 P0 实现后改状态为 accepted -->
