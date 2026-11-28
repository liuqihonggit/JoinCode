# TASK145 — 每轮注入提示词透明化（移除首次快照，逐轮可见）

> 用户反馈：不要“首次注入提示词”，而是每轮注入；并且用户要能看见每轮实际注入了什么。
> 现状：折叠项虽然每轮都插一条，但摘要固定“首次注入”，内容是启动时用 `ISystemPromptProvider`
> 取的一次性快照（与引擎真实装配不同源），且附带注册表工具清单/临时 .md 文件。

## 根因

- `Program.cs` 启动时构建 `promptSnapshotBuilder`（`【首次注入的系统提示词】` + 工具清单）并写临时 .md；
  `ReplShell.SubmitCore` 每轮用这份静态快照建 `PromptContextItem`，所以每轮都显示“首次注入”。
- 引擎每轮实际注入的是 `SystemPromptMiddleware` 现装配的静态前缀 + 动态后缀（随 profile/环境/提醒变化），
  宿主快照既不是同一份数据，也不会随轮次更新。

## 决策

> ADR: [0169](../adr/0169-per-round-prompt-injection-visibility.md)

1. 引擎在 `PreProcessMainAgentAsync` 完成 `PrepareContextAsync` 后读取 `IChatContextManager.GetMessageListAsync()`，
   取 System 角色消息（静态前缀 + 动态后缀）作为本轮快照，经 `IEventConsumer.OnPromptInjected`（默认空实现）发出。
2. TUI 宿主转 `BridgeEvent.PromptInjected`；`ReplBridgeDriver` 按到达顺序建 `PromptContextItem(round, chars, content)`，
   摘要“📎 第 N 轮注入 · 提示词 M 字符 · 约 K tokens · 点击展开”，TokenUsage 回填本轮 tokens。
3. `/prompt` 或点击打开对应轮次全文（transcript 备用屏，展开互斥）；点击按唯一 hit id 定位轮次。
4. 删除旧的首次快照基础设施（启动快照构建/临时 .md/工具清单注入）与已无调用方的 `PromptDetailDialog` 模态。

## 修改清单

| 层 | 文件 | 变更 |
|---|---|---|
| 契约 | `IEventConsumer.cs` + `PromptInjectionSnapshot.cs` | 新增默认回调 `OnPromptInjected`（既有实现方零改动） |
| 引擎 | `SessionController.cs` | 预处理后读取 System 消息并回调消费方 |
| 宿主 | `InProcessEventConsumer.cs` / `Program.cs` | 转 `PromptInjected` 事件；删除启动快照/工具清单/.md 写入 |
| TUI | `BridgeEvent.cs` / `ReplBridgeDriver.cs` | 新增事件与逐轮折叠项（轮次计数、TokenUsage 回填目标） |
| TUI | `PromptContextItem.cs` / `ReplShell.cs` / `TranscriptLines.cs` / `PromptTransparencyEngine.cs` | 轮次模型、唯一 hit id、`/prompt` 打开最近轮、点击定位对应轮 |
| 清理 | `PromptDetailDialog.cs` | 无调用方死代码，移至 `.xxx/PromptDetailDialog.cs.20261128.del` |

## 验证记录

- 单元：`NativeTui.Tests` 3379/3379；`Host.Tests` 1442/1442（新增 SessionController 注入快照用例）。
- 编译：`JoinCode.csproj` Debug 0 警告 0 错误。
- 真机（DeepSeek mock + ConPTY + pyte，`.xxx/task145_capture.py`，PASS）：
  - 主屏两轮注入项并存（`round2_main.txt`）：

    ```text
    ❯ 第一条
    📎 第 1 轮注入 · 提示词 20162 字符 · 约 6597 tokens · 点击展开
    ● 第一轮回答。
    ❯ 第二条
    📎 第 2 轮注入 · 提示词 21869 字符 · 约 7025 tokens · 点击展开
    ● 第二轮回答。
    ```

  - `/prompt` 打开展开最近一轮全文（`prompt_detail.txt`）：transcript 内含真实系统提示词正文
    （“您是一位交互式AI助手…”、“# 网络安全”等段落），证明内容来自引擎本轮装配而非启动快照。

## 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| `IEventConsumer.OnPromptInjected` + 引擎快照读取 | `SessionController.PreProcessMainAgentAsync` | ✅ | ✅ 单测 `StreamResponseAsync_MainAgentPath_EmitsPromptInjectionSnapshot` |
| `BridgeEvent.PromptInjected` → 逐轮 `PromptContextItem` | `ReplBridgeDriver` / `ReplShell` | ✅ | ✅ 单测（逐轮项/展开互斥/定向打开）+ 真机两轮并存 |
| `/prompt` 打开最近一轮全文 | `PromptTransparencyEngine` → `TogglePromptContext` | ✅ | ✅ 真机 `prompt_detail.txt` |
| 旧首次快照/工具清单/临时 .md 移除 | `Program.cs` / `ReplShell.AttachEngineServices` | ✅ | ✅ 编译 + 真机不再出现“首次注入” |

## 遗留

- 首版快照只覆盖对话消息中的 System 注入（静态前缀 + 动态后缀）；API 请求 `tools` 数组未纳入
  （其装配在 LLM 适配层，若需查看需另开请求级钩子 — 见 ADR 0169 备选方案）。
- 长会话每轮保存提示词全文（约 20K 字符/轮），后续可按需加保留窗口。