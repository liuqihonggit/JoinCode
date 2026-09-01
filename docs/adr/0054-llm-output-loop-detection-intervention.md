# 0054. LLM 输出循环检测与分级干预机制

- 状态：accepted
- 日期：2026-09-02
- 决策者：项目架构组
- 关联：[0018](docs/adr/0018-loop-detector-state-machine.md) | [0038](docs/adr/0038-state-machine-flags-guard.md) | [0040](docs/adr/0040-enterprise-fsm-framework.md) | [0053](docs/adr/0053-context-compaction-layered-mechanism.md)
- 验证：Brain 编译 0 警告 0 错误，Loop 检测器 + LoopInterventionMiddleware 测试全通过 ✅

## 背景

LLM 在长对话中可能陷入**输出死循环**：重复输出相同文本片段、重复调用相同工具相同参数、或输出越来越重复（字符分布趋于集中）。原因包括：温度过低、上下文误导、工具结果反馈循环等。

若不干预，LLM 会持续消耗 token 直到触发 `prompt_too_long` 或超出上下文窗口，且无人值守场景下会话永远无法恢复。

需要一套机制：
- 在**流式输出过程中**实时检测循环（不是事后分析）
- 检测成本可控（不能每 token 都跑昂贵检测）
- 误报可控（正常重复不算死循环）
- 分级干预（从轻提示到重截断到压缩重置，逐步升级）
- 无人值守可自愈（重连失败能压缩/重置恢复）

## 决策

采用**四层漏斗式检测 + 三级分级干预 + 重连压缩兜底**的架构，全部位于 `core/execution/Brain/src/Context/` 下。

### 1. 检测层 — InformationEntropyGuardian（串行漏斗）

`InformationEntropyGuardian` 实现 `IOutputLoopDetector` + `ILoopDetectionStrategy`，串行编排四个检测器，**按成本从低到高运行，任一触发即返回**（不跑后续更昂贵的检测器）：

| Layer | 检测器 | 原理 | 默认阈值 | 成本 |
|-------|--------|------|----------|------|
| 1 | `OutputLoopDetector` | 尾部子串重复：从最长模式往最短扫，找到重复模式 | 窗口2000，模式10~500字符，重复≥10次，检查间隔50字符 | 最廉价 |
| 2 | `LogicFingerprintDetector` | 逻辑指纹循环：前缀+后缀hash，滑动窗口命中 | 前缀/后缀各200字符，窗口5，命中≥4次 | 中等 |
| 3 | `ToolCallSequenceDetector` | 工具调用序列循环：工具名+参数指纹重复 | 窗口6，模式≥3，重复≥4次 | 中等 |
| 4 | `ShannonEntropyDetector` | Shannon信息熵持续下降：字符分布趋于集中 | 窗口10，连续4轮熵递减（差≥0.05），5s二次确认 | 最昂贵 |

**漏斗设计**：`Detect(accumulatedText)` 跑 Layer1→Layer2；`CheckTextLoop(text)` 跑 Layer1→Layer2→Layer4；`CheckToolCallLoop` 跑 Layer3。ShannonEntropy 不参与 `Detect`（累积文本不断增长，熵趋势无意义），只在 `CheckTextLoop` 按轮次检测。

**性能门控**：`OutputLoopDetector` 有检查间隔（每50字符才检测一次）和冷却期（触发后500字符内不重复触发）；`StringBuilder` 重载延迟 `ToString()` 直到通过门控，避免每 token O(n) 拷贝。

### 2. ShannonEntropy 状态机（ADR 0040 企业级状态机）

`ShannonEntropyDetector` 用 `[FsmStateMachine]` + `[Transition]` 特性声明状态机：

```
Monitoring →(连续4轮熵递减)→ Suspected →(5s窗口内再次触发)→ Confirmed
Suspected →(窗口超时)→ Monitoring      （误报消除）
Confirmed →(熵恢复)→ Monitoring         （自愈）
Confirmed →(继续熵减)→ Confirmed        （自循环）
```

- **时间窗口二次确认（去抖）**：第一次触发不立即干预，进入 `Suspected` 等5s内二次确认，超时复位消除误报
- **时钟注入**：`Func<DateTimeOffset>? clock = null`，测试可控、生产用 `DateTimeOffset.UtcNow`
- 状态/事件/上下文均为强类型（`EntropyDetectionState`/`EntropyEvent`/`EntropyFsmContext`）

### 3. 干预层 — LoopInterventionMiddleware（三级漏斗）

`LoopInterventionMiddleware` 拦截 `LoopDetected` 事件，按**有效触发次数**分级（`ClassifyIntervention`）：

| 级别 | 触发次数 | 干预手段 | 是否中断流 |
|------|----------|----------|-----------|
| **Level 1 Soft** | 1~2 | 注入提示词"请用序号→箭头方式总结当前回答再继续推理" | 否，流继续 |
| **Level 2 Hard** | 3~4 | 撤回循环轮次 + 降温度(0.6) + 重连LLM | 是，重连 |
| **Level 3 Compact** | 5+ | 上下文压缩（FoldAggressive）或重置 | 是，压缩 |

**任务推进折扣**：若 `ITaskProgressTracker` 检测到任务有推进（TODO完成数增加），有效触发次数 = 实际触发次数 - `ProgressDiscount`(1)，漏斗降一级。避免任务正常推进时被误判为死循环。

### 4. 重连机制（Level 2）

```
1. RewindLastTurnAsync — 撤回上一轮消息
2. 插入审计标记 "[系统撤回: 原因=循环检测, 移除消息数=N]"
3. 降温度重新发起 LLM 调用（RetryTemperature=0.6）
4. 最后一次用更低温度 SecondChanceTemperature=0.3（给模型最后一次低温机会）
5. 重连成功 → 继续对话
6. 重连全部失败(MaxRetryAttempts=2) → 升级 Level 3
```

重连时仍检测循环：若重连后仍 `LoopDetected`，本次重试失败，进入下一次重试。

### 5. 压缩兜底（Level 3）

```
1. FoldIfNeededAsync(FoldAggressive) — 上下文压缩（委托 ADR 0053 Compact 机制）
2. 压缩成功 → 继续对话
3. 压缩失败 → RewindToStartAsync 重置到起点
4. PreserveLastUserMessageOnReset=true → 保留最近1轮用户消息作为种子，避免完全丢失用户需求
```

### 6. 配置 — LoopInterventionOptions（`[RegisterOptions]`，支持热重载）

| 参数 | 默认 | 用途 |
|------|------|------|
| HardTruncateThreshold | 3 | Level 2 触发阈值 |
| CompactThreshold | 5 | Level 3 触发阈值 |
| MaxRetryAttempts | 2 | Level 2 重连次数 |
| RetryTemperature | 0.6 | 重连温度 |
| SecondChanceTemperature | 0.3 | 最后一次重连低温 |
| ProgressDiscount | 1 | 任务推进折扣 |
| InsertRewindAuditMark | true | 撤回审计标记 |
| PreserveLastUserMessageOnReset | true | 重置保留种子 |
| MaxConsecutiveEmptyResponse | 5 | 空响应上限 |

子配置类：`ShannonEntropyConfig` / `OutputLoopConfig` / `LogicFingerprintConfig` / `ToolCallSequenceConfig`，各检测器参数集中管理。

### 7. 诊断 — LoopDiagnosticJournal

记录追踪链供医生模式回溯：`guardian_detect` / `guardian_check_text` / `guardian_check_tool` / `OnLoopDetected`（含检测器名、触发次数、熵值、文本片段）。

### 8. 管道集成

- `LoopInterventionMiddleware` 注册在 Chat 管道（`PipelineComposition.cs:63`）
- `QueryLoopMiddleware` 在查询层使用 `ILoopDetectionStrategy` 检测，触发 `LoopDetected` 事件交由干预中间件处理

### 9. 与 ADR 0053 Compact 守卫的区别

| 维度 | 本 ADR（Loop 检测+干预） | ADR 0053（Compact 守卫） |
|------|--------------------------|--------------------------|
| 目标 | 检测 LLM **运行时输出**循环，流式干预 | 验证 LLM 生成的**摘要**质量，压缩输出守卫 |
| 时机 | 事中（流式过程中） | 事后（压缩完成后） |
| 检测器 | OutputLoop/LogicFingerprint/ToolCallSequence/ShannonEntropy | Gibberish/SummaryRepetition/SummaryCollapse |
| 干预 | 三级漏斗（提示→重连→压缩） | 五级降级（Sanitize→Microcompact→Truncate→Abort） |
| 位置 | `Context/Services/Loop/` + `LoopInterventionMiddleware` | `Context/Compact/Guard/` |

两者互补：本 ADR 防止 LLM 陷入死循环，ADR 0053 保证压缩摘要质量。

## 替代方案

1. **单层检测（只用 OutputLoopDetector）**：放弃。文本重复检测无法覆盖工具调用循环和熵减循环，三种循环模式表现不同，单层漏报率高。
2. **并行跑四个检测器取多数表决**：放弃。ShannonEntropy 最昂贵，并行会拖慢流式输出；串行漏斗让廉价检测器先跑，多数循环在 Layer1/2 就能检出，平均成本最低。
3. **立即干预不二次确认**：放弃。ShannonEntropy 误报高（正常重复如代码缩进也会熵减），5s 窗口二次确认消除误报，避免正常对话被误截断。
4. **固定温度重连，不降温**：放弃。相同温度下 LLM 倾向重复同一思路，降温(0.6→0.3)才能打破循环。
5. **Level 3 直接重置不保留种子**：放弃。完全重置会丢失用户需求，无人值守场景下 LLM 不知道该做什么；保留最近1轮用户消息作为种子可自愈。
6. **不用状态机，if-else 管理检测器状态**：放弃。见 ADR 0018/0038/0040，状态机显式转换、可测试、可扩展。
7. **不设任务推进折扣**：放弃。LLM 在长任务中会重复相同模式（如反复读文件），但任务在推进（TODO 完成），不折扣会误判为死循环并截断正常工作。

## 后果

- 正面：LLM 死循环实时检测+自愈；漏斗式检测平均成本最低；三级干预逐步升级避免过度反应；任务推进折扣防误判；无人值守可恢复
- 负面：四检测器+状态机+干预中间件认知负担较重；ShannonEntropy 计算有开销（虽漏斗设计使其只在 Layer1/2 未触发时才跑）
- 中性：阈值通过 `LoopInterventionOptions` 可调；状态机风格遵循 ADR 0038/0040；与 ADR 0053 Compact 机制互补，Level 3 压缩委托 Compact 管道执行
