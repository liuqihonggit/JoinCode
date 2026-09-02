# 缓存破坏检测维度补齐计划（对齐 TS promptCacheBreakDetection.ts）

> 创建时间: 2026-09-02
> 范围: 补齐 `CacheBreakDetector` 的检测维度与判定阈值，对齐 TS 原版 `promptCacheBreakDetection.ts`（727 行）
> 前置 ADR: [0055](../../adr/0055-system-prompt-section-injection-optimization.md)（系统提示词 section 注入优化空间）

## 0. 现状对比

### 0.1 jcc 现有实现（CacheBreakDetector.cs 192 行 + CacheBreakTypes.cs 50 行）

**已覆盖的 9 个维度**：
| 维度 | CacheBreakKind | 判定方式 |
|------|----------------|----------|
| 系统提示词 | `SystemPromptChanged` | `SystemPromptHash` 不匹配 |
| 工具规格 | `ToolSpecsChanged` | `ToolSpecsHash` 或 `ToolCount` 不匹配 + `ShouldReportToolSpecsBreak` |
| 动态内容 | `DynamicContentChanged` | `DynamicContentHash` 不匹配 |
| 缓存驱逐 | `CacheEviction` | `ShouldReportCacheEviction`：`allHashesMatch && CacheRead==0 && CacheCreation>0` |
| 模型变更 | `ModelChanged` | `ModelId` 不匹配 |
| 快速模式 | `FastModeChanged` | `FastMode` 不匹配 |
| 历史篡改 | `ConversationHistoryChanged` | 前 N 条消息联合 hash 不匹配（jcc 独有） |
| 压缩进入 | `CompactionEntered` | `_pendingCompaction && CacheRead==0 && CacheCreation>0` |
| 无 | `None` | 未检测到失效 |

**关键缺陷**：
- **D1（漏报）**：`ShouldReportCacheEviction` 只看 `CacheReadInputTokens == 0`（硬阈值），若缓存降幅 80%（如 10000→2000）则**不报**，误判为"健康"
- **D2（无排除）**：无 `isExcludedModel`，haiku 等不同缓存行为的模型也参与检测，产生噪声
- **D3（无 TTL 区分）**：驱逐统一报 `CacheEviction`，无法区分 5min TTL 过期 / 1h TTL 过期 / 服务端路由
- **D4（无基线跟踪）**：无 `prevCacheReadTokens`，无法算相对降幅
- **D5（无删除通知）**：无 `cacheDeletionsPending`，cached microcompact 主动删除缓存后的 token 下降被误报为驱逐
- **D6（无 per-tool 定位）**：工具 schema 变更只报聚合，无法定位是哪个工具（TS BQ 数据显示 77% 的工具破坏是同集合内 schema 变更）
- **D7（缺 6 维度）**：无 `cacheControlHash`/`betas`/`effortValue`/`globalCacheStrategy`/`extraBodyHash`/`autoModeActive` 等
- **D8（无 diff 输出）**：无 `writeCacheBreakDiff`，调试时无法对比前后 prompt 状态
- **D9（无 agent 隔离）**：单实例全局状态，多 agent 并发时互相覆盖基线

### 0.2 TS 原版实现（promptCacheBreakDetection.ts 727 行）

**12+ 维度 + 双阈值 + TTL 区分 + 多 agent 隔离**：
- `MIN_CACHE_MISS_TOKENS = 2000` + `cacheReadTokens >= prevCacheRead * 0.95`（相对 5% + 绝对 2000 双阈值）
- `prevCacheReadTokens` 跟踪相对降幅
- `isExcludedModel(model)` 排除 haiku
- TTL 时段区分：`CACHE_TTL_5MIN_MS` / `CACHE_TTL_1HOUR_MS` → reason 文案区分
- `cacheDeletionsPending` 机制
- `perToolHashes` 定位具体工具
- `cacheControlHash`（scope/TTL 翻转，stripped hash 看不到）
- `betas`/`effortValue`/`extraBodyHash`/`globalCacheStrategy`/`autoModeActive`/`isUsingOverage`/`cachedMCEnabled` 7 个额外维度
- `writeCacheBreakDiff` 输出 diff 文件到 `getClaudeTempDir()`
- `getTrackingKey` + `MAX_TRACKED_SOURCES = 10` 多 agent 隔离
- `sanitizeToolName` MCP 工具名脱敏（`mcp__` 前缀折叠为 `mcp`）

## 1. 分批实现计划

### P0 — 漏报修复 + 噪声排除 + TTL 区分（最高价值）

> **目标**：修复 D1 漏报（相对降幅 5% 漏报）、D2 噪声（haiku）、D3 TTL 区分
> **影响文件**：4 个源码 + 1 个测试
> **TDD**：先写红测试复现"降幅 80% 但未到 0 不报"的漏报场景

#### P0.1 相对降幅 5% + 绝对阈值 2000（修复 D1 漏报）

**改动文件**：
1. `foundation/Abstractions/01-ai/LLM/Chat/Cache/CacheBreakTypes.cs`
   - `PromptStateSnapshot` 新增 `PrevCacheReadTokens` 字段（`int?`，null 表示首次无基线）
2. `core/execution/Brain/src/Cache/CacheBreakDetector.cs`
   - 新增私有字段 `_prevCacheReadTokens`（`int?`）
   - `CheckCacheBreak` 入口：若 `_prevCacheReadTokens` 有值，传入 `ShouldReportCacheEviction`
   - `CheckCacheBreak` 出口：更新 `_prevCacheReadTokens = usage.CacheReadInputTokens`
   - 重写 `ShouldReportCacheEviction`：
     ```csharp
     protected virtual bool ShouldReportCacheEviction(TokenUsage usage, bool allHashesMatch, int? prevCacheRead)
     {
         if (!_hasPreviousCacheHit) return false;
         if (!allHashesMatch) return false;
         // 双阈值：相对降幅 >5% 且绝对降幅 >2000
         if (prevCacheRead is null or 0) return usage.CacheReadInputTokens == 0 && usage.CacheCreationInputTokens > 0;
         var tokenDrop = prevCacheRead.Value - usage.CacheReadInputTokens;
         return usage.CacheReadInputTokens < prevCacheRead.Value * 0.95 && tokenDrop >= 2000;
     }
     ```
3. `core/execution/Brain/tests/Context/CacheBreakMonitorTests.cs`
   - 新增红测试 `CheckCacheBreakAsync_CacheEviction_PartialDrop_Detected`：prevRead=10000, currRead=2000（降幅 80%，>2000）→ 应报 `CacheEviction`
   - 新增测试 `CheckCacheBreakAsync_CacheEviction_SmallDrop_NotReported`：prevRead=10000, currRead=9600（降幅 4%，<5%）→ 不报
   - 新增测试 `CheckCacheBreakAsync_CacheEviction_DropBelowThreshold_NotReported`：prevRead=3000, currRead=500（降幅 83%，但绝对降幅 2500 > 2000）→ 应报
   - 新增测试 `CheckCacheBreakAsync_CacheEviction_DropJustAboveMin_NotReported`：prevRead=3000, currRead=1500（降幅 50%，绝对降幅 1500 < 2000）→ 不报

**现有测试影响**：
- `CheckCacheBreakAsync_CacheEviction_Detected`（行 143-160）：prevRead=80, currRead=0，绝对降幅 80 < 2000 → **会失败**（不再报驱逐）
  - **修复**：调整测试数据为 prevRead=10000, currRead=0（绝对降幅 10000 > 2000）→ 仍报驱逐
- `FullPipeline_RecordCheck_RecordAgain_NoBreak`（行 220-236）：首次 miss 不报，逻辑不变（prevCacheRead 为 null）

#### P0.2 isExcludedModel 排除 haiku（修复 D2 噪声）

**改动文件**：
1. `core/execution/Brain/src/Cache/CacheBreakDetector.cs`
   - `CheckCacheBreak` 入口新增排除检查：
     ```csharp
     if (IsExcludedModel(currentModelId)) return CacheBreakResult.NoBreak();
     ```
   - 新增私有方法：
     ```csharp
     private static bool IsExcludedModel(string? modelId)
         => modelId is not null && modelId.Contains("haiku", StringComparison.OrdinalIgnoreCase);
     ```
2. `core/execution/Brain/tests/Context/CacheBreakMonitorTests.cs`
   - 新增测试 `CheckCacheBreakAsync_HaikuModel_Skipped`：modelId="claude-3-haiku" → 即使前缀全变也返回 `NoBreak`

#### P0.3 TTL 时段区分（修复 D3）

**改动文件**：
1. `foundation/Abstractions/01-ai/LLM/Chat/Cache/CacheBreakTypes.cs`
   - `CacheBreakKind` 新增枚举值：
     - `TtlExpiration5Min` — 5min TTL 过期
     - `TtlExpiration1Hour` — 1h TTL 过期
     - `ServerSideRouting` — 服务端路由/驱逐（<5min gap）
2. `core/execution/Brain/src/Cache/CacheBreakDetector.cs`
   - `CheckCacheBreak` 的 `ShouldReportCacheEviction` 分支改为调用新方法 `ClassifyCacheMiss`：
     ```csharp
     if (ShouldReportCacheEviction(usage, allHashesMatch, prevCacheRead))
     {
         var (kind, detail) = ClassifyCacheMiss(usage, timeSinceLastCall);
         return CacheBreakResult.Break(kind, detail);
     }
     ```
   - 新增 `ClassifyCacheMiss`（需要时间参数，通过 `Func<DateTimeOffset>? clock` 注入，测试可控）：
     ```csharp
     private static (CacheBreakKind kind, string detail) ClassifyCacheMiss(
         TokenUsage usage, TimeSpan? timeSinceLastCall)
     {
         if (timeSinceLastCall is null) return (CacheBreakKind.ServerSideRouting, "unknown cause");
         if (timeSinceLastCall.Value > TimeSpan.FromHours(1)) return (CacheBreakKind.TtlExpiration1Hour, "possible 1h TTL expiry (prompt unchanged)");
         if (timeSinceLastCall.Value > TimeSpan.FromMinutes(5)) return (CacheBreakKind.TtlExpiration5Min, "possible 5min TTL expiry (prompt unchanged)");
         return (CacheBreakKind.ServerSideRouting, "likely server-side (prompt unchanged, <5min gap)");
     }
     ```
   - `RecordPromptState` / `CheckCacheBreak` 新增可选 `clock` 参数（`Func<DateTimeOffset>?`），记录上次调用时间戳
3. `foundation/Abstractions/01-ai/LLM/Chat/Session/SessionStats.cs`
   - 新增计数器：`TtlExpiration5MinBreaks` / `TtlExpiration1HourBreaks` / `ServerSideRoutingBreaks`
   - `RecordTurn` switch 补 3 个 case
   - `Reset` 补 3 个清零
4. `core/execution/Brain/tests/Context/CacheBreakMonitorTests.cs`
   - 新增测试 `CheckCacheBreakAsync_Ttl5Min_Detected`：gap=6min → `TtlExpiration5Min`
   - 新增测试 `CheckCacheBreakAsync_Ttl1Hour_Detected`：gap=61min → `TtlExpiration1Hour`
   - 新增测试 `CheckCacheBreakAsync_ServerSide_Detected`：gap=2min → `ServerSideRouting`

**AOT 约束**：新增 `CacheBreakKind` 枚举值后需 `--no-incremental` 全量重建（源码生成器重新扫描）

---

### P1 — 维度补齐（cacheControlHash + perToolHashes + effort + betas + globalCacheStrategy + extraBody）

> **目标**：补齐 D6（per-tool 定位）+ D7（6 维度）
> **影响文件**：3 个源码 + 1 个测试
> **依赖**：P0 完成

#### P1.1 perToolHashes（定位具体工具 schema 变更）

**改动文件**：
1. `foundation/Abstractions/01-ai/LLM/Chat/Cache/CacheBreakTypes.cs`
   - `PromptStateSnapshot` 新增 `PerToolHashes`（`IReadOnlyDictionary<string, string>`）
   - `CacheBreakResult` 新增 `ChangedToolSchemas`（`IReadOnlyList<string>?`）
2. `core/execution/Brain/src/Cache/CacheBreakDetector.cs`
   - `RecordPromptState` 计算每个工具的 hash（仅在聚合 hash 变更时才算，避免 N 次冗余序列化）
   - `CheckCacheBreak` 的 `ToolSpecsChanged` 分支：对比 perToolHashes，填充 `ChangedToolSchemas`
3. 测试：新增 `CheckCacheBreakAsync_PerToolHash_LocatesChangedTool`

#### P1.2 cacheControlHash（scope/TTL 翻转）

**改动文件**：
1. `CacheBreakTypes.cs` — `PromptStateSnapshot` 新增 `CacheControlHash`
2. `CacheBreakDetector.cs` — `RecordPromptState` 计算 cache_control 的 hash（含 scope/TTL），`CheckCacheBreak` 新增分支
3. `CacheBreakKind` 新增 `CacheControlChanged`

#### P1.3 effortValue + betas + globalCacheStrategy + extraBodyHash

**改动文件**：
1. `CacheBreakTypes.cs` — `PromptStateSnapshot` 新增 4 字段
2. `CacheBreakDetector.cs` — `RecordPromptState` / `CheckCacheBreak` 新增 4 分支
3. `CacheBreakKind` 新增 `EffortChanged` / `BetasChanged` / `GlobalCacheStrategyChanged` / `ExtraBodyChanged`
4. `SessionStats.cs` — 新增 4 计数器 + switch case + Reset

---

### P2 — 机制补齐（cacheDeletionsPending + sanitizeToolName + feature gate）

> **目标**：修复 D5（删除通知误报）+ D10（MCP 工具名脱敏）+ feature gate
> **依赖**：P1 完成

#### P2.1 notifyCacheDeletion

**改动文件**：
1. `CacheBreakDetector.cs` — 新增 `NotifyCacheDeletion()` 方法，设置 `_cacheDeletionsPending = true`
2. `CheckCacheBreak` 入口：若 `_cacheDeletionsPending`，清除标记并返回 `NoBreak`（预期下降，非破坏）
3. 调用方：`ChatContextManager` 或 `MicrocompactMiddleware` 在发送 cache_edits deletions 后调用

#### P2.2 sanitizeToolName

**改动文件**：
1. `CacheBreakDetector.cs` — 新增私有方法 `SanitizeToolName`：`mcp__` 前缀折叠为 `mcp`
2. `ChangedToolSchemas` 填充时调用

#### P2.3 feature gate

**改动文件**：
1. `CacheBreakDetector.cs` — 构造函数新增 `ICacheBreakOptions?`（启用/禁用开关）
2. `app/JoinCode/` 配置加载

---

### P3 — 可观测性（diff 文件输出 + 多 agent 隔离）

> **目标**：补齐 D8（diff 输出）+ D9（agent 隔离）
> **依赖**：P2 完成

#### P3.1 diff 文件输出

**改动文件**：
1. `CacheBreakDetector.cs` — `CheckCacheBreak` 检测到破坏时，调用 `WriteCacheBreakDiffAsync`
2. 新建 `CacheBreakDiffWriter.cs`（或加到现有工具类）— 用 `System.IO` 写 diff 文件到 `.jcc/cache-break-{随机4字符}.diff`
3. AOT 约束：用 `XxHash32` 生成随机后缀，不用 `Random`（AOT 友好）

#### P3.2 多 agent 隔离

**改动文件**：
1. `CacheBreakDetector.cs` — 改为 `ConcurrentDictionary<string, AgentState>`，key = `agentId ?? "main"`
2. `MAX_TRACKED_SOURCES = 10`，超出时淘汰最旧
3. `NotifyCompaction` / `Reset` / `NotifyCacheDeletion` 新增 `agentId` 参数

---

## 2. TDD 红测试设计（P0.1 漏报复现）

```csharp
[Fact]
public async Task CheckCacheBreakAsync_CacheEviction_PartialDrop_Detected()
{
    var sut = CreateSut();
    await sut.UpdateSystemPromptAsync("system").ConfigureAwait(true);
    await sut.AddDynamicSystemMessageAsync("dynamic").ConfigureAwait(true);
    await sut.UpdateToolSpecsAsync([new ToolSpec("tool_a", "desc_a")]).ConfigureAwait(true);

    var snapshot = await sut.RecordPromptStateAsync().ConfigureAwait(true);

    // 第一轮：缓存命中 10000 tokens（建立基线）
    var usageWithHit = new TokenUsage(10000, 50) { CacheReadInputTokens = 10000, CacheCreationInputTokens = 0 };
    await sut.CheckCacheBreakAsync(snapshot, usageWithHit).ConfigureAwait(true);

    // 第二轮：缓存降幅 80%（10000 → 2000），绝对降幅 8000 > 2000
    // 现状：CacheReadInputTokens=2000 != 0 → ShouldReportCacheEviction 返回 false → 漏报
    // 期望：相对降幅 80% > 5% 且绝对降幅 8000 > 2000 → 应报 CacheEviction
    var usageWithPartialMiss = new TokenUsage(10000, 50) { CacheReadInputTokens = 2000, CacheCreationInputTokens = 8000 };
    var result = await sut.CheckCacheBreakAsync(snapshot, usageWithPartialMiss).ConfigureAwait(true);

    result.BreakDetected.Should().BeTrue();
    result.Kind.Should().Be(CacheBreakKind.CacheEviction);
}
```

## 3. AOT 兼容约束

| 约束 | 说明 |
|------|------|
| `CacheBreakKind` 枚举新增值 | 需 `--no-incremental` 全量重建（源码生成器重新扫描 `[EnumValue]`） |
| 随机后缀（diff 文件名） | 用 `XxHash32` + `DateTimeOffset.UtcNow.Ticks`，不用 `Random`（AOT 友好） |
| JSON 序列化 | 用 `RelaxedJsonSerializer`（> ADR: [0042](../../adr/0042-json-relaxed-serializer-unification.md)） |
| 查找集 | `MAX_TRACKED_SOURCES` 用 `FrozenDictionary`/`ConcurrentDictionary`，不用 `List<T>.Contains` |
| 时间注入 | `Func<DateTimeOffset>? clock = null`，测试可控、生产用 `DateTimeOffset.UtcNow` |

## 4. 编译策略

| 阶段 | 编译命令 | 说明 |
|------|----------|------|
| 开发 | `dotnet build core/execution/Brain/src/Brain.csproj -c Debug` | 改 CacheBreakDetector 后只编译 Brain |
| 测试 | `dotnet build core/execution/Brain/tests/Brain.Context.Tests.csproj -c Debug` | 测试编译 |
| 提交前 | `dotnet test core/execution/Brain/tests/Brain.Context.Tests.csproj -c Debug --filter "CacheBreak"` | 只跑缓存相关测试 |
| 枚举变更后 | `dotnet build Foundation.slnx -c Debug --no-incremental` | P0.3 新增枚举值需全量重建 |

## 5. 风险与回滚

| 风险 | 缓解 |
|------|------|
| P0.1 改 `ShouldReportCacheEviction` 破坏现有测试 | 已列出受影响测试（`CheckCacheBreakAsync_CacheEviction_Detected`），同步调整数据 |
| P0.3 新增枚举值需全量重建 | 编译策略已标注 `--no-incremental` |
| P1 维度补齐可能引入误报 | 每个维度独立测试，先验证不破坏现有 243+11 个测试 |
| P3 多 agent 隔离改并发结构 | 放最后，前面 P0-P2 都是单实例增量改动 |

## 6. 实现顺序

```
P0.1（漏报修复）✅ commit 4df5bc4dd
P0.2（haiku 排除）✅ commit 61f85df55
P0.3（TTL 区分）✅ commit e9b05c3a5
P1.1（perToolHashes）⏭️ 跳过 — ToolDriftReport.EditedNames 已提供 per-tool 定位
P1.2（cacheControlHash）⏭️ 跳过 — jcc 不支持三级 cache scope（ADR 0055 P1 未实现）
P1.3（effort+betas+strategy+extraBody）⏭️ 跳过 — Anthropic 专属参数，jcc 多供应商不适用
P2.1（notifyCacheDeletion）✅ commit 1c5105bf0
P2.2（sanitizeToolName）✅ commit 4bbf5a18f
P2.3（feature gate）⏸️ 暂缓 — 默认全开不影响现有行为
P3.1（diff 输出）⏸️ 暂缓 — 低价值（jcc 无 --debug 查看渠道）
P3.2（agent 隔离）⏸️ 暂缓 — 高风险大改 IChatContextManager 接口，留给用户决策
```

每步遵循：红测试 → 实现 → 编译 → 绿测试 → git 提交

**验证结果**：28 CacheBreak + 262 PrefixCache 测试全绿

<!-- 🤖 Auto Decision: 2026-09-02 -->
<!-- 决策: 分 4 批 P0-P3 渐进式实现，P0 优先修复漏报 -->
<!-- 原因: P0 的相对降幅 5% 漏报是最高价值修复（现状 80% 降幅都不报），P1-P3 是维度/机制/可观测性补齐 -->
<!-- 替代方案: 一次性全量对齐 TS（风险高，破坏面大，违反渐进式原则）-->
<!-- 验证: P0+P2 已实现，28+262 测试全绿 ✅ -->
