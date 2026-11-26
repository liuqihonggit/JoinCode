# TASK109 — TUI 滚轮失效与帧重叠修复

> 来源：用户真机验收（2026-11-26 22:20）——「/help 的时候我测试了鼠标滚轮滚动是失效。而且有时候，
> 我看见渲染错位现象，尤其是输入栏和上面一些文字重叠了」。
> 纪律：先文档 → 先门禁（红）→ 再实现 → 真机取证，不靠截图猜。

## 1. P19 鼠标滚轮失效

### 1.1 真机现象

- `/help` 面板打开时滚轮完全无反应；主屏滚轮同样无反应（无 scrollback 回看、无内容滚动）。
- 点击（提示词 chip 展开 / 工具结果行展开）正常 —— 说明 `MOUSE_EVENT` 通道是通的，只有滚轮这一类被吃掉。

### 1.2 根因（代码级，确定性）

`Win32InputPump.InputRecord` 的 union 字段偏移写错一格：

Win32 文档的 `MOUSE_EVENT_RECORD` 布局：
`dwMousePosition@4 / dwButtonState@8 / dwControlKeyState@12 / dwEventFlags@16`。
`KEY_EVENT_RECORD` 的 `dwControlKeyState@16`。

现实现把 `EventFlags` 声明在 **offset 12**（那是鼠标的 `dwControlKeyState`），
`ControlKeyState` 声明在 16。于是鼠标记录读到的 `EventFlags` 恒为 0（鼠标通常无修饰键），
`EventFlags switch` 落入 `_` 分支 → 按 `ButtonState` 低两位判 Press/Release。
滚轮事件的 `ButtonState` 是高位 word 的 ±120 增量（低两位为 0）→ **被判定为 Release**。
结果：`HandleMouseEvent` 收到 Release（走选区链），`WheelUp/WheelDown` 分支永不执行。

第二个错误：滚轮方向判定 `(ButtonState & 0xFF000000) != 0` 是**反的且漏判**——
向上滚动 delta = `+120` → `0x00780000`，与 `0xFF000000` 相与为 0 → 被判 WheelDown；
向下滚动 delta = `-120` → `0xFF880000` → 非 0 → 被判 WheelUp。正确判定必须有符号取高位 word：
`(short)(ButtonState >> 16) > 0 ? WheelUp : WheelDown`。

第三个错误：`ControlKeyState` 在鼠标记录下读的是 `dwEventFlags`（0x0008 = WHEELED 会被当成
`LEFT_CTRL_PRESSED`），且 `DecodeMouse` 直接把 `Shift/Meta/Ctrl` 硬编码为 false —— 修饰键信息被丢弃。

同类实现 `terminal/Win32ConsoleInput.cs` 的偏移是对的（`EventFlags@16`），可作为对照；
`Win32InputPump` 是实际运行的那条链（`ReplShell` 默认构造 `new Win32InputPump()`）。

### 1.3 第二层：inline 主屏即使收到滚轮也不滚

`ReplShell.ScrollIfViewport` 只在 `!_inline` 时调 `Messages.ScrollBy`，注释写「inline 模式历史由
终端滚轮原生回看，no-op」。但应用自 TASK077 起默认启用 DECSET `?1000h/?1002h/?1003h/?1006h`，
Windows Terminal 一旦看到鼠标追踪，就把滚轮事件交给应用、**不再滚自己的 scrollback** ——
即「应用吃掉滚轮 + 应用侧 no-op」= 滚轮彻底失效。这是 P19 的语义层根因，与偏移 bug 叠加。

修复方向（待真机确认后定稿）：
- 偏移/方向修好，滚轮事件能正确到达；
- inline 主屏的滚轮不能再 no-op，需给出真实纵向滚动语义（候选：视口分页浏览 / 帧内滚动窗口）。

### 1.4 门禁

- `Win32MouseRecordTests`：断言 `InputRecord` 的鼠标字段偏移与 Win32 文档一致
  （`MouseControlKeyState@12`、`EventFlags@16`）。
- 同文件：`DecodeMouse` 纯函数（eventFlags/buttonState/坐标/修饰键入参）——
  上滚/下滚/按下/松开/双击/修饰键六个用例。
- 回归面：既有 `Win32InputPumpModeTests`（QuickEdit 位）保持绿。

## 2. P20 输入栏与上方文字重叠

### 2.1 真机现象

「有时候」出现，输入框（含边框/提示符行）与上方若干行文字叠在一起，不是终端 scrollback 里的旧帧。

### 2.2 待定位假设（按可能性排序）

1. **inline 帧高 > 视口后的相对光标失同步**：`InlineComposer` 用内容坐标做 `CUU/LF`，
   帧高超过终端行数时顶行已进 scrollback，`CursorUp` 被终端钳在视口顶 → 写错行（残影/叠字）。
   `HasScrollbackChange` 用 `prev.Rows` 算可达下界，帧增长时少算一行以上（`next.Rows` 才是新状态），
   可能放过一次不可达改写。
2. `Screen.ClipScope` 只保护 ScrollBox 内部；输入框/帮助面板/建议区写在 clip 外，
   若各自 `MeasureHeight` 与 `Render` 实际行数不一致（或 frame.Y 为负被钳），就会互相压。
3. 输入区软折行宽度与 `Screen.WriteText` 的实际列宽不一致（`InputLineLayout.Wrap` 按 grapheme
   整块宽，`WriteText` 逐 rune 累加 —— ZWJ emoji 会 2 列算成 6 列），行尾溢出写到底框线上。

### 2.3 取证方法

- `VirtualTerminal`（tests/terminal）真画帧断言，不靠肉眼截图；
- 真机：WT 中 `ctrl+shift+a` + `ctrl+shift+c` → `Get-Clipboard -Raw` 逐行量宽，
  区分「终端 reflow 留下的旧行」与「本帧叠写」。

## 3. 修复清单

| 编号 | 问题 | 状态 |
|---|---|---|
| P19-a | `Win32InputPump.InputRecord` 鼠标字段偏移（EventFlags@12 应为 16） | 待修 |
| P19-b | 滚轮方向判定（`0xFF000000` → 有符号高位 word） | 待修 |
| P19-c | 鼠标修饰键未透传（shift/ctrl/alt 硬编码 false） | 待修 |
| P19-d | inline 主屏滚轮 no-op | 待定（需真机取证后定语义） |
| P20 | 输入栏与上方文字重叠 | 待定位 |