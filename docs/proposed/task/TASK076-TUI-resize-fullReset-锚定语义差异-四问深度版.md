# TUI resize fullReset 锚定语义差异 — 四问深度版

> **状态**：implemented — 方案 A 基准对齐已落地（2026-10-31，TDD 红绿 + 探针物理验收全绿）
> **来源**：用户报告"拖拽终端窗口调整大小时，banner 内容没有永远保证最顶部" + 基准对比调研
> **创建时间**：2026-10-31
> **关联**：TASK072 基准对比调研模板；`native_tui/tool/screenshot/resize_probe/`（本次建成的 L3 感知探针）；L2 像素比对 `native_tui/tool/screenshot/compare/`

---

## 一、四问回答（调研前置）

### 1. 专家隐性实践
1. **物理终端视角实测**：不满足于进程内断言测试（`ReplShellResizeTests` 只验证屏对象重建/diff 分支），专家会问"真实终端上用户看到了什么" — ConPTY 下游字节流 = Windows Terminal 所见的同一流，是唯一可脚本化的物理视角
2. **终端 reflow 语义考古**：resize 时 conhost 对缓冲区做 rewrap 重排，物理光标落点由 reflow 决定 — 任何基于"旧坐标系信念"的相对移动在 resize 后都不可靠；基准 log-update.ts:289-291 注释明确承认 CUU 视口顶钳制会"writes to land 1 row off and garbling the output"
3. **基准注释考古**：`fullResetSequence_CAUSES_FLICKER` 的命名、log-update.ts:139-141 "Resizing is a rare enough event that it's not practically a big issue" — 基准作者**知情选择**了"resize 时全量重发 + 闪烁"的代价换确定性
4. **量化定位度量**：不描述"错位"而量化"banner 落在第几行" — 标记行定位使差异可回归比对

### 2. 原始素材清单
1. **基准 `src/ink/log-update.ts:142-147`** resize 触发原文（高度收缩或宽度变化 → fullReset）；`:503-513` fullReset = `clearTerminal` + 整帧重绘；`:515-523` renderFrameSlice 逐行 CR+LF 语义
2. **基准 `src/ink/clearTerminal.ts:59-69`** `clearTerminal` 序列原文：现代终端 = `ERASE_SCREEN(2J) + ERASE_SCROLLBACK(3J) + CURSOR_HOME(H)` — 绝对锚定 (0,0)
3. **C# `InlineComposer.cs` / `ReplShell.cs` 现状源码**（本次逐行阅读，非 AI 转述）
4. **ECMA-48 / VT510 语义**：CUU 在视口顶钳制；`CSI 2J` 擦视口、`CSI 3J` 擦 scrollback、`CUP` 绝对定位
5. **探针留存的原始字节流** `stream.bin`（demo_seq 8073 字节，可离线重放复核）

### 3. 具体方法与工具
1. **双侧源码逐行对照**：C# `InlineComposer.Compose/FullReset` vs 基准 `LogUpdate.render/fullResetSequence`
2. **ConPTY 多相位探针**（本次新建）：pywinpty spawn 真实 exe → `ResizePseudoConsole` 相位序列 → 下游流按相位偏移在 pyte（独立开源 VT 解释器）全量重放 → 可见网格 + 标记行定位
3. **标记行量化**：`--mark "jcc v"` 等正则逐相位定位 banner/输入框/footer 行号，输出跨相位汇总表

### 4. 独立验证来源
1. **pyte**（第三方 VT 解释器，非本项目代码、非 AI 生成）渲染的网格 — 独立于双侧实现作者
2. **demo_seq 探针报告**（`.xxx/resize_probe/demo_seq/report.md`，2026-10-31）：banner 行号 0 → **4** → 0 → 0，缩窗错位可重复复现
3. **stream.bin 原始流**：任何第三方可用 pyte/其它解释器重放复核，无需信任本报告
4. **基准行为旁证**：Windows Terminal 里跑真实 Claude Code，resize 后布局永远对齐（用户可自行验证的日常经验）

---

## 二、差异清单

### 差异 1：fullReset 锚定机制 — 相对 CUU vs 绝对 clearTerminal（核心差异，用户投诉根因）

| 项 | 内容 |
|---|---|
| 两侧 文件:行号 | 基准 `log-update.ts:503-513`（fullReset = clearTerminal + 整帧）+ `clearTerminal.ts:59-69`（2J+3J+H）；C# `InlineComposer.cs:118-125`（FullReset 相对 MoveTo + 2J）+ `InlineComposer.cs:176-210`（MoveTo 纯相对量）+ `ReplShell.cs:4103-4110`（_cursor 信念跨帧传递） |
| 差异描述 | 基准 fullReset 发 `2J+3J+光标归位H`：**绝对锚定** — 清视口+清 scrollback+光标物理归 (0,0)，与 reflow 无关，然后整帧从内容行 0 重绘。C# FullReset 先 `top = max(0, next.Rows - viewportRows)`，再从 `prevCursor`（上一帧遗留的**内容坐标信念**）做**相对 CUU/LF** 移动 + 仅 `2J`（无 3J、无光标归位）— resize 后终端 reflow 已把物理光标挪到信念之外的位置，CUU 落点失同步，重绘从错误物理行开始 |
| 影响面 | 缩窗（内容 rewrap 增长）后可见区顶部残留空白/错位，banner 不钉在视口顶 — **探针实测：120×30→80×24 后 banner 从第 0 行掉到第 4 行**；且是否错位取决于 reflow 是否恰好破坏对齐（探针相位 2/3 侥幸归位）— 即用户说的"没有永远保证最顶部"。CUU 还受视口顶钳制，落点被钳制截断时错位加剧 |
| 原始素材引用 | 基准 `log-update.ts:187-188`（"we have to use purely relative operations … since we don't know its starting point" — 基准深知相对量局限，所以 reset 场景放弃相对量改用 clearTerminal 绝对锚）；`clearTerminal.ts:55-57`（"Returns the ANSI escape sequence to clear the terminal including scrollback"） |
| 独立验证来源 | 探针 demo_seq 相位 1 网格（顶部 4 行空白 + banner 在第 4 行）；pyte 重放 stream.bin 可复核 |

### 差异 2：fullReset 重绘范围 — 整帧重发 vs 仅底部视口行

| 项 | 内容 |
|---|---|
| 两侧 文件:行号 | 基准 `log-update.ts:509-512`（clearTerminal 后 `renderFrame` = 行 0..screen.height **整帧**）；C# `InlineComposer.cs:124`（`RenderSlice(builder, next, top, next.Rows, ...)` 仅底部 `viewportRows` 行） |
| 差异描述 | 基准 resize 后**整帧重发**（全部消息行重新输出，scrollback 由重绘自然重建，布局确定性对齐，代价是闪烁+全量字节 — 命名 `_CAUSES_FLICKER` 已承认）。C# 只重绘内容最后 `viewportRows` 行，scrollback 旧内容保留（`InlineComposer.cs:117` 注释"scrollback 历史保留不动"为刻意选择）— 增量小、无闪烁，但锚定失同步时整个可见区错位且无法自愈 |
| 影响面 | 长会话 resize：基准全量重发（大输出+闪烁但正确）；C# 轻量但错位（差异 1 的直接后果）。两方案是同一权衡的两端，基准作者在 `log-update.ts:139-141` 明确论证："resizing is a rare enough event" — 罕见事件选确定性优先 |
| 原始素材引用 | 基准 `log-update.ts:139-141`（resize 罕见论原文）；`fullResetSequence_CAUSES_FLICKER` 命名；C# `InlineComposer.cs:117`（保留 scrollback 注释） |
| 独立验证来源 | 基准 Windows Terminal 实跑 resize 布局恒对齐（可日常复验）；C# 探针 demo_seq 相位 1 错位网格 |

### 差异 3：resize 触发面 — 已对齐（无行动项，仅记录）

| 项 | 内容 |
|---|---|
| 两侧 文件:行号 | 基准 `log-update.ts:142-147`；C# `InlineComposer.cs:50-52`（cols 变 / scrollback 变 / shrink 超视口）+ `ReplShell.cs:4261-4267`（inline rows 变 → `_forceFullReset` → 0×0 空白前置帧走 FullReset 分支） |
| 差异描述 | 触发面基本对齐：宽度变化、高度收缩、高度增长（rows 变）都进 fullReset。C# 侧注释自述"对齐基准 log-update.ts:142-147 高度变化无条件 fullReset" — **触发面对齐，但 fullReset 的内部实现（差异 1/2）未对齐** |
| 影响面 | 无独立影响 — 问题全部由差异 1/2 承载 |
| 原始素材引用 | 双侧源码原文 |
| 独立验证来源 | 双侧源码逐行对照 |

---

## 三、探针证据（L3 resize_probe，2026-10-31）

目标：`NativeTui.Demo.exe`（SimulatedEngine 确定性场景 — ReplShell/InlineComposer/BannerItem 渲染链路与 jcc 同源）。序列 `120x30 → 80x24 → 140x40 → 90x20`，每相位 settle 1200ms。

| 相位 | 尺寸 | banner `jcc v` 行 | `❯` 行 | footer 行 | 判定 |
|---|---|---|---|---|---|
| 0 | 120×30 | 0 | 4 | 17 | ✅ 钉在顶部 |
| 1 | 80×24（**缩窗**） | **4** | 8 | 21 | ❌ **顶部 4 行空白，banner 被推下 — 用户投诉复现** |
| 2 | 140×40（放窗） | 0 | 4 | 17 | ✅（CUU 钳制侥幸归位） |
| 3 | 90×20（再缩） | 0 | 4 | 17 | ✅（帧对齐未被 reflow 破坏 — 侥幸） |

相位 1 网格（`.xxx/resize_probe/demo_seq/01_80x24.txt`，节选）：

```text
  0|
  1|
  2|
  3|
  4| ▐▛███▜▌   jcc v1.0.0          ← banner 应在第 0 行
  ...
 21|  ? 查看快捷键
```

错位机理（相位 1）：80 列下内容总高 ≤ 视口（top=0），app 信念光标在内容行 ~17，发 CUU 17；
但 conhost reflow 后物理光标在行 ~21 → CUU 17 落在行 4 → `2J` 擦视口后从行 4 起画内容行 0
（banner）→ 顶部 0-3 行成为无人重写的空白。基准同场景：`2J+3J+H` 物理归零后整帧重画，
banner 恒在第 0 行。

**边界观察（诚实报告）**：裸 ConPTY 下 `jcc.exe` 停在"引擎启动中…"（引擎桥接环境未起，
主动应答 DA1/DA2 亦不解锁）— 探针目标改用 Demo（渲染链路等价）。`jcc.exe` 在用户真实
终端（引擎可用）中的表现可用 `--target` 指向 jcc.exe 复测。

---

## 四、修复落地（方案 A 基准对齐，2026-10-31 用户决策后 TDD 执行）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `InlineComposer.FullReset` 改 `2J+3J+CUP(1;1)` 绝对锚定 + 整帧重绘（`InlineComposer.cs`） | resize 跨尺寸 / scrollback 变更 / 收缩超视口 / ctrl+l（`ReplShell.RenderAndFlush` + `ForceFullRepaint`） | ✅ | ✅ 探针物理验收（下表） |
| 旧语义断言翻转 + 整帧重绘红测试（`InlineComposerTests.cs`：AbsoluteAnchor 前缀 / scrollback 行重发 / 稳态帧禁 3J 守卫） | 单元回归面 | ✅ 11/11 | ✅ `dotnet test NativeTui.Tests` 2974/2974 |
| ReplShell 过时注释同步（`_blankPrev` / `ForceFullRepaint` 旧行为描述） | 文档一致性 | ✅ | ✅ 编译零警告 |

**TDD 记录**：🔴 红 — 4 断言失败，失败输出即旧实现失同步签名 `\r\x1b[9A\x1b[2J...`（相对 CUU +
仅 2J + 底部行重绘）；🟢 绿 — `FullReset` 绝对锚定后 InlineComposerTests 11/11；
🔵 重构 — FullReset 去视口参数（不再需要 top 计算）；回归 2974/2974 全绿。

**E2E 物理验收（resize_probe demo_fixed，与修复前 demo_seq 同序列同标记）**：

| 相位 | 尺寸 | 修复前 banner 行 | 修复后 banner 行 |
|---|---|---|---|
| 0 | 120×30 | 0 | 0 |
| 1 | 80×24（缩窗） | **4 ❌** | **0 ✅** |
| 2 | 140×40（放窗） | 0 | 0 |
| 3 | 90×20（再缩） | 0 | 0 |

四相位布局逐行一致（banner 0 / `❯` 4 / footer 17），相位 1 顶部 4 行空白消失，scrollback
由整帧重绘自然重建 — 与基准 clearTerminal.ts 语义对齐。

**真实 jcc.exe 验收（用户报告的原始目标二进制 — 修复后重编 Debug，联测 MockServer）**：

ConPTY 实测（探针 `--env JCC_ENDPOINT/...`）：启动屏 120×30 渲染后缩窗 80×24、放窗 140×40，
banner 恒第 0 行、布局干净（`.xxx/resize_probe/jcc_fixed/`）；对话进行中（用户消息已提交、
spinner 转动）连续双重缩窗 100×30→80×24→60×18，每相位整帧重锚第 0 行、无残留无错乱。

⚠️ **边界观察（诚实报告，非本次修复引入）**：assistant 回复未在观测窗口内上屏 — 引擎请求
已确认到达 MockServer（dump req_0000_20261030_184326），mock 首轮脚本为 bash 工具调用，
回复收尾/权限确认未走完即到窗口上限。联测链路完整走通属 ADR 0077 范畴，后续可单独排查。

## 五、本次建成资产

| 资产 | 位置 | 说明 |
|---|---|---|
| L3 resize 感知探针 | `native_tui/tool/screenshot/resize_probe/`（resize_probe.py + README.md） | ConPTY 多相位 resize + pyte 独立解释器网格转储 + 标记行量化 + PNG + 原始流留存 + `--env`（联测 MockServer） |
| 证据包 | `.xxx/resize_probe/demo_seq/`（修复前）+ `demo_fixed/`（修复后）+ `jcc_fixed/`（真实 jcc） | 各相位网格/PNG/原始流，可离线重放复核 |

<!-- 🤖 Auto Decision: 2026-10-31 -->
<!-- 决策: resize 错位按"基准对比差异"处理（列文档+一问一答），不直接修复 -->
<!-- 原因: C# InlineComposer.cs:117 "scrollback 历史保留不动"是有注释的刻意选择，属"功能相同实现不同"而非无争议 bug；AGENTS.md 基准对比纪律要求此类走用户决策 -->
<!-- 替代方案: 直接按方案 A 修复（若用户确认即执行 TDD 流程） -->
<!-- 验证: 探针 demo_seq 复现错位（banner 行 0→4），pyte 独立重放可复核 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-31 -->
<!-- 决策: 用户选定方案 A 后，fullReset 语义翻转处将编码旧语义的测试断言一并翻转（ChangeInScrollback_TriggersFullReset 的 DoesNotContain("AAA") → Contains） -->
<!-- 原因: 该断言是旧"scrollback 历史保留"语义的载体，与新基准对齐语义直接冲突；无后向兼容原则 -->
<!-- 替代方案: 保留旧测试另加新测试（两套断言互相矛盾，不可行） -->
<!-- 验证: InlineComposerTests 11/11 + NativeTui.Tests 2974/2974 + 探针 demo_fixed 四相位 banner 行 0 ✅ -->
