# TASK059 — 退出安全按键（双击 Esc 中断 / Ctrl+C 复制 / 永不误退出）

> 状态：in_progress（2026-10-21 开工）
> 前置：TASK058 完结；用户指令："双击ESC是中断对话, Ctrl+C是复制, 这两个都不要关闭终端, 因为很不舒服"

## 痛点（用户真机反馈）

| 键 | 现行为 | 问题 |
|----|--------|------|
| Esc 闲时空输入 | `_running = false` 退出 | 想打断按 Esc，回合恰好结束 → 终端直接关 |
| Ctrl+C | `_running = false` 退出 | 想复制的肌肉记忆 → 终端直接关 |

## 方案（Core 零依赖 + 宿主委托注入）

1. **Esc 双击中断**：忙时 500ms 内两击 → `Interrupt()`；单击无操作（等双击窗口，时钟 `internal Func<long> EscClock` 可注入测试）；闲时有文本清输入（保留）；**Esc 永不退出**
2. **Ctrl+C 复制**：复制滚动区最后一条 AssistantMessageItem 文本（基准语义：复制最后输出）；剪贴板写入经构造参数 `Action<string>? copyToClipboard` 宿主注入（ADR 0140 — Core 不依赖引擎 IClipboardService）；复制后 PostInfo 反馈；无 assistant 消息/未注入委托时无操作
3. **退出路径**：Ctrl+D 空输入退出（已有）+ `/exit` 引擎命令（ExitCommand 确认对话框）— 文档明示

## 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| Esc 双击状态机（ReplShell.EscClock + HandleEscape 改造） | 忙时中断链 | ✅ | 待真机 |
| Ctrl+C 复制（copyToClipboard 委托 + 最后 assistant 消息） | Program 接线引擎 IClipboardService | ✅ | 待真机 |
| 退出路径保留（Ctrl+D + /exit） | 文档说明 | ✅ | 待真机 |
