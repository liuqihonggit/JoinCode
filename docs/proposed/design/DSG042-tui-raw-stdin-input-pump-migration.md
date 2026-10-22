# DSG042 — TUI 输入泵 stdin 原始流迁移计划（S11 遗留#2）

> 状态：proposed（2026-10-23）
> 关联：TASK065 S11（ReadKey 通道 paste 重组状态机已可用 — 本计划是其退役路径）、ADR 0140（TUI 自渲染像素对齐）、ADR 0141（bridge 协议）
> 前置调研：2026-10-23 输入链现状实证（DrainInput/KeyPressParser/ConsoleKeyAdapter 逐文件）

## 1. 问题

native_tui 生产主循环的键盘输入走 `Console.ReadKey` + `ConsoleKeyAdapter`（Key 枚举归一路径），而完整 VT 解析器 `KeyPressParser` 仅被 Demo/测试消费——**为原始流设计的解析器从未在生产接线**。ReadKey 路径的先天限制：

| # | 限制 | 根因 | 实际影响 |
|---|------|------|---------|
| ① | VT 序列不可逆丢失 | ReadKey 归一 Key 枚举 + `KeyChar='\0'`（方向键等） | 修饰组合键不可区分（ctrl+shift+arrow 归并为 arrow）；kitty keyboard protocol 无法启用（增强键位报告需要原始流回写查询） |
| ② | bracketed paste 需重组状态机 | ReadKey 把 ESC[200~/201~ 拆散为逐 KeyInfo | S11 落地的 `TryCollectPaste` 状态机是补丁非根治（注释自认"ReadKey 会拆散 VT 序列"） |
| ③ | F 键/PgUp/PgDn 部分形态缺失 | ConsoleKey 枚举覆盖不全（修饰组合的 F5-F12 / shift+PgDn 等） | transcript 模式部分 less 键位（S5 控制器已支持但 ReadKey 侧喂不出） |
| ④ | 与基准架构不对齐 | 基准 ink = stdin raw 流 + readline 解析器 | 长期键位能力面与 Claude Code 漂移 |

## 2. 现状事实（调研实证）

**输入链现状**：
```
生产主循环（ReplShell.Run → 帧拍 50ms DrainInput）
  └ Console.KeyAvailable / Console.ReadKey(intercept)
      ├ ConsoleKeyAdapter.Of(KeyInfo) → KeyPress（Key 枚举归一 — 限制①③源头）
      ├ TryCollectPaste 状态机（S11 — ESC 后 KeyAvailable 连续判定重组 200~/201~ — 限制②补丁）
      └ _pendingKeys 回退队列（误撞组合塞回防丢键）
Demo 路径（NativeTui.Demo Program.cs）
  └ Console.OpenStandardInput 原始流 → KeyPressParser.TryParseNext → KeyPress  ✅ 全链已有
```

**KeyPressParser 能力面**（InputPasteParserTests/KeyPressParserTests 锁定）：VT CSI/SS3 序列、修饰组合（Ctrl/Meta/Shift 位）、方向键/Home/End/PgUp/PgDn/F 键、`\x1f` 归一 Ctrl+"_"、bracketed paste 整块（200~/201~）、跨 chunk 攒齐（无终止标记不消耗）——**解析器完备，缺的只是生产接线**。

**互斥事实**：`Console.ReadKey` 与 `Console.OpenStandardInput` 不可混用（ReadKey 独占控制台输入缓冲；切换是全有或全无）。

**阻塞事实**：Windows stdin 流同步 `Read` 阻塞无 `KeyAvailable` 等价物——原始流路径必须引入后台读线程（或 PeekNamedPipe P/Invoke），不能在主循环帧拍内同步轮询。

## 3. 方案 — 双通道开关渐进迁移（默认保稳）

**原则**：ReadKey 通道是现役稳定路径（14 键位接线全验证），原始流通道以配置开关并行落地、灰度验证后切默认——全有或全无的互斥切换不允许裸切。

### 抽象层（新）

```
IInputPump（native_tui/src/NativeTui.Core/repl/input/）
  ├ Drain(KeyPress 队列) : bool — 非阻塞排空已解析键（主循环帧拍消费）
  └ Init/Dispose（原始流启停 + raw 模式对称）
ReadKeyPump（现役逻辑收编：DrainInput 的 ReadKey 循环 + paste 重组状态机整体搬入）
RawStdinPump（新：后台读线程 + 字节流 UTF-8 解码 + KeyPressParser 全链 → Channel<KeyPress>）
```

主循环 `DrainInput` 改为消费 `_inputPump.Drain()`（分派链 HandleRawKeyPress 之后零改动——KeyPress 形态两通道一致）。

### 分批（三 Phase，每批可独立交付回滚）

| Phase | 内容 | 验收 | 默认通道 |
|-------|------|------|---------|
| **P1 抽象收编** | IInputPump + ReadKeyPump（现役逻辑搬入零语义变化）；ReplShell 构造注入（默认 ReadKeyPump） | 全量 1667 测试零破坏；帧拍不变 | ReadKey |
| **P2 RawStdinPump 落地** | 后台读线程（Thread + Console.OpenStandardInput 块读）→ UTF-8 解码器（宽字符分块容忍）→ KeyPressParser → Channel；`--raw-input` 配置开关（Host 层）；paste 重组状态机在 RawStdinPump 下天然不需要（整块直出） | 键位全表 E2E 探针（`--selftest-input` 新增：逐键注入断言 KeyPress 形态）；双通道切换幂等 | ReadKey（raw-input 显式开启） |
| **P3 切默认退役** | 双通道灰度全键位对齐验证（含 Windows Terminal/conhost 双终端实测）→ 默认切 RawStdinPump → TryCollectPaste 状态机 + ConsoleKeyAdapter 归一路径归档 `.xxx/` | 键位兼容矩阵（ReadKey 通道 14 键位 vs Raw 通道全集）逐项打勾 | RawStdin |

### P2 关键设计点（风险预置）

1. **后台读线程生命周期**：`Thread(IsBackground: true)` + `CancellationToken` 关闭令牌 + Dispose 时 `stdin.CanRead` 防死等（进程退出后台线程自动回收——对齐基准 ink unmount 语义）；禁 async（主循环同步线程模型，Task.Run 线程池线程失控）
2. **UTF-8 分块解码**：`Utf8Decoder` 状态机（不完整多字节序列跨 Read 块保留）——KeyPressParser 已有跨 chunk 攒齐先例（InputPasteParserTests 锁定），字节→char 层复用同思路
3. **raw 模式对称**：RawConsoleMode 现有（Disable/Dispose）不动——ReadKey 与原始流共用同一 raw 形态（Windows 已验证），差异仅在读取层
4. **resize 事件**：Windows 控制台 resize 走现有 TerminalSizeSource 轮询（50ms 拍内）不受影响——原始流不含 SIGWINCH 语义差异
5. **kitty protocol（P3 后置）**：原始流就绪后才可能启用（查询序列回写 stdin 需全链 VT）；本计划不含，登记 P4 池

## 4. 工作量与影响面

| 项 | 估算 |
|----|------|
| P1 抽象收编 | 小（搬移+注入，1 次编译验证） |
| P2 RawStdinPump | 中（读线程+解码器+开关+探针，核心难点是 Windows 流阻塞语义） |
| P3 切默认+退役 | 中（灰度矩阵实测 + 状态机/适配器归档） |

**影响文件**：repl/ReplShell.cs（DrainInput 消费面）、repl/input/（新 IInputPump/两实现）、apps/NativeTui.Host/Program.cs（开关注入）；ConsoleKeyAdapter/TryCollectPaste P3 归档。

**风险**：Windows conhost（非 Windows Terminal）的原始流行为差异——P2 探针必须双终端实测；失败回退=开关关（ReadKey 通道完整保留）。

## 5. 未决（实施时裁决）

1. P2 探针形态：`--selftest-input` 独立入口 vs 复用 `--selftest-commands` 扩展
2. RawStdinPump 的 Channel 容量与满策略（背压 vs 丢弃——基准 ink 无界，倾向有界 256 满等待防键风暴丢键）
3. P3 退役 ConsoleKeyAdapter 的时机（kitty protocol 是否先落地再退役——归档后 ReadKey 通道复活成本）

## 执行状态（2026-10-23）

| Phase | 状态 | 提交 |
|-------|------|------|
| P1 抽象收编 | ✅ 完成 — IInputPump + ReadKeyPump 搬入 + ReplShell 注入，1667 全绿零语义变化 | ce83f01e9 |
| P2 RawStdinPump | ✅ 完成 — 全链（读线程/UTF-8 增量解码/解析/有界通道 256/非阻塞续投）+ 13 单测 + --raw-input 开关 + Host Dispose 收尾 | ce83f01e9 + 本批 |
| P3 切默认退役 | ⬜ 未做 — 双终端实测矩阵（Windows Terminal/conhost）跑齐后切默认归档 ReadKey 状态机 | — |

P2 实施中修的三个真 bug（用户点报自死锁后熔断定位）：
1. 同线程自死锁 — FeedForTest 同步投递 + WriteAsync 满等待 + Drain 同线程互等（改纯非阻塞 TryWrite + 回退）
2. DropWrite 静默吞键 — FullMode 语义与"满即停"相反，300 键丢 44（改 Wait）
3. 解析消耗未投递丢键 — TryParseNext ref 推进后 TryWrite 失败需回退 span（before 快照重赋值）

熔断机制：测试收集循环 2s 超时 fail-fast 带诊断（已收数/pending 残留）— 本批两真 bug 均由它定位，留为常驻设施。
