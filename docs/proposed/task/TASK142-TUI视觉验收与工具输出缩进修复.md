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
| ctrl+l 清屏后界面完整 | ✅ | `I-ctrl-l.txt` |
| 思考块摘要行（`∴ 思考 · ctrl+o 展开推理`） | ⚠️ 未取到 | Mock 回合带 `thinking_content` 未触发该行（`wt-thinking.txt` 仅正文），需真实推理流或专用 mock 字段 |
| 429/500 错误卡 | ⚠️ 未取到 | Mock 429 被重试吞掉，未在界面形成错误块 |
| 权限确认模态 / bash mode 边框 / ctrl+o 全屏 transcript | ⚠️ 未覆盖 | 需要对应触发链路与配置，本轮未做 |

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

## 四、验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|--------|--------|
| `MessageRenderer.ExpandTabs`（4 空格展开） | `RenderToolOutputLine` / `RenderToolErrorLine` / `RenderDiffLine` / `RenderErrorLine` / `RenderToolCall` / `RenderThinkingContent`；`MessageItems.SplitRawOutput` / `SplitErrorLines` / `ErrorMessageItem` | ✅ | ✅ 真实 WT：`wt-tabs-fixed2.txt`；ConPTY：`fix_probe` 原始帧 `1    public class Demo {` |
| `SurrogatePairAccumulator`（代理对合成） | `Win32InputPump.Drain` → `KeyPress(KeyKind.Char, 完整对)`；`ReplShell` Char 分支成对整串插入 | ✅ | ✅ 单测 `SurrogatePairAccumulatorTests`（6 例）+ `InputBuffer.InsertText` 保对；通道未启用故无真机截图 |
| 真实终端取景基建 | `.xxx/tui-visual/read-win-text.ps1`（按句柄读 UIA 文本）、`term.py`（ConPTY + pyte） | ✅ | ✅ 本轮全部帧均由此产出 |

## 五、测试与回归

- `NativeTui.Tests`：3374 通过（新增 7：tab 展开 1 + 代理对 6）
- `NativeTui.Host.Tests`：183 通过
- `jcc.exe` Debug 构建 0 警告 0 错误

## 六、遗留

- 思考块摘要行、429/500 错误卡、权限模态、bash mode 边框、ctrl+o 全屏 transcript 尚未真机取景。
- 缺陷 B 若要产品化修复，需先决策是否切换 Windows 输入通道（见第三节方向）。
