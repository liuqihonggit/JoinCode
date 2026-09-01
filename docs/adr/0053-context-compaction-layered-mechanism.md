# 0053. 上下文压缩分层机制

- 状态：accepted
- 日期：2026-09-02
- 决策者：项目架构组
- 关联：[0015](docs/adr/0015-config-hotreload-dual-variable.md) | [0018](docs/adr/0018-loop-detector-state-machine.md) | [0040](docs/adr/0040-fsm-candidates.md)
- 验证：Brain 编译 0 警告 0 错误，Context.Compact + Context.Compression 测试全通过 ✅

## 背景

长对话场景下上下文窗口快速膨胀：工具输出（Shell/Read/Grep/WebFetch）、代码读取、搜索结果、多轮助手回复都会累积 token。若不加控制，轻则触发 API `prompt_too_long` 错误，重则超出模型上下文窗口导致会话中断。

需要一套压缩机制，在 token 用量接近上限时自动回收空间，同时：
- 不破坏对话轮次结构（工具调用与结果需一一对应）
- 尽量不调用 LLM（LLM 摘要昂贵且可能丢失关键细节）
- 不破坏 prompt cache（服务端缓存命中的前缀需保持稳定）
- 对压缩结果做质量守卫（防止乱码/塌缩/重复摘要污染上下文）

## 决策

采用**三大子系统 + 中间件管道 + 策略模式 + 守卫降级**的分层压缩架构，全部位于 `core/execution/Brain/src/Context/` 下。

### 1. 三大子系统分工

| 子系统 | 目录 | 职责 | 是否调 LLM |
|--------|------|------|-----------|
| **Compact**（对话级） | `Context/Compact/` | 对整条消息列表压缩，按级别递进 | 仅 FullCompact 调 LLM |
| **Compression**（内容级） | `Context/Compression/` | 对单段内容压缩，策略模式选最优 | 否（纯规则） |
| **Collapse**（折叠级） | `Context/Collapse/` | 识别可折叠段并折叠（实验性） | 否（纯规则） |

三者粒度递减：Compact 管整段对话，Compression 管单段内容，Collapse 管段内可折叠区域。

### 2. Compact 对话级压缩管道

`AutoCompactService` 是薄协调层，核心逻辑通过 `MiddlewarePipeline<CompactContext>` 执行（注册见 `composition/Pipelines/src/Registration/PipelineComposition.cs:111`）：

```
CrashSnapshot → CompactHook → CompactTelemetry → ContextCollapse
            → Microcompact → SessionMemoryCompact → ReactiveCompact → Hooks
```

中间件按顺序尝试，任一产生 `context.Result` 即短路。各中间件职责：

| 中间件 | 职责 | 产出 CompactLevel |
|--------|------|-------------------|
| CompactHook | 执行 pre-compact hook，可 Skip 跳过 | — |
| CompactTelemetry | 压缩遥测埋点 | — |
| ContextCollapse | 段折叠（实验性） | Microcompact |
| Microcompact | 时间间隔压缩 + 工具结果清理 | TimeBasedMicrocompact / Microcompact |
| SessionMemoryCompact | 用 session-memory.md 摘要替换历史 | SessionMemoryCompact |
| ReactiveCompact | API 报错后反应式丢弃最旧消息组 | ReactiveCompact |

### 3. 七级压缩级别（CompactLevel 枚举）

```csharp
public enum CompactLevel {
    None,                  // 未压缩
    Microcompact,          // 微压缩：清除旧工具结果为占位符
    TimeBasedMicrocompact, // 时间间隔微压缩：空闲>60min 触发
    SessionMemoryCompact,  // 会话记忆压缩：用记忆文件替换历史
    FullCompact,           // 全量压缩：调 LLM 生成摘要（/compact 命令）
    PartialCompact,        // 部分压缩：按 pivot 位置分割
    ReactiveCompact        // 反应式压缩：prompt_too_long 后丢弃最旧组
}
```

三种触发方式（CompactTrigger）：`Manual`（/compact 命令）、`Auto`（阈值触发）、`Reactive`（API 错误后）。

### 4. Microcompact 核心原则（对齐 TS microCompact.ts）

`MicrocompactService` 是最常用的压缩，纯规则不调 LLM：

1. **不删除消息，只替换内容** — 工具结果替换为 `"[Old tool result content cleared]"` 占位符，保留消息结构，避免破坏轮次对应
2. **保留最近 N 个** — `keepRecent=5`，只清除窗口外的旧工具结果
3. **可压缩工具按枚举自动收集** — `BuildCompactableTools()` 遍历 `ShellToolName`/`FileToolName`/`SearchToolName`/`WebToolName` 枚举，新增工具自动纳入
4. **幂等保护** — 已清除的消息（内容等于占位符常量）不再重复处理
5. **时间间隔模式** — 最后一条助手消息距今超过 60 分钟才触发，避免活跃对话误清

### 5. Compression 内容级策略模式

`ContextCompressor` 委托 `CompressionStrategyFactory` 按 `ContentType` 选策略，按 `Priority × 10 + (1 - 估算压缩比) × 100` 打分选最优：

| 策略 | 适用内容 | 压缩手段 |
|------|----------|----------|
| CodeContentCompressor | 代码 | 保留类/接口/枚举签名，删除方法体，保留关键注释，可选保留 import |
| DialogueCompressor | 对话 | 摘要旧轮次，保留最近轮次 |
| ReferenceIndexCompressor | 引用索引 | 压缩文件路径引用表 |

策略可运行时动态注册/注销（`RegisterStrategy`/`UnregisterStrategy`），支持批处理（`CompressBatchAsync` 并行）。

### 6. 守卫降级（CompactOutputGuard）

LLM 生成的摘要（FullCompact）需经 `CompactOutputGuard.Validate` 质量检查，不合格按降级链处理：

```
None → Sanitize → Microcompact → Truncate → Abort
```

检测项与降级映射：

| 检测 | FailureReason | FallbackLevel | 处理 |
|------|---------------|---------------|------|
| 乱码 | GibberishDetected | Microcompact | 放弃摘要，回退微压缩 |
| 摘要塌缩 | SummaryCollapsed | Truncate | 摘要过短/为空，回退截断 |
| 重复段落 | RepetitionDetected | Sanitize | 去重段落 |
| 格式错误 | FormatInvalid | Sanitize | 原样保留待人工处理 |
| 干预污染 | InterventionContamination | Sanitize | 剥离干预关键词行 |

### 7. API 端 vs 客户端压缩

`ApiContextManagementService`（对齐 TS apiMicrocompact.ts）通过 `context_management` 请求参数让 Anthropic API **在服务端**清理工具结果：

- **优势**：不破坏 prompt cache（服务端缓存前缀稳定）
- **触发**：环境变量 `JCC_USE_API_CLEAR_TOOL_RESULTS` / `JCC_USE_API_CLEAR_TOOL_USES`
- **阈值**：`JCC_API_MAX_INPUT_TOKENS`（默认 180000）触发，清理至 `JCC_API_TARGET_INPUT_TOKENS`（默认 40000）

客户端 `MicrocompactService` 是 API 端不可用时的兜底。优先 API 端，客户端次之。

### 8. 阈值与配置

`CompactThresholds`（`[RegisterOptions]`，支持 ADR 0015 热重载）：

| 参数 | 默认值 | 说明 |
|------|--------|------|
| AutoCompactBufferTokens | 13000 | 自动压缩缓冲区 |
| WarningBufferTokens | 20000 | 警告阈值缓冲 |
| SoftCompactRatio | 0.5 | 软压缩提示比例（窗口 50%） |
| MaxConsecutiveAutoCompactFailures | 3 | 连续失败上限，超限停止自动压缩 |
| MaxOutputTokensForSummary | 20000 | 摘要最大输出 token |

用户开关：`ConfigKey.AutoCompactEnabled`（`/config` 命令可改）。

### 9. 钩子

`HookEvent.PreCompact` / `PostCompact`（`CompactHookMiddleware` 执行）：pre-hook 可返回 `Skip` 跳过压缩，post-hook 在压缩完成后触发（如恢复文件检查点）。

### 10. 分层架构归属

| 层 | 内容 |
|----|------|
| Abstractions（foundation） | `IContextCompressor`/`ICompressionStrategy`/`IContextCollapseService`/`ContextLayer` 契约 |
| Core（core/execution/Brain） | 全部实现 |
| Composition（composition/Pipelines） | `MiddlewarePipeline<CompactContext>` 注册 |

## 替代方案

1. **单一全量 LLM 摘要**：放弃。每次压缩都调 LLM，昂贵且可能丢失关键细节（如确切错误消息、文件路径）；Microcompact 纯规则零成本，应优先。
2. **删除消息而非替换占位符**：放弃。删除会破坏工具调用与结果的轮次对应关系，导致 API 报错；占位符保留结构。
3. **不用中间件管道，if-else 链串联各压缩策略**：放弃。管道支持短路、遥测、hook 注入、异常隔离，if-else 链无法做到；且符合项目 `MiddlewarePipeline<TContext>` 统一模式。
4. **只做客户端压缩，不用 API 端 context_management**：放弃。API 端不破坏 prompt cache，命中率更高；客户端是兜底。
5. **压缩策略硬编码不抽工厂**：放弃。策略模式支持运行时注册新策略、按内容类型自动选优、批处理并行，硬编码无法扩展。
6. **不设守卫，信任 LLM 摘要**：放弃。LLM 可能生成乱码/塌缩/重复摘要，污染后续上下文导致对话质量劣化；守卫是质量底线。

## 后果

- 正面：长对话不中断；纯规则压缩零 LLM 成本；多层降级保证压缩质量；API 端压缩保 prompt cache；策略可扩展
- 负面：三子系统 + 管道 + 策略 + 守卫认知负担较重；Microcompact 占位符丢失旧工具结果细节（需用户主动 /compact 全量摘要恢复）
- 中性：Collapse 为实验性功能默认不启用；阈值通过 `CompactThresholds` 配置可调；对齐 TS 端 microCompact.ts/apiMicrocompact.ts 保持行为一致
