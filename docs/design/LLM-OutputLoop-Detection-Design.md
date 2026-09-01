# LLM 输出循环检测与干预实现设计

> **为什么**这样设计见 [ADR 0054](../adr/0054-llm-output-loop-detection-intervention.md)。本文聚焦**怎么**实现。

## 1. 目录结构

```
core/execution/Brain/src/Context/
├── Services/
│   ├── Loop/                           # 检测器
│   │   ├── IOutputLoopDetector.cs      #   检测器接口
│   │   ├── OutputLoopDetector.cs       #   Layer1: 尾部子串重复
│   │   ├── ShannonEntropyDetector.cs   #   Layer4: 信息熵减（状态机）
│   │   ├── InformationEntropyGuardian.cs #  串行漏斗编排器
│   │   ├── LoopDetectionResult.cs      #   检测结果
│   │   ├── LoopDiagnosticJournal.cs    #   诊断日志
│   │   ├── EmptyResponseTracker.cs     #   空响应追踪
│   │   ├── StreamTokenDetector.cs      #   流 token 检测
│   │   └── ReasoningRound.cs           #   推理轮次
│   ├── LogicFingerprintDetector.cs     # Layer2: 逻辑指纹
│   ├── ToolCallSequenceDetector.cs     # Layer3: 工具调用序列
│   ├── LoopInterventionMiddleware.cs   # 干预中间件（三级漏斗）
│   ├── LoopInterventionOptions.cs      # 配置 + 子配置类
│   └── Chat/
│       ├── LoopDetectionStrategy.cs    # ILoopDetectionStrategy 接口
│       └── QueryLoopMiddleware.cs      # 查询层循环检测
```

## 2. 检测器实现

### 2.1 OutputLoopDetector（Layer1，最廉价）

尾部子串重复检测，`Detect(accumulatedText)`：

```
1. 长度 < minPatternLength × requiredRepeats → NoLoop
2. 距上次检查 < checkInterval(50字符) → NoLoop（性能门控）
3. 冷却期内（触发后500字符内）→ NoLoop
4. tailLen = min(len, windowSize=2000)
5. 从 maxPatternLength(500) 到 minPatternLength(10) 逐长度扫描：
   a. pattern = 尾部 patternLen 个字符
   b. 往前数连续重复次数
   c. repeatCount >= 10 → 触发，进入冷却期，返回 LoopDetectionResult
```

`StringBuilder` 重载延迟 `ToString()` 直到通过门控，避免每 token O(n) 拷贝。

### 2.2 LogicFingerprintDetector（Layer2，中等）

前缀+后缀hash循环检测，`Record(text)`：
- 指纹 = `text[..200]` + `text[^200..]`（前缀200 + 后缀200字符）
- 滑动窗口5轮，相同指纹命中 ≥4 次 → 触发

### 2.3 ToolCallSequenceDetector（Layer3，中等）

工具调用序列循环检测，`Record(toolName, argsFingerprint)`：
- 参数指纹：`toolName(key1=val1,key2=val2)`，取 file_path/path/pattern/query/command 等关键参数
- 滑动窗口6，模式 ≥3，重复 ≥4 次 → 触发

### 2.4 ShannonEntropyDetector（Layer4，最昂贵，状态机）

Shannon 信息熵：`H = -Σ(p_i × log2(p_i))`，字符分布越集中熵越低。

状态机转换（`[FsmStateMachine]` + `[Transition]` 特性声明）：

| 当前状态 | 事件 | 目标状态 | 动作 |
|----------|------|----------|------|
| Monitoring | Decline | Suspected | 记录 FirstTriggerTime |
| Suspected | Confirm | Confirmed | TriggerCount++ |
| Suspected | Timeout | Monitoring | 清除 FirstTriggerTime |
| Confirmed | Decline | Confirmed | TriggerCount++ |
| Confirmed | Recover | Monitoring | 清除 FirstTriggerTime |

事件选择逻辑（`SelectEvent`）：
- Monitoring：连续 `declineThreshold(4)` 轮熵递减 → Decline
- Suspected：5s 窗口内再次熵减 → Confirm；窗口超时 → Timeout
- Confirmed：继续熵减 → Decline；熵恢复 → Recover

`Record(text)` 流程：计算熵 → 加入 RingBuffer 历史 → 数连续下降轮 → 选事件 → 驱动状态机 → 返回 `ShannonEntropyResult(State, IsLoopDetected=Confirmed, ...)`。

## 3. InformationEntropyGuardian 串行漏斗编排

### 3.1 Detect(accumulatedText) — 流式累积文本

```
OutputLoop.Detect → 触发则返回
LogicFingerprint.Record → 触发则返回
（ShannonEntropy 不参与，累积文本熵趋势无意义）
```

### 3.2 CheckTextLoop(text) — 按轮次文本

```
OutputLoop.Detect → 触发则返回
LogicFingerprint.Record → 触发则返回
ShannonEntropy.Record → 触发则返回
```

### 3.3 CheckToolCallLoop(toolName, args) — 工具调用

```
ToolCallSequence.Record(toolName, argsFingerprint) → 触发则返回
```

所有检测触发时通过 `LoopDiagnosticJournal.OnLoopDetected` 记录追踪链。

## 4. LoopInterventionMiddleware 三级干预

### 4.1 主流程

```
await foreach (evt in next(context, ct)):
  if evt.Type == LoopDetected:
    hasProgressed = CheckTaskProgressAsync()  # TODO 完成数是否增加
    effectiveCount = hasProgressed ? triggerCount - ProgressDiscount : triggerCount
    (level, prompt, shouldBreak) = ClassifyIntervention(effectiveCount)
    yield Text(prompt)
    if shouldBreak: break
  else:
    yield evt

if !hasLoopDetected || effectiveCount < HardTruncateThreshold: return
if effectiveCount >= CompactThreshold: CompactAsync(); return

# Level 2 重连
for attempt in 0..MaxRetryAttempts:
  RewindLastTurnAsync()
  插入审计标记
  temperature = 最后一次 ? 0.3 : 0.6
  重新发起 LLM 流式调用
  if 重连后无循环: 成功返回
if 全部失败: CompactAsync()  # 升级 Level 3
```

### 4.2 ClassifyIntervention 决策

```csharp
if (effectiveTriggerCount >= CompactThreshold=5)     → (Compact, HardTruncatePrompt, break)
if (effectiveTriggerCount >= HardTruncateThreshold=3) → (Hard, HardTruncatePrompt, break)
else                                                  → (Soft, SoftIntervenePrompt, continue)
```

### 4.3 CompactAsync（Level 3）

```
1. PreserveLastUserMessageOnReset → 提取最近1轮用户消息
2. FoldIfNeededAsync(FoldAggressive) → 委托 ADR 0053 Compact 机制
3. 折叠成功 → yield CompactSuccessPrompt
4. 折叠失败 → RewindToStartAsync + 保留用户消息作为种子 + yield CompactFallbackPrompt
```

## 5. 配置参数（LoopInterventionOptions）

### 5.1 干预阈值

| 参数 | 默认 | 用途 |
|------|------|------|
| HardTruncateThreshold | 3 | Level 2 触发 |
| CompactThreshold | 5 | Level 3 触发 |
| MaxRetryAttempts | 2 | Level 2 重连次数 |
| RetryTemperature | 0.6 | 重连温度 |
| SecondChanceTemperature | 0.3 | 最后一次低温 |
| ProgressDiscount | 1 | 任务推进折扣 |
| MaxConsecutiveEmptyResponse | 5 | 空响应上限 |

### 5.2 检测器子配置

**ShannonEntropyConfig**：WindowSize=10, DeclineThreshold=4, MinEntropyDelta=0.05, ConfirmationWindow=5s

**OutputLoopConfig**：WindowSize=2000, MinPatternLength=10, MaxPatternLength=500, RequiredRepeats=10, CheckInterval=50, CooldownChars=500

**LogicFingerprintConfig**：FingerprintPrefixLen=200, FingerprintSuffixLen=200, WindowSize=5, HitThreshold=4

**ToolCallSequenceConfig**：WindowSize=6, MinPatternLength=3, RequiredRepeats=4

## 6. 管道集成

- `LoopInterventionMiddleware` 注册在 Chat 管道（`PipelineComposition.cs:63`）
- `QueryLoopMiddleware` 在查询层用 `ILoopDetectionStrategy` 检测，触发 `LoopDetected` 事件
- 事件流：`QueryLoopMiddleware` 检测 → 发 `LoopDetected` 事件 → `LoopInterventionMiddleware` 拦截 → 分级干预

## 7. 诊断日志（LoopDiagnosticJournal）

记录点：
- `guardian_detect` — Detect 调用入口
- `guardian_check_text` — CheckTextLoop 调用入口
- `guardian_check_tool` — CheckToolCallLoop 调用入口
- `OnLoopDetected` — 检测触发（含检测器名、触发次数、熵值、文本片段）

供 `DiagnosticEngine` 医生模式回溯分析。

## 8. 与 Compact 守卫的协作

| 场景 | 使用的机制 |
|------|-----------|
| LLM 流式输出重复 | 本机制（Loop 检测 + 干预） |
| LLM 生成的摘要质量差 | ADR 0053 Compact 守卫（Gibberish/Repetition/Collapse） |
| Level 3 干预需压缩上下文 | 本机制委托 ADR 0053 Compact 管道执行 |

两者通过 `FoldIfNeededAsync` 衔接：本机制 Level 3 调用 Compact 管道的折叠功能。
