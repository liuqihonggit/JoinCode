# ADR 0169 — 每轮注入提示词透明化：引擎快照回调 + 单轮折叠项

- 状态：accepted（2026-11-28 决策并实现，真机验收通过）
- 日期：2026-11-28
- 关联：TASK079（首次注入透明化）、TASK145（本决策落地）

## 背景

TASK079 的提示词透明化只在启动时用 `ISystemPromptProvider.GetSections()` 取一次快照：
`PromptContextItem` 摘要固定写“首次注入”，内容与引擎每轮真实装配结果无关。
引擎每轮在 `SystemPromptMiddleware` 里按 profile/工具组/动态段落重新装配静态前缀 + 动态后缀，
宿主侧的快照既不是同一份数据源，也不会随轮次更新——用户看不到“这一轮实际注入了什么”。

## 决策

1. **数据源 = 引擎每轮真实装配结果**：`SessionController.PreProcessMainAgentAsync`
   在 `PrepareContextAsync`（系统提示/动态系统消息已写入上下文）之后读取
   `IChatContextManager.GetMessageListAsync()`，取 System 角色消息（静态前缀 + 动态后缀）拼接为快照。
2. **传递通道 = 既有 `IEventConsumer` 默认回调**：新增 `OnPromptInjected(PromptInjectionSnapshot)`
   默认空实现（对齐 `OnSystemNotice` 先例，既有实现方零改动）；TUI 宿主实现为
   `BridgeEvent.PromptInjected`，引擎侧不持有 UI 概念。
3. **UI = 每轮一条折叠项**：移除启动态“首次注入”快照（`Program.cs` 静态快照 + 临时 .md + 工具清单注入），
   改为事件到达时创建 `PromptContextItem(round, chars, content)`，历史逐轮回看；
   `/prompt` 与点击打开对应轮次，展开互斥（同思考块单焦点语义）。
4. **不纳入首版**：API 请求的 `tools` 数组（其装配在 LLM 适配层，取真实值需另开请求级钩子）；
   快照只覆盖对话消息中的 System 注入。旧版的注册表工具清单随首次注入一起移除。

## 备选方案（放弃）

- **宿主侧继续用 `ISystemPromptProvider` 重新取**：与引擎实际装配（profile 工具组、动态段落）不同源，
  仍会展示“似是而非”的提示词；且 `GetSections()` 需要宿主同步阻塞调用。
- **在 LLM 适配层抓完整请求（含 tools）**：最精确，但跨 cognition/llm 两层新增请求级钩子，
  首版成本高；留作后续若用户需要查看 tools 数组时再扩展。
- **单条“本轮注入”原地刷新**：实现最省内存，但丢失历史；用户明确要求逐轮可见。

## 影响

- 契约：`IEventConsumer` 新增默认方法；`BridgeEvent` 新增 `PromptInjected`（JSON 派生类型）。
- 宿主：删除启动快照与 .md 临时文件写入；`AttachEngineServices` 去掉提示词相关参数。
- UI：`PromptContextItem` 改为轮次模型（round/chars/content，唯一 hit id），`/prompt` 打开最新轮。
- 内存：每轮保存系统提示词全文（约 90K 字符/轮）；长会话按轮累积，可后续按需加保留窗口。