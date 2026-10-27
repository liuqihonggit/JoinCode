# TASK069-E5 无人值守重试差异分析

> **状态**：待用户决策
> **来源**：TASK069 域5 错误与重试 E5
> **创建时间**：2026-10-28
> **判定**：功能相同但实现不同 — 差异项，非 bug

---

## 一、差异描述

基准 ClaudeCode TS 在无人值守（unattended）模式下对 429/529 启用 **无限重试 + 6h cap + 30s 心跳**。C# 侧已有无限重试机制（`ResilientHttpExecutor` 24h 预算驱动）和心跳（Bridge v2 传输层 20s），但实现方式与基准有 5 处差异。

---

## 二、双侧源码证据

### 2.1 基准侧（TS）

| 要素 | 实现 | 位置 |
|------|------|------|
| 无人值守判定 | `isPersistentRetryEnabled()` = `feature('UNATTENDED_RETRY') && isEnvTruthy(CLAUDE_CODE_UNATTENDED_RETRY)` | `withRetry.ts:100-104` |
| 触发条件 | 仅 429 + 529（`isTransientCapacityError`） | `withRetry.ts:106-110` |
| 无限重试 | 钳制 for 循环 `attempt` 计数器，循环永不终止 | `withRetry.ts:504-506` |
| 6h cap | `PERSISTENT_RESET_CAP_MS = 6*60*60*1000`，单次延迟上限 | `withRetry.ts:97,446,459,821` |
| 30s 心跳 | `HEARTBEAT_INTERVAL_MS = 30_000`，分块睡眠 + yield `SystemAPIErrorMessage` 到 stdout | `withRetry.ts:98,477-506` |
| 退避算法 | `500 * 2^(n-1)` + 25% 抖动，maxDelay 5min | `withRetry.ts:530-548` |
| Retry-After 优先 | 服务器返回 `retry-after` 头时直接使用，绕过 maxDelay | `withRetry.ts:533-538` |
| 绕过 529 连续限制 | persistent 模式绕过 `MAX_529_RETRIES=3` | `withRetry.ts:353-364` |
| 绕过 fast mode fallback | persistent 模式跳过 fast mode 切换 | `withRetry.ts:267-272` |
| 绕过 subscriber gates | 429/529 总是 retryable | `withRetry.ts:702-706` |

### 2.2 C# 侧

| 要素 | 实现 | 位置 |
|------|------|------|
| 无人值守判定 | CLI 层 `TestEnvironmentDetector.IsNonInteractive` + 权限层 `PermissionMode.Unattended` | `TestEnvironmentDetector.cs:28` / `GoalEngine.cs:937` |
| **模式与重试耦合** | **无耦合** — 重试策略由配置决定，与无人值守模式无关 | — |
| 无限重试 | `ResilientHttpExecutor` 当 `RetryConfig.TotalBudget` 非 null 时无限重试至预算耗尽 | `ResilientHttpExecutor.cs:152,166` |
| 24h 预算 | `NetworkRetryOptions.TotalBudget = TimeSpan.FromHours(24)` — **总预算时间，非单次延迟上限** | `NetworkRetryOptions.cs:11` |
| 心跳 | Bridge v2 传输层 `RunHeartbeatLoopAsync`，20s 间隔 + ±10% 抖动，POST `/worker/heartbeat` | `V2ReplBridgeTransport.cs:443-480` / `V2BridgeConfig.cs:33-39` |
| 退避算法 | `BaseDelay * 2^(n-1)` + 抖动（0.75-1.25 倍），BaseDelay 2s，MaxDelay 5min | `ResilientHttpExecutor.cs:251-268` |
| Retry-After | **未优先处理** — 退避算法不读 Retry-After 头 | `ResilientHttpExecutor.cs:251-268` |
| 429/529 区分 | **不区分** — `ShouldRetry` 只看异常类型（HttpRequestException/Timeout/IO），不看状态码 | `ResilientHttpExecutor.cs:240-249` |
| QueryEngine 重试 | 独立重试层，3 次有限重试，`IsRetryable` 也不区分 429/529 | `QueryEngine.cs:182-195,602-607` |
| 529 降级 | `StreamingFallbackConfig.FallbackStatusCodes = [529,503,502]` — 529 时降级非流式，非重试 | `StreamingFallbackConfig.cs:42` |

---

## 三、五处差异详析

### 差异 1：无人值守模式不驱动重试策略

- **基准**：`isPersistentRetryEnabled()` 为 true 时，重试从有限（10 次）切换为无限 + 6h cap + 30s 心跳 + 绕过 529 限制
- **C#**：`IsNonInteractive` / `PermissionMode.Unattended` 均不改变重试配置。无论模式，`ResilientHttpExecutor` 始终用 `NetworkRetryOptions` 配置（默认 24h 预算无限重试）
- **影响**：C# 在交互模式下也用 24h 无限重试（比基准交互模式 10 次更宽松）；无人值守模式下不额外改变行为
- **评估**：C# 默认更宽松，不会出现基准交互模式"10 次后放弃"的场景。但 C# 无法在交互模式下退回到有限重试

### 差异 2：24h 预算 vs 6h cap（语义不同）

- **基准**：6h cap 是**单次延迟上限**（`Math.min(delay, 6h)`），总重试时间可超过 6h（多次 6h 延迟）
- **C#**：24h 预算是**总重试时间上限**（`Stopwatch.Elapsed >= budget` 时放弃），单次延迟上限是 MaxDelay=5min
- **影响**：C# 总等待时间 24h > 基准单次 6h，但基准可多次 6h 累加。实际行为差异：C# 24h 后必放弃，基准理论上可无限重试（每次延迟 ≤6h）
- **评估**：C# 24h 总预算更保守（有明确放弃点），基准 6h 单次 cap 更宽松（无总放弃点）

### 差异 3：心跳位置不同

- **基准**：心跳在**重试等待期间**，分块睡眠 30s + yield `SystemAPIErrorMessage` 到 stdout，让宿主看到周期性活动
- **C#**：心跳在 **Bridge v2 传输层**，20s 间隔 POST `/worker/heartbeat`，与 API 重试独立
- **影响**：C# 在非 Bridge 场景（直连 API）下，重试等待期间无心跳，宿主可能标记会话 idle
- **评估**：C# 心跳覆盖 Bridge 场景，不覆盖直连 API 场景。基准心跳覆盖所有重试等待场景

### 差异 4：不区分 429/529

- **基准**：persistent 模式仅对 429/529 无限重试，其他错误仍用有限重试
- **C#**：`ResilientHttpExecutor.ShouldRetry` 不看状态码，所有 `HttpRequestException/Timeout/IO` 异常都无限重试（24h 预算内）
- **影响**：C# 对 500/502/503/504 等也无限重试，基准对这些错误仍有限重试
- **评估**：C# 更激进（对所有服务端错误无限重试），基准更保守（仅对容量错误 429/529 无限重试）

### 差异 5：无 Retry-After 头优先

- **基准**：服务器返回 `retry-after` 头时直接用 `seconds * 1000` 作为延迟，绕过 maxDelay（但受 6h cap 兜底）
- **C#**：退避算法不读 Retry-After 头，始终用指数退避
- **影响**：C# 可能比服务器建议的更早重试（指数退避 < Retry-After），导致更多 429
- **评估**：Retry-After 是服务器明确指示，优先处理可减少无效重试

---

## 四、四问回答

### 1. 专家隐性实践
顶尖专家会：① 在真实 429/529 场景下实测两侧重试行为差异（用 MockServer 模拟限流）② 查证 Retry-After 头在 Anthropic API 的实际返回行为 ③ 验证 C# 24h 预算在长会话中的实际表现 ④ 评估"不区分 429/529"是否导致非容量错误被无限重试（如 500 持续错误时 24h 不放弃）

### 2. 原始素材清单
- 基准侧 `withRetry.ts` 源码（已查）
- C# 侧 `ResilientHttpExecutor.cs` / `NetworkRetryOptions.cs` / `QueryEngine.cs` 源码（已查）
- Anthropic API 文档（Retry-After 头语义、429/529 区别）— **未查证**
- 基准侧 `feature('UNATTENDED_RETRY')` 特性开关的启用条件 — **未查证**（ant-only 含义）

### 3. 具体方法与工具
- MockServer 模拟 429/529 响应，对比两侧重试行为
- `gh pr view` / `git log` 查基准 `UNATTENDED_RETRY` 特性的引入背景
- Anthropic API 文档查 Retry-After 头规范

### 4. 独立验证来源
- MockServer E2E 测试（模拟限流场景）
- Anthropic API 文档（Retry-After 头规范）
- 基准侧 `withRetry.ts` 测试套件

---

## 五、修复方案选项

### 方案 A：不修复（C# 现状已足够）
- **理由**：C# 默认 24h 无限重试比基准交互模式更宽松，无人值守场景下行为合理。24h 预算有明确放弃点，比基准 6h 单次 cap 更保守安全
- **代价**：差异 3（非 Bridge 场景无心跳）和差异 5（无 Retry-After 优先）可能在实际使用中造成问题

### 方案 B：部分对齐（修复差异 3 + 5）
- **差异 3**：在 `ResilientHttpExecutor` 重试等待期间加分块睡眠 + 心跳回调
- **差异 5**：退避算法优先读 Retry-After 头
- **代价**：改动 `ResilientHttpExecutor` 核心逻辑，需全仓编译
- **价值**：覆盖直连 API 场景的心跳 + 减少无效重试

### 方案 C：全面对齐（5 处差异全部修复）
- **差异 1**：加 `IsNonInteractive` / `PermissionMode.Unattended` → 重试策略切换
- **差异 2**：加 6h 单次延迟 cap（`Math.Min(delay, 6h)`）
- **差异 3**：重试等待期间心跳
- **差异 4**：`ShouldRetry` 区分 429/529（persistent 模式仅 429/529 无限）
- **差异 5**：Retry-After 头优先
- **代价**：大改 `ResilientHttpExecutor` + `NetworkRetryOptions` + `QueryEngine`，跨域编译
- **价值**：完全对齐基准行为

---

## 六、建议

**建议方案 B**（部分对齐）：
- 差异 1（模式不驱动重试）：C# 默认更宽松，不修复风险低
- 差异 2（24h vs 6h）：语义不同但 C# 更保守，不修复
- **差异 3（心跳位置）**：修复 — 直连 API 场景需心跳
- 差异 4（不区分 429/529）：C# 更激进但不会导致错误行为，不修复
- **差异 5（Retry-After）**：修复 — 减少无效重试，提升效率

---

<!-- 🤖 Auto Decision: 2026-10-28 -->
<!-- 决策: E5 调研完成，判定为差异项非 bug，落差异文档待用户决策 -->
<!-- 原因: C# 已有无限重试(24h预算)+心跳(Bridge 20s)，与基准实现不同但非缺失 -->
<!-- 替代方案: 直接判 bug 修复 — 否决，无原始规范证据证明 C# 行为错误 -->
<!-- 验证: 双侧源码证据已列出，四问已回答 -->
