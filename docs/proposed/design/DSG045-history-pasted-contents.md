# DSG045 — 历史条目携带 pastedContents（8.19 闭环）

> 状态：accepted（两批落地 2026-10-23 — Core 2495 + Host.Tests 125 + selftest EXIT=0 全绿）
> 来源：TASK067 差异 8.19（批次 9 登记"待专项 spec — 推翻 C2 锁定设计 + HistoryRing 结构升级"）
> 基准：useArrowKeyHistory.tsx:63-197（HistoryEntry.pastedContents 随导航恢复）、history.ts:81-100/265-279（历史条目携带+提交展开）、handlePromptSubmit.ts:216（expandPastedTextRefs 用恢复后登记展开）

## 一、问题

历史翻回含折叠引用的条目再提交时，`[Pasted text #N]` 字面占位符发给引擎——
登记簿随首次提交 `Clear()` 清空，历史只存折叠形态文本（ReplShell.Submit:3018-3032），
`ExpandedText` 查不到登记回退原文占位符。用户可见内容突变：粘贴原文 → 历史往返 → 占位符。

C2 曾按既有测试注释（`Submit_PasteReference_SendsExpandedTextToEngine` 锁定"历史条目为折叠
字面量不可再展开"为有意设计）降级差异项；本专项经用户决策对齐基准（批次 7 同源决策
"全部对齐基准"方向），**推翻该锁定**。

## 二、方案（沿基准 HistoryEntry 携带语义，本地组件化落点）

### 组件改造

| 组件 | 改造 |
|------|------|
| HistoryRing | 条目 `string` → `HistoryEntry(string Text, IReadOnlyDictionary<int,string>? PastedContents)`；`Add(text, pasted?)` 重载（旧签名兼容 = null）；`TryUp/TryDown` 返回 entry；测试断言面 `Entries` 同步升级 + 兼容投影 `EntryTexts` |
| PastedContentTracker | 新增 `Snapshot`（当前登记只读投影）+ `LoadPasted(contents)`（整体替换注入：清当前 → 按 id 注入 → `_nextId = max(_nextId, maxId+1)` — 7.8 只增语义兼容） |
| InputBuffer | 新增 `ReplaceWithPasted(text, pasted?, cursorToStart)`（替换文本 + LoadPasted 注入）；提交快照在 `Clear()` 前由调用方取 |
| ReplShell | Submit：`_input.PastedSnapshot`（Clear 前）→ `_history.Add(text, snapshot)`；NavigateHistory：恢复改 `ReplaceWithPasted`（登记随条目回放） |

### 语义闭环

提交（折叠文本 + 登记快照入史）→ 翻回（文本 + 登记同时恢复到 tracker）→ 再提交
（ExpandedText 用恢复后登记展开为原文发引擎）→ 上屏用户消息仍折叠形态。

### 批次

| 批 | 内容 | 前置 |
|----|------|------|
| 1 | HistoryEntry 结构 + 提交快照入史 + 导航恢复注入 + 展开闭环 + 推翻 C2 测试 | 无 |
| 2 | undo 快照携带 pastedContents（基准 useInputBuffer 快照含登记 — UndoSnapshot 加状态）| 批1 |

## 三、明确不在范围

- 历史条目 mode 携带（bash 模式态 — 输入模式体系属独立差异域）
- 历史磁盘持久化 pastedContents 外存（8.17 已判定 transcript 派生等价实现）
- tracker 多实例竞态（单线程主循环消费，无并发面）

## 四、验收

| 项 | 方式 |
|----|------|
| 单测 | 批1 ≥6 用例（入史快照/恢复注入/再提交展开/无粘贴兼容/编号衔接/撤史兼容）；批2 ≥3 |
| 回归 | Core 全量 + Host.Tests + selftest EXIT=0 |
| 手动 | ADR 0080：粘贴大块 → 提交 → ↑ 翻回 → 再提交 → 引擎收到原文非占位符 |

## 六、验收记录（2026-10-23）

| 批 | 内容 | 提交 | 验证 |
|----|------|------|------|
| 1 | HistoryEntry 结构 + 提交快照入史 + 导航恢复注入 + 展开闭环 + C2 推翻 | 本提交 | HistoryRingTests 2 新 + 闭环 2 + 全量 2495 绿 |
| 2 | UndoSnapshot 携带登记（undo 回退登记同步回退） | 本提交 | Undo_PasteSnapshot 1 用例 |

实施定案：Snapshot 返回 **ToDictionary 副本**（引用共享会连历史条目一起清空 — Clear 穿透坑）；
Paste 显式先 PushUndoSnapshot 再 RegisterPaste（登记先于 InsertText 的 Push 会污染快照 — 顺序坑）；
恢复后新粘贴拼接顺序 second+first（Up 恢复光标置行首 — 基准历史导航光标语义的固有行为）。

## 五、决策记录

- 恢复 = 整体替换 tracker 登记（对齐基准恢复态直接替换语义 — 不叠加旧登记防撞号）
- 编号衔接取 max 而非覆盖（7.8 会话级只增语义不回退）
- C2 锁定测试推翻（用户批次 7 决策方向：产品行为向基准对齐优先于历史锁定注释）
