# TASK077 — ReadConsoleInput 输入泵重构（主屏鼠标点击支持）

## 背景

用户反馈：jcc 启动后 `PromptContextItem` 渲染的"点击展开"超链接，鼠标左键单击无效，无法进入 transcript 第二屏查看系统提示词全文。

根因（`e9765ab14` 提交记录在案）：`Console.ReadKey` / stdin `ReadFile` 在 Windows 上只返回 `KEY_EVENT` 记录，`MOUSE_EVENT` 被 .NET Console API 静默跳过。多轮试验无论武装 `?1000h` 还是换编码都收不到鼠标事件。

当前替代：`ctrl+o` / `/prompt` 可进入第二屏（已验证）。用户要求推进 `ReadConsoleInput` P/Invoke 泵重构，实现点击进入。

## 四问回答

1. **专家隐性实践**：跨终端实测鼠标上报矩阵（WT/conhost/WezTerm）；查 Win32 Console API 原始文档（`ReadConsoleInput`/`INPUT_RECORD`/`MOUSE_EVENT_RECORD`）；查 .NET runtime 源码确认 `Console.ReadKey` 跳过 `MOUSE_EVENT`；P/Invoke 签名验证；并发安全（输入泵线程 vs 渲染线程）；NativeAOT 兼容性（P/Invoke + StructLayout 兼容）
2. **原始素材清单**：Win32 Console API 文档（MSDN `ReadConsoleInput`/`INPUT_RECORD`/`KEY_EVENT_RECORD`/`MOUSE_EVENT_RECORD`）；.NET runtime 源码；现有 `Win32ConsoleInput.cs` P/Invoke 基建；基准侧 ink 鼠标处理（`App.tsx handleMouseEvent`）
3. **具体方法与工具**：P/Invoke `ReadConsoleInput` spike 验证；单元测试 mock `INPUT_RECORD` 验证 `MakeMouseEvent`/`MakeKeyInfo`；跨终端手动 exe 验收（ADR 0080）
4. **独立验证来源**：Win32 API 文档 + 实际终端鼠标事件录制 + 现有单元测试套件

## 发现的 bug

`Win32ConsoleInput.InputRecord` 结构体布局错误（从未被验证过）：

| 字段 | 当前偏移 | 正确偏移 | 说明 |
|------|---------|---------|------|
| `RepeatCount` | 10 | 8 | KEY_EVENT wRepeatCount |
| `VirtualKeyCode` | 14 | 10 | KEY_EVENT wVirtualKeyCode（当前与 UnicodeChar @14 冲突） |
| `EventFlags` | 12 | 16 | MOUSE_EVENT dwEventFlags |
| `MouseControlKeyState` | 缺失 | 12 | MOUSE_EVENT dwControlKeyState（修饰位提取缺失的根因） |

## 实现步骤

| 步骤 | 内容 | 状态 |
|------|------|------|
| 1 | 修复 `Win32ConsoleInput.InputRecord` 布局 bug + 补 `MouseControlKeyState` 字段 + 修饰位提取 | ⏳ |
| 2 | 改 `InputRecord`/`MakeMouseEvent`/`MakeKeyInfo` 为 internal 可测试 + 单元测试 | ⏳ |
| 3 | spike exe 验证 `ReadConsoleInput` 能收到鼠标事件（ADR 0080 手动验收） | ⏳ |
| 4 | 包装 `Win32ConsoleInputPump : IInputPump`（后台线程 + Channel 非阻塞 Drain） | ⏳ |
| 5 | 主屏鼠标武装（`ReplShell.Run` 解除注释，启用 `1000+1002+1003+1006`） | ⏳ |
| 6 | Host 装配（Windows 默认用新泵，Linux 仍用 RawStdinPump） | ⏳ |
| 7 | 集成验证：单击 PromptContextItem → 进入第二屏 | ⏳ |

## 验收标准

- [ ] `Win32ConsoleInput` 布局 bug 修复，单元测试覆盖 `MakeMouseEvent`/`MakeKeyInfo`
- [ ] spike exe 手动验收：Windows Terminal 下点击有坐标显示
- [ ] `Win32ConsoleInputPump` 实现 `IInputPump`，非阻塞 `Drain`
- [ ] 主屏鼠标武装启用，单击 `PromptContextItem` → 进入 transcript 第二屏
- [ ] Linux 平台不受影响（仍走 RawStdinPump）
- [ ] 全仓编译 + 单元测试绿

## 设计决策

- **修饰位/Drag/Move 语义对齐**：方案 B（直接从 `INPUT_RECORD` 字段映射，不走 SGR 中间层）+ 方案 C 范围（先 Press/Release/Wheel + 修饰位，Drag/Move 后续迭代）
- **平台分流**：Windows 用 `Win32ConsoleInputPump`，Linux 用 `RawStdinPump`（VT 序列天然含鼠标）
- **线程模型**：后台 Thread + 有界 Channel<KeyPress>（对齐 `RawStdinPump` 模式）
