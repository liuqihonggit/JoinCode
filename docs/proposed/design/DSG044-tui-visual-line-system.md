# DSG044 — TUI 视觉行体系（grapheme 宽度 + 输入软折行 + 视觉行光标）

> 状态：accepted（四批 L1-L4 全部落地，2026-10-23）
> 来源：TASK067 差异文档 6.3b / 6.4 / 7.5 / 7.6 / 8.13 / 8.14 六项联动（渲染链 + 编辑器 + 键语义三域）
> 基准：ink/stringWidth.ts（grapheme 分段 + eastAsianWidth）、utils/Cursor.ts（grapheme 偏移 + 视觉行三级移动）、hooks/useTextInput.ts:269-316（wrapped→logical→history）

## 验收记录（2026-10-23）

| 批次 | 内容 | 落点 | 提交 | 验证 |
|------|------|------|------|------|
| L1 | GraphemeSegmenter（UAX29 核心子集）+ RuneWidth.WidthOfGrapheme | grid/GraphemeSegmenter.cs + RuneWidth.cs | 810ac17c2 | 分段 9 用例 + 宽度 1 用例全绿 |
| L2 | InputBuffer.Left/Right/Backspace grapheme 边界化 | InputBuffer.cs（Previous/NextGraphemeStart） | 576da6780 | 编辑器域 75/75 绿 |
| L3 | InputLineLayout 软折行投影 + PromptInputBox 视觉行渲染 + ReplView 高度/光标投影（HeightForText/TailHeightOf/InputCursorPos） | InputLineLayout.cs + PromptInputBox.cs + ReplView.cs | 9abc0b0cd | 折行 8 + 渲染 26 + ReplView 级 2 全绿 |
| L4 | TryMoveVisualVertically 三级回退 + HomeVisual/EndVisual + ReplShell 键分派接线 | InputBuffer.cs + ReplShell.cs | 7c5faaf32 | InputBuffer 7 + 分派 4 全绿 |

终态回归：Core **2327** + Host.Tests **125** + Host --selftest EXIT=0 全绿。
手动验收（ADR 0080 真机项）：粘贴 ZWJ emoji 列不错位 / 200 字符长输入全可见可编辑 / 折行内方向键细移 — 待用户真机执行。

## 实施决策补充（落地时定案）

- 折行宽统一 `cols - PromptWidth`（保守 — 视觉行续行略窄于可用宽不溢出；基准 wrap-ansi columns-1 同思路）
- 软折行断点固有同位语义：满宽硬切/词折行断点处"上一视觉行尾 = 下一视觉行首"同 char 位 — Home 连按在该处位置不变（真实跳转发生在跨逻辑行边界）；LocateVisualCursor 行尾边界归属下一视觉行（末视觉行除外）
- LocateVisualCursor 归属规则：`clamped < end || (clamped == end && 末视觉行)` — 行尾光标物理在下侧行首
- ConsoleTerminalSizeSource 无控制台防御回退 80×24（L4 按键路径读 cols 暴露 — 对齐 KeyAvailable 无控制台防御先例）
- TailHeight 属性改 TailHeightOf(cols) 方法（视觉行高度依赖终端宽 — WriteExit 消费传 sizeSource 当前 cols）

## 一、问题（用户可感知缺陷）

| # | 症状 | 根因 | 基准行为 |
|---|------|------|---------|
| P1 | ZWJ emoji（👨‍👩‍👧 家庭组合）输入/渲染时光标列错位、后续文本错列 | RuneWidth 按 rune 逐个计宽（ZWJ 序列成员各自计宽 = 6 列），无 grapheme 分段 | stringWidth.ts grapheme 整块计 2 列 |
| P2 | 光标可停在代理对中间（emoji 需按两次方向键、中间态渲染异常） | InputBuffer.CursorCol 是 UTF-16 char 偏移 | Cursor.ts grapheme 边界偏移（prevOffset/nextOffset 跳边界） |
| P3 | 长输入超屏宽时尾部静默截断不可见、光标超宽不画 | PromptInputBox WriteText 右界截断不折行 | useTextInput Cursor.fromText(value, columns) 软折行 + 光标恒可见 |
| P4 | 折行处按 Up 一次跳整段逻辑行（或触发历史），无法折行内细移 | InputBuffer 仅逻辑行垂直移动（CursorLineIndex） | 视觉行→逻辑行→历史三级回退 |
| P5 | 折行中 Home/End 跳逻辑行首尾（而非视觉行首尾） | 同上 | Cursor.ts:426-442 视觉行首 + 行首连按上跳一行 |

## 二、分层设计

### L1 grapheme 基础设施（纯函数层，零接线风险）

- **GraphemeSegmenter**：`native_tui/src/NativeTui.Core/grid/GraphemeSegmenter.cs` —
  UAX #29 分段（`StringInfo.GetTextElements` 不适合热路径逐次分配；自研枚举器：
  区域指示符对（RI pair）合并、ZWJ 链合并、Extend/Variation 附加吸收；0-GC 目标
  用 `Span` 切片枚举 `EnumerateGraphemes(ReadOnlySpan<char>, out Range range)`）
- **RuneWidth grapheme 入口**：`WidthOfGrapheme(ReadOnlySpan<char> grapheme)` —
  首 rune 查 EAW 表（代理对解 UTF-32）+ ZWJ 链特殊规则（`\u200D` 后 emoji 成员不再独立计宽，
  整 cluster 恒 2 列 — 对齐基准 stringWidth 语义）；RI 对（旗帜）2 列
- 单测：ZWJ 家庭序列 2 列、旗帜 2 列、肤色修饰（单 emoji+VS16）2 列、单 CJK 2 列、ASCII 1 列

### L2 InputBuffer grapheme 光标模型

- `CursorCol`（char 偏移）语义升级为 grapheme 序号索引 + `GraphemeStartCol`（char 偏移缓存）：
  - `Left()/Right()` 按 grapheme 边界跳（跳过代理对/附加符整块）
  - `Insert/Backspace` 边界安全（退格删整 grapheme — P2 修复；对齐基准 deleteTokenBefore 更进一步
    的 chip 整删属 8.16 差异不在本 spec 范围）
- 测试面：emoji 输入一次方向键跨整字、退格一次删完整 👨‍👩‍👧、光标永不落代理对中间

### L3 输入区软折行投影（渲染层）

- **InputLineLayout**（新组件）：`Layout(string text, int cols) → VisualLine[]` —
  词边界贪心折行（复用 markdown 折行算法语义：空格分词 + 超宽硬切）+
  grapheme 宽度精确列计算（消费 L1）；`\n` 逻辑行边界保留
- **PromptInputBox** 改造：不折行→软折行多视觉行渲染（贴底滚动窗口保持 — 视觉行超
  视口高时滚动窗口跟光标）；光标行恒可见 + 光标列精确到 grapheme
- **ReplShell.InputCursorPos**：投影改造 — 逻辑光标位 → 视觉行/列 → frame 物理行
- 测试面：80 列输入 200 字符 → 3 视觉行全可见、光标在任意位置可见、CJK/emoji 混排列对齐

### L4 键语义升级（视觉行移动）

- `InputBuffer.MoveCursorVertically(±1)` 语义升级：视觉行移动（同逻辑行内折行间细移），
  首视觉行上移 → 上一逻辑行末视觉行 → 历史导航（三级回退对齐基准 useTextInput.ts:269-316）
- `Home/End`：视觉行首/尾 + 行首连按 Home 上跳一视觉行（基准 Cursor.ts:426-442）
- 测试面：折行内 Up 细移不触发历史、首视觉行 Up 跳上一逻辑行、Home 连按行为

## 三、批次划分（TDD 渐进）

| 批次 | 层 | 内容 | 前置 |
|------|----|------|------|
| 1 | L1 | GraphemeSegmenter + RuneWidth.WidthOfGrapheme + 单测 | 无 |
| 2 | L2 | InputBuffer grapheme 光标（P2） | 批1 |
| 3 | L3 | InputLineLayout + PromptInputBox 折行渲染（P3） | 批1 |
| 4 | L4 | 视觉行键语义（P4/P5） | 批3 |

每批：红测试 → 实现 → Core 全量回归 → 提交。

## 四、明确不在本 spec 范围（防蔓延）

- 折行是软换行（渲染层投影）— **不改变** InputBuffer 文本内容与提交语义（提交仍是原文）
- `softWrap` 位图/选择复制折行还原（基准 Screen SpacerHead）— 无选择复制功能前不引入
- chip 整块退格删除（8.16 差异）— 独立项
- markdown/权限对话框折行算法统一（6.5/6.6 差异）— 独立项
- 消息区渲染链 grapheme 化（仅输入区）— 消息区 Cell 网格 grapheme 改造风险大，登记后续

## 五、验收

| 项 | 方式 |
|----|------|
| 单测 | 每批红绿循环（L1 ≥15 用例 / L2 ≥8 / L3 ≥6 / L4 ≥6） |
| 回归 | Core 全量 + Host.Tests + selftest EXIT=0 |
| 手动 | ADR 0080 真机：粘贴 ZWJ emoji 输入列不错位、200 字符长输入全可见、折行内方向键细移 |

## 六、决策记录

- grapheme 分段自研（StringInfo 每次分配不适合 50ms 帧拍热路径）而非 BCL 封装 — AOT + 0-GC 约束
- 软折行走渲染投影而非文本插入 — 保持提交语义纯净（基准同款：折行是 Cursor.fromText 投影）
- 视觉行移动三级回退（视觉→逻辑→历史）— 对齐基准 useTextInput.ts:269-316 顺序
