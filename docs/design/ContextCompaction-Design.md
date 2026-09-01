# 上下文压缩机制实现设计

> **为什么**这样设计见 [ADR 0053](../adr/0053-context-compaction-layered-mechanism.md)。本文聚焦**怎么**实现。

## 1. 目录结构

```
core/execution/Brain/src/Context/
├── Compact/                          # 对话级压缩（中间件管道）
│   ├── Core/                         #   契约：CompactThresholds / CompactResult / CompactLevel
│   ├── Guard/                        #   守卫：CompactOutputGuard + 乱码/塌缩/重复检测器
│   └── Services/
│       ├── Middleware/               #   管道中间件（7 个）
│       ├── MicrocompactService.cs    #   微压缩核心（对齐 TS microCompact.ts）
│       ├── AutoCompactService.cs     #   自动压缩协调层
│       ├── ReactiveCompactService.cs #   反应式压缩（prompt_too_long 后）
│       ├── SessionMemoryCompactService.cs # 会话记忆压缩
│       └── ApiContextManagementService.cs # API 端 context_management
├── Compression/                      # 内容级压缩（策略模式）
│   ├── Core/                         #   契约：ICompressionStrategy / ICompressionStrategyFactory
│   └── Strategies/
│       ├── ContextCompressor.cs      #   基础压缩器
│       ├── CompressionStrategyFactory.cs # 策略工厂
│       ├── CodeContentCompressor.cs  #   代码策略
│       ├── DialogueCompressor.cs     #   对话策略
│       └── ReferenceIndexCompressor.cs # 引用索引策略
├── Collapse/                         # 折叠级压缩（实验性）
│   └── ContextCollapseService.cs
└── Core/Hierarchy/                   # 分层上下文（ContextLayer.Compress）
```

## 2. Compact 管道执行流程

### 2.1 管道注册（`composition/Pipelines/src/Registration/PipelineComposition.cs:111`）

```
MiddlewarePipeline<CompactContext>:
  CrashSnapshot → CompactHook → CompactTelemetry → ContextCollapse
               → Microcompact → SessionMemoryCompact → ReactiveCompact → Hooks
```

### 2.2 短路机制

`CompactContext.IsHandled => Result is not null`（`CompactContext.cs:18`）。中间件产生 `context.Result` 后 `return`（不调 `next`），后续中间件跳过。

### 2.3 各中间件触发条件

| 中间件 | 触发条件 | 短路条件 |
|--------|----------|----------|
| CompactHook | 始终执行 pre-hook | hook 返回 `Skip` 时短路 |
| ContextCollapse | `IContextCollapseService` 已注入 | 折叠后 token 减少才设 Result |
| Microcompact | 始终尝试 | 时间间隔压缩或普通微压缩成功时短路 |
| SessionMemoryCompact | session-memory.md 非空 | 摘要后 token < autoCompactThreshold 才短路 |
| ReactiveCompact | `Trigger == Reactive` | 压缩成功时短路 |

## 3. Microcompact 核心算法（`MicrocompactService.cs`）

### 3.1 普通微压缩 `CompactMessages`

```
1. CollectCompactableToolIds(messages) → 按出现顺序收集可压缩工具调用 ID
2. keepSet = 最后 keepRecent(5) 个 ID
3. clearSet = 其余 ID
4. ClearToolResults: 遍历 Tool 角色消息，clearSet 中的内容 → "[Old tool result content cleared]"
```

**不删除消息，只替换内容**，保留轮次对应关系。幂等：已清除的消息（内容等于占位符常量）跳过。

### 3.2 时间间隔微压缩 `TimeBasedCompact`

```
1. 取最后一条 Assistant 消息的时间戳
2. gapMinutes = now - lastTimestamp
3. gapMinutes < 60 → 返回 null（不触发）
4. 满足时间条件 → 执行与普通微压缩相同的清除逻辑
```

### 3.3 可压缩工具自动收集

`BuildCompactableTools()` 遍历枚举，新增工具枚举值自动纳入：
- `ShellToolName` 全部值（Bash/PowerShell 等）
- `FileToolName`：FileRead/FileWrite/FileEdit
- `SearchToolName`：Grep/Glob
- `WebToolName`：WebSearch/WebFetch

### 3.4 Token 估算

`EstimateMessageTokens`：文本 `字符数/4`，图片/文档固定 2000，工具调用 `name+input`，最终 `× 4/3` 修正系数。

## 4. Compression 策略选优（`CompressionStrategyFactory.cs`）

### 4.1 选优算法

```csharp
score = strategy.Priority * 10 + (1 - estimatedRatio) * 100
```

按 `ContentType` 筛选兼容策略 → 按分数降序 → 取最高分。

### 4.2 三种策略

| 策略 | 保留 | 删除 |
|------|------|------|
| CodeContentCompressor | 类/接口/枚举签名、关键注释、可选 import | 方法体、普通注释 |
| DialogueCompressor | 最近轮次 | 旧轮次摘要 |
| ReferenceIndexCompressor | 引用键 | 冗余路径 |

策略可运行时 `RegisterStrategy`/`UnregisterStrategy`，支持 `CompressBatchAsync` 并行。

## 5. 守卫降级链（`CompactOutputGuard.cs`）

```
Validate(summary, originalChars):
  1. 空摘要        → SummaryCollapsed  → Truncate
  2. GibberishDetector → GibberishDetected → Microcompact
  3. SummaryCollapseDetector → SummaryCollapsed → Truncate
  4. SummaryRepetitionDetector → RepetitionDetected → Sanitize（去重段落）
  5. SummaryFormatValidator → InterventionContamination → Sanitize（剥离关键词行）
  6. FormatError → FormatInvalid → Sanitize
  7. 全部通过 → IsValid=true
```

降级级别枚举：`None(0) < Sanitize(1) < Microcompact(2) < Truncate(3) < Abort(4)`。

## 6. 阈值配置（`CompactThresholds.cs`）

| 参数 | 默认 | 用途 |
|------|------|------|
| AutoCompactBufferTokens | 13000 | 自动压缩缓冲 |
| WarningBufferTokens | 20000 | 警告阈值缓冲 |
| ErrorBufferTokens | 20000 | 错误阈值缓冲 |
| ManualCompactBufferTokens | 3000 | 手动压缩缓冲 |
| MaxConsecutiveAutoCompactFailures | 3 | 连续失败上限 |
| MaxOutputTokensForSummary | 20000 | 摘要输出上限 |
| SoftCompactRatio | 0.5 | 软提示比例 |
| PostCompactTokenBudget | 50000 | 压缩后恢复文件 token 预算 |
| PostCompactMaxFilesToRestore | 5 | 压缩后恢复文件数 |

`[RegisterOptions]` 标记，支持 ADR 0015 热重载。用户开关 `ConfigKey.AutoCompactEnabled`。

## 7. 触发流程

### 7.1 自动压缩（Auto）

```
ShouldAutoCompact(currentTokens, window):
  consecutiveFailures >= 3 → false（熔断）
  threshold = window - MaxOutputTokensForSummary - AutoCompactBufferTokens
  return currentTokens >= threshold
```

`CalculateWarningState` 返回多级状态：软提示 / 警告 / 错误 / 阻塞。

### 7.2 反应式压缩（Reactive）

```
RunReactiveCompactAsync(messages, errorMessage):
  1. IsPromptTooLongError(errorMessage) → 否则返回未压缩
  2. GetPromptTooLongTokenGap(errorMessage) → 解析超出的 token 数
  3. GroupMessagesByApiRound(messages) → 按 API 轮次分组
  4. CalculateDropCount → 按 tokenGap 累加丢弃最旧的组
  5. BuildDroppedGroupsSummary → 结构化占位摘要（不调 LLM）
```

### 7.3 API 端压缩（`ApiContextManagementService.cs`）

通过 `context_management` 请求参数让 Anthropic 服务端清理，不破坏 prompt cache：

- 触发：环境变量 `JCC_USE_API_CLEAR_TOOL_RESULTS` / `JCC_USE_API_CLEAR_TOOL_USES`
- 阈值：`JCC_API_MAX_INPUT_TOKENS`（默认 180000）→ 清理至 `JCC_API_TARGET_INPUT_TOKENS`（默认 40000）
- thinking 策略：`ClearThinkingStrategy`，`ClearAllThinking` 时仅保留最近 1 个 thinking turn

## 8. 钩子

`HookEvent.PreCompact` / `PostCompact`（`CompactHookMiddleware`）：
- pre-hook：可返回 `Skip` 跳过整次压缩
- post-hook：压缩完成后触发（如恢复文件检查点）

## 9. 与 TS 端对齐

| C# 服务 | TS 对应文件 |
|---------|------------|
| MicrocompactService | microCompact.ts |
| ApiContextManagementService | apiMicrocompact.ts |
| ContextManagementConfig | apiMicrocompact.ts ContextManagementConfig |

行为保持一致，占位符常量 `ContentReplacementConstants.ToolResultClearedMessage` 对齐 TS `TIME_BASED_MC_CLEARED_MESSAGE`。
