# TASK144 — 思考摘要零时长：Markdown 消息项补齐与 TUI 真机验收

> 来源：[TASK142](TASK142-TUI视觉验收与工具输出缩进修复.md) 遗留项“思考块摘要行未取到”。
> 现象：MockServer 回合带 `thinking_content`，真实 TUI 只显示正文，不显示 `∴ 思考 · ctrl+o 展开推理`。

## 根因

- `AssistantMessageItem` / `MarkdownMessageItem` 用 `thinkingDurationMs > 0` 同时决定“是否有思考块”和“是否显示摘要”。
- 快流回合思考段耗时不足 1ms，`InProcessEventConsumer` 结算 `ThinkingDurationMs = 0`；CoT 内容存在但摘要被时长门槛过滤。
- 时长只应作为 spinner 的辅助信息，不能作为是否存在思考内容的唯一判据。

### 与 main #763 的关系

main 的 `b760d7417`（“思考时长 0ms 时默认视图丢失思考摘要行”）已独立修复 `AssistantMessageItem`
（新增 `HasThinkingSummary`，真实 WT 取景并通过测试）。本任务保留该实现，补齐其未覆盖的部分：

1. `MarkdownMessageItem` 同样以 `duration > 0` 判可见 —— 真实回复走 Markdown 渲染时仍会丢摘要行，本任务改为同一 `HasThinkingSummary` 判据；
2. 宿主 `EndThinkingIfActive` 增加 `[THINK]` 结算打点（`Diag.WriteLine`，受 `JCC_DEBUGLOG` 控制，默认不输出）——用真机证明“零时长仍显示摘要行”；
3. ConPTY + pyte 真机取景闭环（主屏摘要行 + ctrl+o 转录 CoT + duration=0ms 同次证据）。

TASK142 的真机矩阵已由 #763 回写，本任务不再改动 TASK142。

### 取证副产物：OpenAI mock 无法产出思考流

用 OpenAI mock 配 `thinking_content` 取不到摘要行的另一层遮蔽：
`test/mock/open_ai.mock_server/OpenAIResponseStrategy.cs` 的 `BuildStreamThinkingResponse` 固定返回空串
（注释“OpenAI 不支持思考内容”），`HasThinkingContent()` 为真但流里没有任何 reasoning 增量。
思考链路真机复现应使用 DeepSeek mock（`DeepSeekResponseStrategy` 按 `reasoning_content` 流式下发，
jcc 侧同为 OpenAI 兼容适配器 `OpenAIQueryService` 解析），本次真机验收即走该路径。
OpenAI mock 的该能力缺口未在本次修改（不是 UI 缺陷，留作后续 mock 能力补齐）。

## 修复边界

- 可见判据与 main `AssistantMessageItem.HasThinkingSummary` 完全一致：未脱敏，且时长大于 0 或 CoT 非空。
- `MeasureHeight` / `Render` 共用同一判据，避免高度与绘制分叉。
- 脱敏思考仍不显示摘要；Ctrl+O 展开条件仍要求非空 CoT。
- 不改事件协议、不改时长计算、不改 MockServer。

## 执行清单

- [x] Markdown 消息项零时长红灯（有 CoT、duration=0 仍应渲染摘要行）。
- [x] 统一 `HasThinkingSummary` 判据并转绿（保留 main 的 Assistant 测试）。
- [x] NativeTui 全量回归与宿主编译。
- [x] MockServer + ConPTY 真机取景，确认摘要行与 ctrl+o 展开。
- [x] 提交、推送并创建 squash auto-merge PR，守候 CI。

## 验证记录

- 红灯：`MarkdownMessageItem_ZeroDuration_ContentStillRendersThinkingRow`，旧实现 `MeasureHeight` 期望 3 实际 2。
- 定向绿：`ToolRenderThinkingTests` 6/6。
- 全量：`NativeTui.Tests` 3376/3376。
- 编译：`NativeTui.Host.csproj` 与 `JoinCode.csproj` Debug 均 0 警告 0 错误。
- 真机（DeepSeek mock，真实 `jcc.exe` + ConPTY + pyte，`.xxx/task144_capture.py`）：
  - 主屏（`.xxx/task144-tui/before_ctrl_o.txt`）：

    ```text
    ∴ 思考 · ctrl+o 展开推理
    ● 思考摘要真机验收通过。
    ```

  - ctrl+o 后 transcript（`.xxx/task144-tui/after_ctrl_o.txt`）：

    ```text
    ∴ 这是零时长思考验收的推理内容。
    ∴ 第二行推理用于验证转录展开。
    ● 思考摘要真机验收通过。
    ```

- 真机时序证据（`JCC_DEBUGLOG=1`，`.xxx/task144_timing.py`，优雅退出触发 ConsoleSuppressionGate 冲刷）：
  `.xxx/task144-tui/timing_evidence.txt` 记录 `[THINK] 思考段结算 duration=0ms chars=30` 且同次运行摘要行可见 —
  零时长分支在真实回合中被实际命中。

## 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| 零时长可见思考判据 | `AssistantMessageItem`（main #763） | ✅ | ✅ main 红转绿 + WT 取景 |
| 零时长可见思考判据 | `MarkdownMessageItem`（本任务） | ✅ | ✅ 单元红转绿 + ConPTY 摘要行 |
| 真实 TUI 摘要行 | MockServer + ConPTY | ✅ | ✅ `before_ctrl_o.txt`（duration=0ms 同次运行） |
| ctrl+o 展开 CoT | transcript 投影 | ✅ | ✅ `after_ctrl_o.txt` |