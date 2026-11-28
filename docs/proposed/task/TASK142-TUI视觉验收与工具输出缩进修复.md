# TASK142 — TUI 视觉验收与工具输出缩进修复

> 目的：不再只靠单元测试判断 TUI，改以**真实终端（Windows Terminal + ConPTY）取景**为准，逐项复核视觉面；
> 对真实终端暴露的缺陷先红测试后修复。

## 一、取证链路（可复现）

| 环节 | 工具 | 说明 |
|------|------|------|
| 确定性内容源 | `OpenAI.MockServer`（Debug 产物）+ 脚本回合 | `--config <json> --port 9911`，回合内容由 `scripted_turns` 指定，避免真实模型不确定性 |
| 真机取景器 | Windows Terminal + UIA `TextPattern` | 按窗口句柄定位终端文本元素读取字符（不截图猜字），脚本 `.xxx/tui-visual/read-win-text.ps1` |
| 自动取景器 | ConPTY + pyte | `.xxx/tui-visual/term.py`：以 pyte 重建屏幕，断言列宽/属性/分隔线 |
| 运行隔离 | `JCC_APP_DATA_FOLDER` 独立目录 | 直连模式需 `JCC_ENDPOINT` + `JCC_ALLOW_DIRECT_FALLBACK=1`；`JCC_ROUND_GATE=off` 避免轮次闸门内耗脚本回合 |

## 二、验收矩阵（本轮结果）

| 视觉面 | 结论 | 证据 |
|--------|------|------|
| 启动横幅 / provider 行 / 工作目录行 | ✅ | `fix2-boot.txt`：`openai/gpt-4o · API 按量计费` + `D:\project\w4` |
| 输入框上下分隔线整宽 | ✅ | 120 列下两行 `─` 均 120 宽（`fix2-boot.txt`） |
| 首次注入摘要行（📎 … 点击展开） | ✅ | 同帧内出现且不越界 |
| Markdown 标题/列表/代码块 | ✅ | 前轮取证 `C-markdown-final.txt`，中文注释与缩进保留 |
| Markdown 表格（方框对齐 + 中文列宽） | ✅ | `wt-table2.txt`：`┌─┬─┐ / ├─┼─┤` 闭合，表头居中，全角列宽对齐 |
| emoji 渲染（👍 🎉 👨‍👩‍👧‍👦 ❤） | ✅（历史回合内容） | `wt-turn1.txt`；输入路径见缺陷 B |
| 长段落折行 + 悬挂缩进 | ✅ | `wt-turn2.txt`：续行 2 空格缩进，行首无残字 |
| 工具卡折叠（20 行 + `… +N lines (ctrl+o)`） | ✅ | `wt-turn3b.txt` |
| 工具输出制表缩进 | ✅（修复后） | `wt-tabs-fixed2.txt`、ConPTY `fix_probe`：`1    public class Demo {` / 二级 8 空格 |
| 窄屏缩放（79 列） | ✅ | `wt-narrow.txt`：分隔线与内容同为 79 列 |
| ctrl+l 刷新界面（内容保留、重绘） | ✅ | `A10-ctrl-l.txt`：执行后历史保留，语义为刷新而非清屏（勘正旧表述） |
| 思考块摘要行（`∴ 思考 · ctrl+o 展开推理`） | ✅（修复后） | DeepSeek mock（`reasoning_content`）真实 WT：`C3-think.txt` 摘要行 + 正文；`C3-expand.txt` / `shot-C3-expand.png` 转录内推理全文。修复前同配置整块消失（缺陷 C） |
| 429/500 错误卡 | ⚠️ 不可取景（设计使然） | `ApiException.IsRetryable`（≥500/429/408）会触发 `SuggestedRetryCount=3` 重试，mock 单回合失败被重试掩盖；改用非可重试 400 取到错误卡 |
| 错误卡（HTTP 400，正文截断 + `...`） | ✅ | `B6-error400c2.txt` / `shot-B-error400c.png`：`● 查询执行失败: [LLM.StreamingChatCompletion] HTTP 400: {` + JSON 正文截断 |
| 权限确认模态（bash） | ✅ | `B1-permission.txt` / `shot-B-permission.png`：`Tool use` / `Bash` / `Do you want to proceed?` / `❯ 1. Yes` / `2. Yes, and don't ask again` / `3. No` / `Esc to cancel · ctrl+e 解释 · Tab 修改说明`；spinner `✶ Jitterbugging… (bash 剩余 112s)`、footer `esc 打断` |
| 权限允许后 bash 执行与折叠 | ✅ | `B2-after-allow.txt`：目录清单 3 行 + `… +8 lines (ctrl+o)` |
| bash mode 边框/提示符 | ✅ | `A2-bash-mode.txt`：空输入按 `!` → 提示符 `❯`→`!`；Esc 恢复 |
| 命令菜单（`/` 触发，中文描述 + 无匹配态） | ✅ | `A3-command-menu2.txt`、`A16-after-menu-esc.txt`：`/add-dir`、`/advisor`、`/agents`…；输入 `abcPaste` 显示「无匹配命令」 |
| `/help` 全量面板 | ✅ | `A5-help.txt`：约 118 行命令清单，底部整宽分隔线与 footer 完整 |
| ctrl+o 全屏 transcript | ✅ | `A8-transcript.txt`、`C3-expand.txt`：头部 `详细转录 · ctrl+o 切换 · ↑↓ 滚动 · / 搜索 · q 退出`，历史回合 + 思考全文；`q` 退出 |
| 多行粘贴 chip | ✅ | `shot-A4.png`、`A17-multiline-paste2.txt`：5 行粘贴 → `[Pasted text #1 +4 lines]` |
| QuickOpen / GlobalSearch | ⚠️ 真机不可达 | WT 抢占 `Ctrl+Shift+P`（终端命令面板）与 `Ctrl+Shift+F`（查找），按键不进入 TUI；仅组件单测覆盖。取景时该面板会吞掉后续所有按键，须先 Esc 退出 |
| 鼠标交互面（hover/点击展开、选区、unread divider、message actions） | ⚠️ 未覆盖 | 需鼠标注入驱动，本轮未做 |

## 三、缺陷与修复

### 缺陷 A（已修复）：工具输出制表符塌陷

- **现象（真实终端）**：`read` 含 `\t` 的文件，行号与正文粘连 `1public class Demo {`，两级缩进全部消失。
- **根因**：`Screen.WriteText` 把 `'\t'` 当零宽跳过（`RuneWidth.Of('\t') = 0`），而工具输出/补丁行从未展开制表符。
- **修复**：`MessageRenderer.ExpandTabs`（4 空格，对齐 `PastedContentTracker.ExpandTabs` 既有口径），在工具输出行、工具错误行、diff 行、错误正文、工具调用参数、思考正文统一展开；`SplitRawOutput` / `SplitErrorLines` 构造期展开。
- **验证**：红测试 `RawOutput_WithTabs_ExpandsToFourSpaces`（修复前实际值 `1public class Demo {`）→ 绿；真实 WT 复核见 `wt-tabs-fixed2.txt`。

### 缺陷 B（平台边界，已定位不可在应用层恢复）：非 BMP 输入变 U+FFFD

- **现象**：WT 剪贴板粘贴 `👍测试`，输入框与 `transcript.jsonl` 均落 `��测试`（1 个 emoji → 2 个 U+FFFD）。
- **字节级证据**：`RawStdinPump` 临时打点显示 stdin 收到 `EF BF BD EF BF BD E6 B5 8B E8 AF 95`，即 **U+FFFD 在进入进程之前**已被替换；应用 UTF-8 解码器行为正确。
- **结论**：Windows Terminal → ConPTY 的粘贴/按键文本转换把 UTF-16 代理对逐半替换为 U+FFFD，属终端/ConPTY 层限制；当前 Windows 主机走 `RawStdinPump`（VT stdin），应用侧无法还原。
- **顺带加固（非本条修复）**：`Win32InputPump`（ReadConsoleInput 通道，`KEY_EVENT.UnicodeChar` 为单码元）新增 `SurrogatePairAccumulator`，把「高代理 + 低代理」两条记录合成完整字符再派发；`ReplShell` 的 `Char` 分支拒绝孤立代理、成对时整串插入。该通道当前未被宿主选用，但作为既有回退路径语义已正确。
- **可行的产品化方向（未实施，待决策）**：Windows 下改用 `Win32InputPump` 读取原始 KEY_EVENT（可保住代理对），代价是需自行实现粘贴分行/括号粘贴语义。

### 缺陷 C（已修复）：思考时长 0ms 时摘要行整块消失

- **现象（真实终端）**：DeepSeek mock 流式返回 `reasoning_content`，`ctrl+o` 转录里能看到推理全文，但默认视图只有正文，没有 `∴ 思考` 摘要行——用户没有任何「有思考可展开」的入口。
- **根因**：`InProcessEventConsumer` 以墙钟差结算 `ThinkingDurationMs`；mock/极快响应下思考与正文在同一毫秒内到达，时长结算为 `0`。`AssistantMessageItem` 的可见性与高度都只看 `thinkingDurationMs > 0`，于是 CoT 非空却被判为「无思考」。
- **修复**：可见性判据改为 `!thinkingRedacted && (thinkingDurationMs > 0 || thinkingContent 非空)`（新增私有 `HasThinkingSummary`，`MeasureHeight` 与 `Render` 共用），保证「有内容必有入口」。
- **验证**：先红测试 `AssistantMessageItem_ThinkingContentWithoutDuration_StillRendersSummary`（修复前 `MeasureHeight` 实际 2、期望 3）→ 绿；真实 WT 复核 `C3-think.txt` 出现 `∴ 思考 · ctrl+o 展开推理`，`C3-expand.txt` 转录可见推理全文。

### 取景器失真清单（真机验收教训）

- UIA 文本不覆盖浮层与输入态（命令菜单、输入框内容可能滞后/缺失）→ 以截图判定。
- WT 抢占 `Ctrl+Shift+P` / `Ctrl+Shift+F`，且其命令面板会吞掉后续按键 → 取景前先点标题栏聚焦，必要时 Esc 关闭浮层。
- `SendKeys("{ESC}/")` 会被 VT 解析为 Meta+`/`，Esc 必须单发、间隔后再发下一键。
- 单个 Enter 偶发丢失 → 文本与 Enter 拆成两次驱动调用补发。

## 四、验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| `MessageRenderer.ExpandTabs`（4 空格展开） | `RenderToolOutputLine` / `RenderToolErrorLine` / `RenderDiffLine` / `RenderErrorLine` / `RenderToolCall` / `RenderThinkingContent`；`MessageItems.SplitRawOutput` / `SplitErrorLines` / `ErrorMessageItem` | ✅ | ✅ 真实 WT：`wt-tabs-fixed2.txt`；ConPTY：`fix_probe` 原始帧 `1    public class Demo {` |
| `SurrogatePairAccumulator`（代理对合成） | `Win32InputPump.Drain` → `KeyPress(KeyKind.Char, 完整对)`；`ReplShell` Char 分支成对整串插入 | ✅ | ✅ 单测 `SurrogatePairAccumulatorTests`（6 例）+ `InputBuffer.InsertText` 保对；通道未启用故无真机截图 |
| 真实终端取景基建 | `.xxx/tui-visual/read-win-text.ps1`（按句柄读 UIA 文本）、`win-shot.ps1`（按 HWND 截图）、`wt-drive.ps1`（焦点/粘贴/按键/回车/回读）、`term.py`（ConPTY + pyte） | ✅ | ✅ 本轮全部帧均由此产出 |
| `AssistantMessageItem.HasThinkingSummary`（时长 0 但 CoT 非空仍出摘要行） | `MeasureHeight` / `Render` / `ToggleThinking`（ctrl+o 转录展开） | ✅ | ✅ 红测试 `AssistantMessageItem_ThinkingContentWithoutDuration_StillRendersSummary` → 绿；真实 WT `C3-think.txt` + `C3-expand.txt` |

## 五、测试与回归

- `NativeTui.Tests`：3375 通过（新增 8：tab 展开 1 + 代理对 6 + 思考摘要 1）
- `NativeTui.Host.Tests`：183 通过
- `jcc.exe` Debug 构建 0 警告 0 错误

## 六、遗留

- 429/500 错误卡因内建重试（`SuggestedRetryCount=3`）不可稳定取景；如需验收重试动画/最终失败文案，需可控重试次数开关。
- QuickOpen / GlobalSearch 被 WT 快捷键抢占，真机不可达；如需真机验收，需改 WT keybindings 或换 ConPTY 直喂序列。
- 鼠标交互面（工具卡 hover/点击展开、选区高亮与复制、unread divider、message actions bar）尚未取景。
- 缺陷 B 若要产品化修复，需先决策是否切换 Windows 输入通道（见第三节方向）。
