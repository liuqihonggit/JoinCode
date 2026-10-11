# TASK042 — Agent 工具设计缺口：提示词与循环跳出

> 来源：《Agent 工具设计》文档"提示词"整节
> 检索日期：2026-10-09

## 背景

设计要求：冷却期动态注入（非每次注入）；跳出循环三轮策略（子代理调查→验证记忆→没收工具冷却期）；双击 ESC 清理计数；选择性忽略用子代理结论倒装结构 + 低频驱动。

## 已落地部分

| 设计点 | 落地位置 |
|--------|----------|
| 冷却期核心（CooldownService，默认 5 分钟） | `abs_core/core_utils/core/misc/CooldownService.cs` |
| 动态关键词注入 + 冷却期 | `KeywordInjectionMiddleware.cs:50-56,102-107` |
| 系统提醒管理器 | `kit/prompts/services/SystemReminder.cs` |
| 每轮重注入 CriticalSystemReminder | `AgentBase.cs:245,384` |
| 工具空闲提醒（冷却期 2 分钟） | `ReminderInjectionMiddleware.cs:29-30` |
| Handoff 分类器冷却期 | `HandoffClassifier.cs:72-74` |
| 循环检测四层漏斗（OutputLoop→LogicFingerprint→ToolCallSequence→ShannonEntropy） | `InformationEntropyGuardian.cs` |
| OutputLoopDetector（含 cooldownChars） | `OutputLoopDetector.cs` |
| ShannonEntropyDetector | `ShannonEntropyDetector.cs` |
| 三级干预中间件（软干预→硬截断→上下文压缩） | `LoopInterventionMiddleware.cs` |
| 折叠卡死守卫（IsFoldStuck） | `ContextFoldDecider.cs:200` |
| 子代理活跃度扫描 + 卡死检测 | `SubAgentLivenessScanner.cs` |
| Stuck 技能 | `kit/hands/skills/built_in/StuckSkill.cs` |
| /clear 重置（含循环检测+工具空闲） | `ClearCommand.cs:128-134` |
| FastModeService 冷却期 | `FastModeService.cs` |
| CompactPromptTemplate 尾缀倒装（NoToolsTrailer） | `CompactPromptTemplate.cs:19-23` |
| 子代理输出三层截断（L1原样/L2摘要/L3落盘指针） | `SubAgentOutputTruncator.cs`、`SubAgentSummaryGenerator.cs` |
| Fork 消息约束 | `ForkMessageBuilder.cs:39` |
| Handoff 安全审查 | `AgentHandoffMiddleware.cs` |
| 诱导式错误提示（防绕过） | `GitHubToolHandlers.Api.cs:109-110` |

## 缺口清单

### GAP-042-01 单工具频率限制/没收冷却期 ⭐ P1

- **当前状态**：未落地（有 `UsdBudgetManager`/`TokenBudget`/`RateLimitTracker` API 速率限制，但无针对"单个工具高频率使用"的额度限制）
- **缺什么**：设计要求"针对 bash 高频率使用某种工具时...没收工具使用冷却期"——第三轮干预手段缺失
- **建议方案**：
  1. 新增 `ToolQuotaService`：按工具名+时间窗口计次，超阈值触发冷却期
  2. 阈值可配（如 Bash 1 分钟内 20 次触发 5 分钟冷却）
  3. 冷却期内该工具调用直接拒绝 + 注入"工具 X 已冷却，建议用专用工具 Y"提示
  4. 集成到 `LoopInterventionMiddleware` 第三级：连续触发循环时没收高频工具
- **验收标准**：
  - Bash 1 分钟内调用 20 次后进入冷却期
  - 冷却期内调用被拒绝 + 替代工具提示
  - /clear 重置配额
- **复杂度**：中

### GAP-042-02 子代理结论倒装结构 ⭐ P1

- **当前状态**：部分落地（仅 `CompactPromptTemplate.NoToolsTrailer` 一处倒装实例，用于上下文压缩；子代理报告未应用倒装）
- **缺什么**：设计要求"子代理返回结论约束一个结构，通过重新组装实现结尾提示词要倒装结构"——`SubAgentSummaryGenerator` 只是简单摘要，无倒装
- **建议方案**：
  1. `SubAgentSummaryGenerator` 生成摘要后，尾部追加倒装约束（关键结论重复 + 下一步提示）
  2. 结构：`[摘要正文] + [关键结论倒装] + [下一步行动]`
  3. 倒装内容：把摘要开头的关键结论重复到尾部，防止 AI 选择性忽略尾部
- **验收标准**：子代理返回的摘要尾部含关键结论重复 + 下一步提示
- **复杂度**：低

### GAP-042-03 双击 ESC 键绑定 ⭐ P2

- **当前状态**：未落地（`/clear` 命令覆盖功能，但无"双击 ESC"专门键绑定）
- **缺什么**：设计要求"双击 ESC 清理掉计数"——键绑定缺失
- **建议方案**：
  1. GUI 端（Avalonia）监听 ESC 键，500ms 内双击触发 `/clear`
  2. CLI 端无 ESC 键绑定，保持 `/clear` 命令
  3. 双击 ESC 后提示"已重置工具额度/循环检测/缓存"
- **验收标准**：GUI 双击 ESC 触发 /clear；提示确认
- **复杂度**：低

### GAP-042-04 任务重排自动机制 ⭐ P2

- **当前状态**：未落地（有 `ReorderStepsAsync` 手动重排、`ReorderSubCommandToFront` 子命令重排，无"任务重排避免单点问题干扰整体"自动机制）
- **缺什么**：设计要求"任务重排避免单点问题干扰整体项目推进，除非它是鱼骨图重要节点"
- **建议方案**：
  1. 检测某任务连续失败 N 次（单点问题）
  2. 自动把该任务移到队列尾部，前置无依赖任务
  3. 鱼骨图重要节点（标记 `IsCriticalNode`）不重排
  4. 提示用户"任务 X 因连续失败已后置，先推进任务 Y"
- **验收标准**：连续失败任务自动后置；非关键节点；提示用户
- **复杂度**：中

### GAP-042-05 注意力涣散专门检测 ⭐ P2

- **当前状态**：未落地（仅循环检测间接覆盖，无专门"注意力涣散"状态检测）
- **缺什么**：设计要求"上下文太多时经历数小时运行多次上下文压缩摘要，会产生注意力涣散状态"——专门检测缺失
- **建议方案**：
  1. 新增 `AttentionFatigueDetector`：监测上下文压缩次数 + 运行时长 + 错误决策率
  2. 压缩次数>3 且运行时长>2 小时且错误率上升 → 触发注意力涣散状态
  3. 触发后注入"建议用 /clear 重置上下文或开启新会话"提示
- **验收标准**：长时间运行+多次压缩后触发注意力涣散提示
- **复杂度**：中

### GAP-042-06 低频驱动机制 ⭐ P3

- **当前状态**：未落地（`AutoDreamConfig.UseLowFrequencyMode` 是 Dream 功能扫描频率，非"Agent 运行时低频驱动诱导 AI"）
- **缺什么**：设计要求"Agent 作为运行时，只能通过低频驱动的方式诱导 AI"
- **建议方案**：
  1. 用 `CooldownService` 已有基建，定义低频诱导事件（如每 10 分钟检查一次工具使用健康度）
  2. 健康度下降时注入一次性诱导提示
  3. 避免高频注入造成噪声
- **验收标准**：低频诱导事件按配置间隔触发；不高频注入
- **复杂度**：中

## 优先级汇总

| 优先级 | 缺口 | 复杂度 |
|--------|------|--------|
| P1 | GAP-042-01 单工具频率限制/没收冷却期 | 中 |
| P1 | GAP-042-02 子代理结论倒装结构 | 低 |
| P2 | GAP-042-03 双击 ESC 键绑定 | 低 |
| P2 | GAP-042-04 任务重排自动机制 | 中 |
| P2 | GAP-042-05 注意力涣散专门检测 | 中 |
| P3 | GAP-042-06 低频驱动机制 | 中 |

## 完成状态

| 缺口 | 状态 | 说明 |
|------|------|------|
| GAP-042-01 | ✅ 完成 | 改为强烈重复性警告提示而非没收冷却期（用户决策），ToolQuotaService + LoopInterventionMiddleware 第三级警告 + /clear 重置 |
| GAP-042-02 | ⏭ 跳过 | 用户决定跳过子代理结论倒装结构设计 |
| GAP-042-03 | ✅ 完成 | 双击 ESC 合并终止生成+重置计数（用户决策合并方案），IJccChatSession.ResetCountersAsync + MainViewModel.ResetCountersCommand |
| GAP-042-04 | ✅ 完成 | PlanStep 加 ConsecutiveFailures/IsCriticalNode，RecordStepFailureAsync + AutoReorderOnFailureAsync，鱼骨图关键节点不重排 |
| GAP-042-05 | ✅ 完成 | AttentionFatigueDetector 三阈值（压缩次数+运行时长+错误率），提示写交接文档+/clear/新会话 |
| GAP-042-06 | ✅ 完成 | LowFrequencyInductionService 按间隔低频诱导，避免高频噪声 |

## FrequencyGate 统一重构

三个信号服务本质上共享同一套频率门控逻辑，重构为统一基础设施：

| 服务 | 方向 | FrequencyGate 角色 |
|------|------|---------------------|
| ToolQuotaService | 高频闹钟（窗口内次数 >= 阈值） | HighFrequency 方向 |
| LowFrequencyInductionService | 低频冷却（距上次 >= 间隔） | LowFrequency 方向 |
| AttentionFatigueDetector | 多维度累积（压缩+时长+错误率） | 用 FrequencyGate 做累积计数（10年窗口=永不过期） |

- **文件**：`lib/abstractions/abs_core/core_utils/core/misc/FrequencyGate.cs`
- **设计**：`GateDirection` 枚举（HighFrequency/LowFrequency）+ `GateConfig` 配置 + `FrequencyGate` 类
- **对称性**：`ShouldSignal` 按方向分派 — High: `CountInWindow >= Threshold`，Low: `TimeSinceLast >= Window`
- **测试**：12 个 FrequencyGateTest + 25 个原有服务测试全部通过

## 运行时集成点

| 集成点 | 消费位置 | 已实现 | 已验收 |
|--------|----------|--------|--------|
| 集成点1: 压缩时记录涣散 | `AutoCompactService.CompactAsync` 压缩成功时调 `AttentionFatigueDetector.RecordCompaction()` | ✅ | ✅ 编译+测试通过 |
| 集成点2: 工具执行后高频警告 | `PermissionAwareToolExecutor.ExecuteAsync` 调 `ToolQuotaService.RecordCall` + `ShouldWarn` 时追加 `GetWarningPrompt` 到结果 | ✅ | ✅ 编译+190测试通过 |
| 集成点3: 流开始注入涣散/诱导 | `LoopInterventionMiddleware.InvokeAsync` 开头调 `BuildContextAwarenessPrompts`（涣散+诱导） | ✅ | ✅ 编译+410测试通过 |

## 关联

- 设计文档：《Agent 工具设计》"提示词"整节
- 关联 ADR：[0054](../adr/0054-llm-output-loop-detection-intervention.md)、[0086](../adr/0086-core-tech-selection-lock-design.md)
- 关联设计文档：[DSG016-LLM-OutputLoop-Detection-Design.md](../design/DSG016-LLM-OutputLoop-Detection-Design.md)
