# ADR 0156: 上下文窗口主动预防体系改进

> 📍 **导航**: [docs/](../../README.md) › [adr/](../README.md)
> 🔗 **上游索引**: [adr/README.md](../README.md) — 修改本文档后须同步更新此索引

> 状态: proposed
> 创建: 2026-11-14
> 决策者: 用户主导（要求主动计算 token 预防溢出，禁止 Agent 因上下文溢出崩溃退出）
> 影响: modules/cognition/brain.context_services/ + modules/cognition/brain.query/ + modules/cognition/brain.context_fold/ + modules/cognition/brain.context_compact/ + modules/abstractions/ai/llm/chat/context_fold/
> 关联: [ADR 0080](../implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md)（手动 exe 验收）、[ADR 0086](../implemented/adr/component_strategy/0086-core-tech-selection-lock-design.md)（锁设计与死锁防护）

## 背景

### 用户诉求

> "LLM 有窗口大小，每个工具有不定长输出，当工具输出 b > 窗口剩余 a 时如何控制 b < a？是否可以做一些计算，避免突破上下文出现报错？而是主动进行上下文压缩。否则任何报错导致 Agent 闪退、停止、结束，都是不对的。"

核心原则：**Agent 永远不应因上下文溢出而崩溃退出。应该在发送 API 请求前主动计算 token 总量，预测是否会超限，提前压缩。**

### 当前系统的 7 层防御体系

| 层级 | 机制 | 触发时机 | 位置 |
|------|------|---------|------|
| L1 | `ToolResultTruncator` — 即时截断 | 工具执行时 | `cognition/hands_core/.../ToolResultTruncator.cs` |
| L2 | `MaybePersistLargeToolResult` — 单工具持久化 | 工具结果超阈值 | `cognition/brain.context_core/.../ContentReplacementService.cs` |
| L3 | `ApplyToolResultBudgetAsync` — 预算检查 | 每组工具结果总和超 200K chars | 同 L2 |
| L4 | `HistorySnipMiddleware` — 历史裁剪 | 工具调用后，剩余预算 <10% | `cognition/brain.query/.../HistorySnipMiddleware.cs` |
| L5 | `ContextFoldService` — 上下文折叠 | API 返回 usage 后，比率 >0.5 | `cognition/brain.context_services/.../ContextFoldService.cs` |
| L6 | `ReactiveCompactService` — 响应式压缩 | API 返回 `prompt_too_long` 错误 | `cognition/brain.context_compact/.../ReactiveCompactService.cs` |
| L7 | `ContextOverflowException` — 紧急溢出 | 折叠后仍超紧急阈值 | 同 L5 + `QueryLoopMiddleware.GuardContextBudget` |

### 5 个缝隙的详细分析

#### 缝隙 1：Token 估算精度极粗糙

**现状**：`ContextFoldDecider.EstimateTokenCount()` 使用 `字符总数 / CharsPerToken(=4)` 估算 token 数。

**问题**：
- 中文内容：1 个汉字 ≈ 1-2 token，但 `1字符/4` 估算 = 0.25 token，**低估 4-8 倍**
- 代码内容：BPE 分词与字符数无线性关系，JSON 结构开销未计入
- 消息格式开销：角色标记、结构化包装（如 `<tool_call>` 标签）未计入
- 工具规格开销：`InputSchemaJson` 的 JSON 结构 token 开销远大于字符数/4

**影响**：预检认为"没超限"，实际已超限 → API 返回 `prompt_too_long` → 退化到 L6 被动压缩

**证据**：
- `ContextFoldDecider.cs:83-104` — `totalChars / t.CharsPerToken`，`CharsPerToken = 4`
- `MicrocompactService.cs:181-212` — 稍精细，用 `字符数/4 * 4/3` 修正系数，但仍粗糙
- 对比基准：Anthropic 官方 tokenizer 对中文约 1.5 字符/token，对代码约 2-3 字符/token

#### 缝隙 2：预检阈值过高，危险区间不拦截

**现状**：`GuardContextBudget()` 只在 `EstimatedRatio >= 1.0` 时抛异常拦截。

**问题**：
- `DecidePreflight` 的 `EmergencyThreshold = 0.95`，但 `GuardContextBudget` 用 `>= 1.0` 而非 `NeedsAction`
- 0.5~1.0 之间的危险区间：明知快满了，但照常发送 API
- 如果估算偏低（缝隙 1），实际比率可能已达 1.2+，但估算只有 0.8 → 不拦截 → API 报错

**影响**：本可主动压缩的窗口被浪费，退化到 L6 被动压缩（浪费一次 API 调用）

**证据**：
- `QueryLoopMiddleware.cs:245` — `if (preflight is not { EstimatedRatio: >= 1.0 }) return;`
- `ContextFoldDecider.cs:71` — `NeedsAction = ratio > t.EmergencyThreshold`（0.95），但 `GuardContextBudget` 未使用此字段

#### 缝隙 3：主动压缩是事后的，无法预防单轮内突增

**现状**：`ContextFoldService` 在 API 返回 usage 数据后才触发折叠决策。

**问题**：
- 场景：上轮 API 返回后比率 = 0.4（不触发折叠）→ 本轮工具产生大输出 → 本轮发送 API 时比率跳到 0.9
- 预检不拦截（< 1.0）→ API 发送 → 可能收到 `prompt_too_long`
- `HistorySnipMiddleware` 本可在工具调用后裁剪，但默认不生效（缝隙 4）

**影响**：单轮内 token 突增无法预防，退化到 L6 被动压缩

**证据**：
- `ChatUsageProcessor.cs:53-56` — `DecideAfterUsage(usage)` 基于 API 返回的 `PromptTokens`
- `ProcessUsageMiddleware` 在 chat-pipeline 中位于 `query-loop-mw` 之后

#### 缝隙 4：HistorySnipMiddleware 默认不生效

**现状**：`HistorySnipMiddleware` 依赖 `ITokenBudgetManager` 注入，未注入时不注册裁剪钩子。

**问题**：
- `TokenBudgetManager` 的预算需要 `QueryEngine` 中 `options?.TokenBudget is > 0` 才分配
- 默认情况下 `TokenBudget` 未设置 → `GetRemainingBudgetAsync` 返回 `long.MaxValue` → 裁剪永不触发
- 这是唯一能在"工具调用后、API 请求前"主动裁剪的中间件，但默认形同虚设

**影响**：L4 防御层缺失，工具调用后的大输出直接累积到 API 请求

**证据**：
- `HistorySnipMiddleware.cs:34` — `if (_historySnipService is not null && _tokenBudgetManager is not null)`
- `HistorySnipMiddleware.cs:44-45` — `remaining < context.Config.MaxTokens * 0.1`，但 `remaining = long.MaxValue` 时永不触发

#### 缝隙 5：ContextOverflowException 无恢复机制

**现状**：`GuardContextBudget` 抛出的 `ContextOverflowException` 被 `ChatErrorHandlingMiddleware` catch 后**重新抛出**，最终传播到上层导致 Agent 崩溃。

**问题**：
- `ChatErrorHandlingMiddleware.cs:32-34` — catch(Exception) 后 break，然后第 46 行 `throw classified`
- `ClassifyException` 对 `WorkflowException` 子类原样返回（第 57-58 行）
- `ContextOverflowException` 继承 `WorkflowException` → 原样重新抛出
- 对比：`ChatUsageProcessor.cs:63-65` 中 `ContextFoldService` 抛出的同异常被 catch 后吞掉（只 log warning）
- 两个抛出点的处理方式不一致：一个吞掉继续，一个重新抛出崩溃

**影响**：Agent 直接崩溃退出，违反"Agent 永不因上下文溢出崩溃"的核心原则

**证据**：
- `QueryLoopMiddleware.cs:251` — `throw new ContextOverflowException(...)`，无 try-catch 包裹
- `ChatErrorHandlingMiddleware.cs:46` — `throw classified`（重新抛出）
- `ChatUsageProcessor.cs:63-65` — `catch (Exception ex) { _logger?.LogWarning(...); }`（吞掉）
- `ContextOverflowException.cs:8` — `IsRetryable => false`

## 决策

### 改进原则

1. **主动 > 被动** — 在 API 请求发送前主动计算+压缩，不等 API 报错
2. **精确 > 粗糙** — token 估算应考虑内容类型差异，而非简单字符数/4
3. **恢复 > 崩溃** — 任何上下文溢出情况都应有恢复路径，Agent 永不崩溃退出
4. **默认生效 > 可选配置** — 预防机制默认开启，不依赖外部配置

### 修复方案（按优先级排序）

#### P0：缝隙 5 — ContextOverflowException 恢复机制（最致命）

**方案**：在 `GuardContextBudget` 中，将"抛异常"改为"触发紧急压缩 + 降级处理"

```
GuardContextBudget 检测到比率 >= 1.0 时：
  1. 先尝试紧急折叠（SnipStaleToolResults + FoldAggressive）
  2. 折叠后仍超限 → 自动执行 ReactiveCompactService（不等 API 报错）
  3. 压缩后仍超限 → 清空最旧 N 条消息（保留系统提示 + 最近 3 条）
  4. 最终兜底：返回错误事件（ChatStreamEvent.Text）而非抛异常
```

**关键变更**：
- `GuardContextBudget` 从 `void` 改为返回 `GuardResult`（包含是否拦截、已执行的压缩操作）
- 不再抛 `ContextOverflowException`，改为 yield return 一个错误事件 + break 循环
- `ChatErrorHandlingMiddleware` 中对 `ContextOverflowException` 的处理改为：记录日志 + 返回友好消息事件（不重新抛出）

**影响面**：
- `QueryLoopMiddleware.GuardContextBudget()` — 核心变更
- `ChatErrorHandlingMiddleware.ClassifyException()` — 对 `ContextOverflowException` 特殊处理
- `ContextFoldService.cs:166` — 折叠后溢出也改为降级而非抛异常

#### P1：缝隙 2 — 降低预检拦截阈值 + 缝隙 1 — 改进 token 估算

**方案 A（阈值）**：`GuardContextBudget` 使用 `NeedsAction`（比率 > 0.95）而非 `EstimatedRatio >= 1.0`

```csharp
// 改前
if (preflight is not { EstimatedRatio: >= 1.0 }) return;

// 改后
if (preflight is not { NeedsAction: true }) {
    // 比率 > 0.7 但 < 0.95 时，主动触发预防性裁剪（不拦截请求）
    if (preflight.EstimatedRatio > 0.7) {
        await PreventiveSnipAsync(ct);
    }
    return;
}
// 比率 > 0.95 → 紧急压缩（P0 方案）
```

**方案 B（估算精度）**：引入内容类型感知的 token 估算

```
EstimateTokenCount 改进：
  - 中文内容：CharsPerToken = 1.5（而非 4）
  - 代码/JSON 内容：CharsPerToken = 2.5
  - 英文自然语言：CharsPerToken = 4
  - 混合内容：按字符范围分类加权计算
  - 消息格式开销：每条消息 +4 token（角色标记+结构包装）
  - 工具规格开销：每个工具 +10 token（JSON 结构开销）
```

**关键变更**：
- `ContextFoldDecider.EstimateTokenCount()` — 改进估算算法
- `ContextFoldThresholds.CharsPerToken` — 从单一值改为按内容类型区分
- `QueryLoopMiddleware.GuardContextBudget()` — 使用 `NeedsAction` + 预防性裁剪

**影响面**：
- `ContextFoldDecider.cs` — 估算逻辑变更
- `ContextFoldThresholds.cs` — 阈值配置变更
- `QueryLoopMiddleware.cs` — 预检逻辑变更

#### P2：缝隙 4 — 启用 HistorySnipMiddleware 默认生效

**方案**：让 `HistorySnipMiddleware` 不依赖外部 `TokenBudgetManager` 注入，改为内部直接计算剩余预算

```csharp
// 改前：依赖注入的 budgetManager
var remaining = await budgetManager.GetRemainingBudgetAsync(ct);
if (remaining < context.Config.MaxTokens * 0.1) { ... }

// 改后：直接从上下文估算
var estimatedTokens = ContextFoldDecider.EstimateTokenCount(
    context.ChatHistory, context.ToolSpecs);
var remaining = context.Config.MaxTokens - estimatedTokens;
if (remaining < context.Config.MaxTokens * 0.15) {
    var snipTarget = (int)(context.Config.MaxTokens * 0.7);
    await snipService.SnipByTokenLimitAsync(context.ChatHistory, snipTarget, ct);
}
```

**关键变更**：
- `HistorySnipMiddleware` — 不再依赖 `ITokenBudgetManager`，改为内部估算
- 触发阈值从 10% 改为 15%（更早触发）
- 裁剪目标从 70% 改为 65%（更激进裁剪）

**影响面**：
- `HistorySnipMiddleware.cs` — 核心变更
- `ITokenBudgetManager` — 不再被 HistorySnipMiddleware 依赖（其他消费方不变）

#### P3：缝隙 3 — 工具调用后主动压缩

**方案**：在 `ContentReplacementMiddleware`（query-pipeline 第 11 步）之后，增加一个"工具调用后预算检查"步骤

```
工具调用后流程：
  1. ContentReplacementMiddleware 执行（L2/L3 持久化）
  2. 新增：PostToolCallBudgetCheck
     a. 估算当前总 token（消息+工具规格）
     b. 比率 > 0.7 → 主动触发 SnipStaleToolResults
     c. 比率 > 0.85 → 主动触发 FoldNormal
     d. 比率 > 0.95 → 主动触发 FoldAggressive + ReactiveCompact
  3. 下一步迭代发送 API（此时已压缩到位）
```

**关键变更**：
- 新增 `PostToolCallBudgetCheckMiddleware` 或在现有中间件中增加预算检查逻辑
- 与 P1 的 `GuardContextBudget` 改进配合：工具调用后先压缩，API 请求前再检查

**影响面**：
- query-pipeline 中间件链 — 新增一步
- `ContextFoldService` — 需要支持"工具调用后"的折叠触发（目前只在 API 返回后触发）

### 实施顺序

| 阶段 | 内容 | 依赖 |
|------|------|------|
| 阶段 1 | P0：缝隙 5 恢复机制 — 确保 Agent 永不崩溃 | 无 |
| 阶段 2 | P1：缝隙 2 阈值 + 缝隙 1 估算 — 提高预检精度 | 阶段 1 |
| 阶段 3 | P2：缝隙 4 启用 HistorySnip — 工具调用后主动裁剪 | 阶段 2 |
| 阶段 4 | P3：缝隙 3 工具调用后压缩 — 完整预防体系 | 阶段 3 |

### 验收标准

| 验收项 | 标准 |
|--------|------|
| Agent 永不崩溃 | 模拟上下文超限场景（注入大量工具输出），Agent 不抛 `ContextOverflowException` 退出 |
| 主动压缩 | 比率 > 0.7 时自动触发裁剪/折叠，不等 API 报错 |
| Token 估算误差 | 中文内容估算误差 < 30%（当前 > 60%） |
| API 报错频率 | `prompt_too_long` 错误出现频率降低 80%+ |
| HistorySnip 默认生效 | 不注入 `TokenBudgetManager` 时裁剪仍能触发 |

## 替代方案

### 替代方案 A：使用精确 tokenizer 库

**方案**：引入 `Microsoft.ML.Tokenizers` 或 `SharpToken` 精确计算 token 数。

**优点**：估算精度极高（误差 < 5%）
**缺点**：
- NativeAOT 兼容性未验证（项目强制 AOT）
- 每个模型需要不同的 tokenizer（GPT-4 vs Claude vs DeepSeek）
- 性能开销：每次预检都要 tokenize 全部消息（可能 100ms+）
- 增加项目依赖

**放弃原因**：AOT 兼容性风险 + 模型差异管理复杂度 + 性能开销。改进估算算法（P1 方案 B）已足够将误差降到可接受范围。

### 替代方案 B：API 侧返回 token count 预检接口

**方案**：在发送正式请求前，先发一个轻量请求获取 token count（如 OpenAI 的 `count_tokens` 端点）。

**优点**：精确度最高（使用模型实际 tokenizer）
**缺点**：
- 不是所有供应商都提供此端点
- 额外网络往返延迟
- 额外 API 计费
- 供应商差异大

**放弃原因**：供应商兼容性 + 额外延迟/成本。本地估算改进已足够。

### 替代方案 C：保守截断策略 — 所有工具输出统一限制为极小值

**方案**：将所有工具的 `maxResultSizeChars` 统一限制为 5000，从源头杜绝大输出。

**优点**：简单粗暴，彻底避免上下文溢出
**缺点**：
- 大量有用信息被截断
- LLM 无法获取完整工具输出，决策质量下降
- 需要频繁重新运行工具或读取持久化文件

**放弃原因**：牺牲功能质量换取安全性，不符合项目"架构最优"原则。

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| 改进估算后预检更频繁触发，增加折叠开销 | 折叠操作本身有缓存优化（Deferred 机制），且比 API 报错重试成本低得多 |
| P0 恢复机制可能掩盖真实上下文问题 | 恢复时记录详细日志 + 诊断事件，用户可通过 `/compact` 手动检查 |
| HistorySnipMiddleware 不再依赖 TokenBudgetManager 可能影响其他消费方 | 仅改 HistorySnipMiddleware 的内部实现，ITokenBudgetManager 接口和其他消费方不变 |
| 预防性裁剪可能过度裁剪有用信息 | 裁剪策略保留最近 N 条消息 + 系统消息，且优先裁剪最大/最旧的消息 |

## 验证计划

1. **单元测试**：对每个缝隙修复编写红绿测试
   - 缝隙 5：模拟 `GuardContextBudget` 比率 >= 1.0，验证不抛异常
   - 缝隙 2：模拟比率 0.7~0.95，验证预防性裁剪触发
   - 缝隙 1：中文/代码/英文内容估算误差测试
   - 缝隙 4：不注入 `TokenBudgetManager` 时裁剪仍触发
   - 缝隙 3：工具调用后预算检查触发

2. **手动 exe 验收**（ADR 0080）：
   - 启动 jcc.exe，注入大量工具输出模拟上下文接近满
   - 验证 Agent 不崩溃、主动压缩、继续对话
   - 验证 `prompt_too_long` 错误不再出现（或大幅减少）
