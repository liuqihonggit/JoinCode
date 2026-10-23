# TASK067 — TUI 底层实现差异对比（C# native_tui vs ClaudeCode 基准）

> 状态：调研完成，bug 已直接修复（TDD），差异项待用户逐项决策（一问一答）
> 对比范围：C# 侧 `native_tui/`（TUI 层 + 输入/渲染底层），基准侧 `D:\project\claude-code-rust\claude-code-rev-main\src\`（TS 复刻版）
> 方法：8 个并行调研代理按功能域对比，每项含两侧 `文件:行号` 证据；判定纪律 = 拿不准归差异

## 判定结果汇总

| 类别 | 数量 | 处置 |
|------|------|------|
| C# 确认 bug（行为错误） | 12 项（初判 13，C2 降级差异） | 已直接修复（TDD 红绿循环），见下文"已修复 bug 清单" |
| 功能相同但实现不同 | 98 项 | 本文档差异清单，**待用户逐项决策是否修复** |
| 基准有 C# 缺失的功能 | 74 项 | 本文档缺失清单，随差异项一并决策 |

## 用户决策记录

**2026-10-23 第 1 问：高优先级缺失批次 → 四项全部立项**，已全部 TDD 落地：

| 批次 | 功能 | 落点 | 验收 |
|------|------|------|------|
| 批1 | F1-F12/Insert/Clear 功能键映射（三变体：SS3 OP-OS/~数字表/linux console [[A-E + rxvt Home/End） | KeyKind.cs + KeyPressParser.cs（BuildSimple/ParseSs3/[[A-E 分支） | KeyPressParserTests 26 用例 |
| 批2 | stdin 静默 gap 自愈（>5s 重断言 bracketed paste — tmux/ssh 重连场景） | TerminalModeReasserter.cs + ReplShell（HandleRawKeyPress 打点/RenderAndFlush 检测重发） | TerminalModeReasserterTests 5 用例 |
| 批3 | earlyInput 启动期输入捕获（引擎组装窗口期按键回填输入框） | EarlyInputBuffer.cs + Program.cs（raw mode 后捕获/Run 前回灌） | EarlyInputBufferTests 2 用例 + selftest |
| 批4 | 终端查询响应解析路由（8 类回复 + 查询 promise 路由器 + RawStdinPump 前置分流） | TerminalResponse.cs/Parser.cs/Querier.cs + RawStdinPump（querier 注入） | 18 用例；**主动发探测查询接入 TerminalCapabilities 为后续增量**（ReadKey 通道无原始流，response 分流仅 RawStdin 灰度通道可达 — 与 TASK066-B3 切默认衔接） |

**2026-10-23 第 2 问：下一批次 → 两批都做，先防御后体验**，已全部落地：

| 批次 | 功能 | 落点 | 验收 |
|------|------|------|------|
| 批5 | 异常路径防御：ESC 前缀 50ms 超时 flush（差异1.6）+ 粘贴流中断 500ms 兜底产出部分内容（1.7）+ 退出 stdin 排空防泄漏（1.12） | KeyPressParser force 重载 + EscapeFlushPolicy + RawStdinPump 滞留计时接线 + ReadKeyPump 中断产出改造 + WriteExit 排空（无控制台防御） | EscapeFlushPolicyTests 6 + 泵超时 3 + force 4 用例 |
| 批6 | 渲染体验：事件驱动唤醒（差异6.1 — 有键/引擎事件时节拍等待提前结束，输入回显低延迟）+ EAW Wide BMP 区段全补（6.3a — ⌚⏩⚪⭐ 等 34 区段错列修复） | ReplShell._inputWake（DrainInput 返回值/DrainEngine 事件 Set/PaceTick 可中断等待）+ RuneWidth 表扩展 | RuneWidthTests 35 新用例；**6.3b grapheme 重构 + 6.4 输入行软折行为跨模块大改造（渲染链+编辑器+键语义三域联动视觉行体系），登记待 spec 后实施** |

---

## 一、已直接修复的 bug（13 项，无需决策）

| # | 症状 | C# 位置 | 根因 | 修复 |
|---|------|---------|------|------|
| A1 | `ESC[;5A`（空参数）抛 FormatException、`ESC[9999999999999999~`（超长数字）抛 OverflowException，击穿 RawStdinPump 读线程 catch 过滤器 → 读线程死亡/进程崩溃（违反 `RawStdinPump.cs:93-97` 自身注释契约"读线程不可死"） | KeyPressParser.cs:157,180-181 | 裸 `int.Parse` 无防御；基准 parseInt 永不抛 | `int.TryParse` + 失败降级 -1 |
| A2 | modifyOtherKeys 三段序列 `\x1b[27;2;13~` 碎片化泄漏：Unknown + 字符 `1`/`3`/`~` 三个文本键事件进输入框（违反 KeyPressParser.cs:8 自身防泄漏契约） | KeyPressParser.cs:164-196 | 数字扫描停在第二个 `;`，把 `;` 当 final | 泛化参数区扫描（含多段/intermediate），未知形态整体吞 |
| A3 | `ESC[?1$y` 等私有参数序列（DECRPM 响应/tmux 透传）拆包泄漏 `1$y` 为文本 | KeyPressParser.cs:138-161 | numEnd 仅扫数字，`?`/`$` 等参数字节未覆盖 | 同 A2 泛化扫描 |
| B1 | Alt+Enter（Meta+Return）误触发提交而非插入换行 — 解析层已正确产出 Meta+Return，分派层漏判 | ReplShell.cs:843-849 | 换行分支只判 `key.Shift`；基准 meta\|\|shift 均换行 | 换行分支加 `key.Meta` |
| B2 | 建议激活时 Shift+Up 被劫持为建议导航，消息光标导航（分派链 :783）不可达 | ReplShell.cs:1638-1642,1668-1671 | `case KeyKind.UpArrow` 不检查修饰符（兄弟分支均精确判定，唯建议分支漏检） | case 加裸键判定（排除 Shift/Ctrl/Meta 组合） |
| B3 | 输入 `/mo` 弹建议 → ctrl+o 进 transcript → Up/Tab/Enter/Esc 全被残留建议劫持，transcript 滚动/退出失效（破坏自身"整屏独占"注释契约 ReplShell.cs:676-677） | ReplShell.cs:739-748,966-969 | Toggle 进 transcript 不清建议器 | ctrl+o 进 transcript 时 Dismiss 建议器 |
| B4 | transcript 模式 ctrl+c 完全死键 — TranscriptController.cs:89 注释承诺"宿主继续分派 ctrl+c"，实现吞键 | ReplShell.cs:1217-1231, TranscriptController.cs:256-277 | HandleTranscriptKey 仅放行 ctrl+o | 放行 ctrl+c 兑现注释契约 |
| B5 | 接受 ghost 建议后再按 → 旧后缀再次追加（"help /actions" → "help /actionstions" 文本污染） | ReplShell.cs:1688-1693,654-668 | FillInput 置 `_programmaticFill` 跳过 UpdateSuggestions，状态残留 | 接受后强制刷新建议（清残留 ghost） |
| C1 | 粘贴折叠可见行数上限漏 `min(…,2)` — 24 行终端粘贴 3~14 行文本不折叠，原文直插挤爆输入区（C# 注释 PastedContentTracker.cs:21-22 自证意图是 min 形态） | ReplShell.cs:883 | 传参 `Math.Max(0, rows-10)` 漏上限；基准 `Math.min(rows-10, 2)` | 补 `Math.Min(…, 2)` |
| D1 | 内容大幅收缩且被清首行落入 scrollback 区时 ClearBottomRows 物理错位、后续帧渲染全乱（基准同场景走 fullReset，log-update.ts:259-271 有守卫+注释论证） | InlineComposer.cs:39-41,86-99 | 清行无"行是否物理可达"守卫 | 加 IsShrinkBeyondViewport 守卫走 FullReset |
| D2 | inline 模式终端高度（rows）变化后不重置不重绘，物理屏与内部帧失同步（基准高度收缩无条件 fullReset，log-update.ts:142-147） | ReplShell.cs:2316-2331, InlineComposer.cs:33 | EnsureScreens 只依赖 cols | EnsureScreens 加 rows 变化检测置 _forceFullReset |
| E1 | 崩溃/未捕获异常路径无终端状态恢复兜底 — 光标永久隐藏、bracketed paste 泄漏（CursorLifecycle.cs:8-9 注释声称"宿主全局异常兜底"，实际不存在） | Program.cs:14-37 | Main 仅 catch ConfigurationException | TerminalRestoreGuard 三钩子兜底（ProcessExit/UnhandledException/CancelKeyPress） |
| F1 | 帮助面板 Esc 文案"空输入时清空"与实际行为（有输入时清空）字面相反 | HelpPanel.cs:24 | 文案与代码不同步 | 修正文案 |

> **判定降级说明**：初判 13 项中 C2（历史恢复的粘贴输入提交后占位符字面量发引擎）经复核降级为差异项——既有测试 `ReplShellWiringInputTests.Submit_PasteReference_SendsExpandedTextToEngine` 以注释明确锁定"历史条目为折叠字面量不可再展开"为有意设计（虽与基准 pastedContents 随历史携带行为不一致），不满足"非有意设计"的 bug 判据，归差异清单 7.8/8.19 走用户决策。**实际直接修复 12 项。**

---

## 二、差异清单（功能相同但实现不同，待用户逐项决策）

> ⚠️ 按判定纪律：拿不准是否 bug 的一律在此。决策方式 = 一问一答逐项过。

### 域1：raw stdin 输入底层

| # | 功能点 | C# 实现 | 基准实现 | 差异描述 | 潜在影响 |
|---|--------|---------|---------|----------|---------|
| 1.1 | raw 模式进入/退出粒度 | 进程级一次性（RawConsoleMode.cs:20-46） | 组件级引用计数（App.tsx:216-293） | C# 全程独占 raw | 外部编辑器（ctrl+g）子进程期间终端仍 raw |
| 1.2 | Windows raw 模式位集 | 清 Processed/Line/Echo 三位（ConsoleModeMath.cs:31-32） | Node libuv 内部切换 | QuickEdit 等位保留策略无法逐位核对 | conhost 下行为可能有细微差异 |
| 1.3 | 随 raw 启停的输入增强模式 | 仅 bracketed paste（ReplShell.cs:388,2358） | +焦点上报+kitty+modifyOtherKeys（App.tsx:243-255） | C# 少 4 类 | 无法收焦点事件/扩展键修饰 |
| 1.4 | 退出/崩溃 raw 恢复兜底链 | 两层（using+WriteExit） | 多层（signal-exit/SIGINT/SIGHUP/gracefulShutdown） | 非主线程异常终止进程时 C# 终端残留 raw | 用户需手动 reset（E1 已补主进程兜底，信号级兜底仍缺） |
| 1.5 | stdin 读取模型 | 后台线程同步阻塞读 4096 块（RawStdinPump.cs:34-42） | 事件驱动 readable（App.tsx:359-374） | 线程模型 vs 事件循环 | C# 占一线程、跨 Channel 解耦 |
| 1.6 | 不完整转义序列 flush | chunk 边界近似（KeyPressParser.cs:101-105） | 50ms/500ms 定时器 flush（App.tsx:309-333） | 慢链路序列跨块时 C# 误插字符 | SSH/卡顿场景 |
| 1.7 | bracketed paste 不完整兜底 | 永不产出留 pending（KeyPressParser.cs:83-96） | 500ms PASTE_TIMEOUT 产出部分（parse-keypress.ts:287-291） | 终止标记丢失时 C# 输入静默失联 | 终端异常场景（代理判定分歧，归差异） |
| 1.8 | 输入背压 | 有界 Channel 256 键 + pending StringBuilder 无界（RawStdinPump.cs:17-19,144） | 同线程天然背压 | C# 消费卡顿时 pending 无界 + 每帧 ToString 全量复制 | GC 压力 |
| 1.9 | 消费触发节奏 | 50ms 帧拍 Drain | readable 即时分派 | C# 键事件延迟 ≤50ms | 快速输入可感知 |
| 1.10 | stdin 非 TTY 策略 | 硬拒绝退出（Program.cs:263-266） | 降级 /dev/tty 继续（StdinContext.ts:12-20） | C# 管道场景不可用 | 脚本驱动场景 |
| 1.11 | Ctrl+C 语义 | 复制最后 assistant 消息（TASK059 有意设计） | 打断+双击退出（defaultBindings.ts:40） | 产品决策差异 | 迁移用户肌肉记忆 |
| 1.12 | 退出后 stdin 排空 | 无 | drainStdin（ink.tsx:1664-1718） | 在途键事件泄漏到 shell | 低风险 |

### 域2：键序列解析

| # | 功能点 | C# 实现 | 基准实现 | 差异描述 | 潜在影响 |
|---|--------|---------|---------|----------|---------|
| 2.1 | 解析器架构 | 单遍 span 递归下降 | tokenizer 8 状态机 + 正则语义层 | C# 无独立切词层 | OSC/DCS 覆盖差（已随 A2/A3 部分收窄） |
| 2.2 | CR/LF 键名 | 统一 Return | return/enter 两键名 | 等价收敛 | 无 |
| 2.3 | 双 ESC（\x1b\x1b） | Escape+Meta | 两个独立 Escape | 修饰语义不同 | Alt+Esc 场景 |
| 2.4 | Escape 恒 meta | Meta=false | meta=true（ink 兼容怪癖） | 基准继承怪癖，C# 更合理 | 按 meta 过滤的消费方行为不同 |
| 2.5 | CSI 修饰位解码 | 3 位（Shift/Meta/Ctrl） | 4 位（+super） | C# 模型层无 Super 字段 | cmd/win 组合键不可达 |
| 2.6 | NUL（Ctrl+Space） | 丢弃（测试锁定） | ctrl+反引号怪癖 | 两侧行为都不理想 | Ctrl+Space 无事件 |
| 2.7 | 空粘贴事件 | 双通道不一致（RawStdin 产出/ReadKey 吞） | 恒产出 | C# 内部不一致 | Windows 下影响小 |
| 2.8 | ReadKey 通道 paste 重组 | KeyAvailable 连续性假设 | tokenizer 无此假设 | 慢链路粘贴可能泄漏 | SSH 场景 |
| 2.9 | ESC+控制字符（Meta+Tab 等） | 产出正确 Meta 组合键 | 泄漏为文本 | **C# 优于基准** | 无 |
| 2.10 | SGR 鼠标非法终点 | 泄漏 1 字节 | 整序列消费 | 畸形序列注入时 | 影响小 |
| 2.11 | 双输入通道并存 | ReadKeyPump（现役）+RawStdinPump（灰度） | 单一 stdin 流 | 同一序列两通道产出不同事件 | 维护双份语义 |

### 域3：kitty 键盘协议

| # | 功能点 | C# 实现 | 基准实现 | 差异描述 | 潜在影响 |
|---|--------|---------|---------|----------|---------|
| 3.1 | 协议全链路 | 完全未实现 | 探测→开启→解析→栈管理→退出 | 整条链路缺失 | ctrl+shift+字母不可区分（TASK066-B1 已在规划内） |
| 3.2 | G 跳末尾 kitty 双形态 | TranscriptController.cs:300 有分支但解析层不可达（死分支） | CSI u 真实可达 | 纸面对齐 | 协议接入前无用户影响 |
| 3.3 | 事件类型/关联文本 | 不支持 | 也不支持（flags=1 最小集） | 两侧一致 | 无 |

### 域4：键位配置系统

| # | 功能点 | C# 实现 | 基准实现 | 差异描述 | 潜在影响 |
|---|--------|---------|---------|----------|---------|
| 4.1 | 键位表示 | 强类型 KeyPress + 模式匹配 | 字符串语法+别名归一 | 无字符串配置层 | 无法承接外部键位输入 |
| 4.2 | 默认键位表 | ReplShell 分派链散布硬编码 | DEFAULT_BINDINGS 数据表 | 数据驱动 vs 代码 | C# 改键位需同步两处（分派+帮助文案） |
| 4.3 | Esc 忙时打断 | 500ms 双击 Esc | 单击直接打断 | C# 加了确认层 | 体验迟钝（M8-2 有意设计） |
| 4.4 | Esc 闲时清空 | 单击立即清空、不入史、清 undo 栈 | 双击+提示+入史可恢复 | **C# 有误触丢文本风险** | 数据丢失风险级 |
| 4.5 | ctrl+d 双击窗口 | 3000ms（注释声称对齐） | 800ms | 数值 3.75 倍 | 误触概率高 |
| 4.6 | chord 多键序列 | 仅 ctrl+x 前缀单变量、无超时 | 通用 chord 状态机+1s 超时 | ctrl+x 悬置无超时 | 离开再回来按 ctrl+e 误触发 |
| 4.7 | 键位查找/上下文 | 编译期 if 链 | 运行时 Set 上下文+绑定数组 | 固化 vs 动态 | 扩展成本 |
| 4.8 | HistorySearch Esc | 取消丢弃匹配 | 接受匹配填入 | 语义相反 | 用户习惯 |
| 4.9 | 权限对话框直选键 | 数字 1/2/3 | y/n 直选 | 键集不同 | 习惯差异 |
| 4.10 | 平台自适应键位 | 恒定 | Windows VT 模式退化分支 | 策略不同 | VT 灰度切换时依赖终端能力 |
| 4.11 | 消息导航首末键 | ctrl+↑/↓ | meta/super+↑/↓ + shift+k/j | 键位不同 | kitty 终端肌肉记忆失效 |
| 4.12 | ctrl+n/p | 全局滚动 ±3 行（自加行为） | Select 上下文导航 | C# 多出滚动 | 不破坏对齐 |
| 4.13 | 键位显示文案 | 静态硬编码（HelpPanel） | 动态反查绑定表 | 改键位文案自动跟随 vs 手工 | 漂移风险 |

### 域5：终端模式与 ANSI 控制

| # | 功能点 | C# 实现 | 基准实现 | 差异描述 | 潜在影响 |
|---|--------|---------|---------|----------|---------|
| 5.1 | alternate screen | 枚举定义零消费，transcript 主屏重绘模拟 | AlternateScreen 组件+编辑器 handoff | 主屏重绘失败不可恢复 | transcript 崩溃残留 |
| 5.2 | CUP 写法 | 1;1 省略参数优化 | 恒发全参数 | 字节级差异 | 无 |
| 5.3 | 尺寸变化检测 | 50ms 轮询+夹紧 20×8 | resize 事件同步 | 延迟一拍 vs 零延迟 | C# 极窄终端有保底 |
| 5.4 | DEC 2026 包裹条件 | 不支持终端一律不包裹 | 仅 alt 屏不支持才跳过 | tmux 主屏包裹策略不同 | 理论闪跳风险一致 |
| 5.5 | 退出清理 | 清屏（主屏可见区被清） | 保留最后一帧+stdin 排空+OSC 清理 | 体验取舍 | C# 退出后终端干净但历史被清 |
| 5.6 | 孤立 ESC 超时 | chunk 边界近似 | 50ms 定时器 | 同 1.6 | 同 1.6 |
| 5.7 | DECSTBM 硬件滚动 | 序列构建器零消费，全量差分 | setScrollRegion+scrollUp 硬件滚动 | 大幅滚动输出量差数量级 | 性能 |
| 5.8 | resize 模式重断言 | 无 | alt 屏鼠标重断言+防闪跳 | tmux 重连自愈缺失 | 重连后模式静默失效 |
| 5.9 | Ctrl+Z 挂起/恢复 | 无 | 完整状态交接 | Windows 主平台可辩护 | Unix 里程碑需补 |

### 域6：渲染管线

| # | 功能点 | C# 实现 | 基准实现 | 差异描述 | 潜在影响 |
|---|--------|---------|---------|----------|---------|
| 6.1 | 渲染触发 | 固定 50ms tick 轮询 | 事件驱动+16ms 节流 | 20fps vs 60fps 上限 | 按键回显延迟 50ms；空闲空转 CPU |
| 6.2 | 增量渲染 | 每帧全量重绘+全屏 diff | damage 区域 diff+行缓存 | O(全屏) vs O(变化) | 大终端长会话 CPU 恒定开销 |
| 6.3 | 宽字符宽度 | rune 级手写区间表（缺部分 EAW Wide 区段） | grapheme 分段+eastAsianWidth 库 | ZWJ emoji 错列、⌚ 等 EAW Wide 判 1 列 | 光标/文本错位 |
| 6.4 | 输入行溢出 | 右界截断不折行、光标超宽不画 | 按列软折行+光标恒可见 | 长输入尾部不可见 | 可感知 |
| 6.5 | 截断统一入口 | 各组件分散实现 | wrapText 统一入口（4 模式+省略号） | 无 truncate-start/middle | 一致性漂移风险 |
| 6.6 | 折行算法 | 自研贪心（markdown/权限两套） | wrap-ansi 包 | 断点位置边缘差异 | 行数差 1 |
| 6.7 | diff 段光标移动 | LF 向下（依赖布局不变式） | CUD 相对移动 | C# LF 有滚屏副作用（当前恰好安全） | 未来布局调整踩坑 |
| 6.8 | patch 优化器 | 无 | 合并 cursorMove/去 no-op | 冗余光标序列字节 | 输出略多 |
| 6.9 | stdout 背压 | 同步阻塞写 | 异步高水位 | 平台本性 | 可接受 |
| 6.10 | 双缓冲污染标记 | 无 overlay 路径（当前无风险） | prevFrameContaminated 防 blit 脏数据 | 未来加 overlay 会踩坑 | 前瞻 |
| 6.11 | 动画时钟 | 每拍固定 +50ms 与真实时间脱钩 | rAF 真实墙钟 | 长帧动画相位漂移 | 视觉细节 |
| 6.12 | 高亮实现 | ScrollBox 渲染内嵌每帧全量重算 | onRender 独立 overlay pass | 架构位置 | 区域大时开销 |
| 6.13 | 视口空帧输出 | 恒发光标定位（~8 字节/帧） | 零字节 | 恒定背景流量 | 无害 |
| 6.14 | 全量重绘 | 2J 不清 scrollback（视口模式） | 2J+3J+H | 视口模式残留旧帧滚入历史 | 已知差异 |
| 6.15 | 滚动 drain 速率曲线 | ScrollBy 立即生效 | 4ms 比例 drain 平滑 | 中间帧缺失 | 快速滚动视觉 |

### 域7：粘贴与多行输入

| # | 功能点 | C# 实现 | 基准实现 | 差异描述 | 潜在影响 |
|---|--------|---------|---------|----------|---------|
| 7.1 | 粘贴标记检测 | 两通道两套实现 | 统一状态机 | 内部一致性面 | 双通道需各自对齐 |
| 7.2 | 粘贴中断兜底 | ReadKey 整块吞/RawStdin 永久攒 | 500ms 超时产出部分内容 | 慢速终端大粘贴丢失 | 见 1.7 |
| 7.3 | CRLF 归一 | \r\n 折叠单个 \n | \r 全换 \n（CRLF 翻倍） | **C# 更合理但偏离基准** | Windows 粘贴行数差一倍 |
| 7.4 | 粘贴 ANSI 清洗范围 | 仅 CSI/SS3/ESC+单字符（OSC 残留） | strip-ansi 全类别 | OSC 文本残留垃圾字符 | 终端标题/超链接粘贴 |
| 7.5 | 上下键跨行移动 | 仅逻辑行 | 视觉行→逻辑行→历史三级 | 折行内细移缺失 | 长折行跳整段 |
| 7.6 | Home/End 语义 | 逻辑行首尾 | 视觉行首尾+行首连按上跳 | 手感差异 | 折行场景 |
| 7.7 | 空输入粘贴 !cmd | 提交时才切 bash 模式 | 粘贴即时切模式+剥前缀显示 | 显示形态差异 | 最终语义一致 |
| 7.8 | 粘贴编号生命周期 | 提交后重置回 1 | 会话内只增不复用 | 防撞号设计缺失 | C2 修复后撞号场景已收窄 |
| 7.9 | Pasting… 进行中 UI | 无 | footer 提示 | 反馈缺失 | 大粘贴体验 |
| 7.10 | 无 bracketed paste 大输入 | \r 逐行触发提交 | >800 字符按粘贴折叠+多行插入 | 不支持 DEC 2004 终端粘贴多行会逐行提交 | 破坏性（老终端） |
| 7.11 | backslash+Enter 续行 | 无 | bash 风格换行 | 缺失 | 习惯差异 |
| 7.12 | 粘贴高亮显示 | 纯文本 | ShimmeredInput 高亮 | 形态差异 | 无 |

### 域8：REPL 键分派模态 + 输入编辑器与历史

| # | 功能点 | C# 实现 | 基准实现 | 差异描述 | 潜在影响 |
|---|--------|---------|---------|----------|---------|
| 8.1 | 模态优先级链组织 | 硬编码 if 链 | 声明式三层+overlay 注册表 | 等价顺序、不同架构 | 扩展成本 |
| 8.2 | 权限对话框键集 | 缺 j/k/ctrl+n/p/翻页/多位数字 | Select 全套 | vim 键位/翻页缺失 | 习惯差异 |
| 8.3 | 模型选择器键集 | 仅 Esc/Enter/Up/Down | +j/k/数字/翻页/effort 循环 left/right | 键集窄 | 同上 |
| 8.4 | 光标态 c/p 复制后保持 | 保持导航态（注释误导） | 复制即退出 | C# 连续复制更方便但注释与基准相反 | 注释级矛盾 |
| 8.5 | 光标态 Esc 收起范围 | 收全部展开 | 仅收当前项 | 全部 vs 当前 | 多组展开时 |
| 8.6 | Up 键弹出可编辑排队 | 无（仅 Esc 弹回） | Up 先 popAllCommandsFromQueue | 键位路径差异 | 忙时编辑排队 |
| 8.7 | 排队上限 | 10 条守卫 | 无上限 | C# 防御增强 | 无 |
| 8.8 | 帮助面板触发 | 空闲+空输入+'?'，忙时不可开 | 任何时刻输入 '?' 即 toggle | 忙时无法看帮助 | 体验 |
| 8.9 | Tab 无建议时 | no-op | 接受 promptSuggestion/显示 thinking 提示 | 引导反馈缺失 | 教学 |
| 8.10 | 建议导航 ctrl+n/p | 无 | 有 | emacs 风格缺失 | 长列表滚动 |
| 8.11 | 后台任务导航形态 | 内嵌选中态 | 独立 BackgroundTasksDialog | 交互形态不同 | 无 teammate 语义 |
| 8.12 | 搜索态 Up/Down | 匹配间导航（C# 多做） | 无绑定 | 功能增强型差异 | 无 |
| 8.13 | 文本缓冲表示 | StringBuilder+UTF-16 char 偏移 | string+grapheme 偏移+NFC 归一 | 光标可停代理对中间 | emoji 编辑异常 |
| 8.14 | 词移动算法 | 自研近似 | Intl.Segmenter 引擎级 | 边界行为差异 | 下划线/组合符 |
| 8.15 | Ctrl+U/K 跨行 | 行内删除 | 连按跨行清空 | 多行编辑效率 | 可感知 |
| 8.16 | Backspace 整块 token 删除 | 逐字符 | chip 一次整删 | 折叠引用退格 N 次 | 可感知 |
| 8.17 | 历史存储 | 内存 List 环 | history.jsonl 磁盘持久化+项目过滤 | 无跨会话/跨项目历史 | 功能级差异 |
| 8.18 | 提交入史去重 | 与末条相同跳过 | 直接 push | C# 多了去重 | 重复提交行为不同 |
| 8.19 | 历史草稿暂存内容 | 纯文本 | +mode+pastedContents | 恢复丢模式 | C2 已收窄 |
| 8.20 | undo 栈 | 逐键快照容量 100 | 防抖 1s+容量 50+索引回退 | 回退粒度 | C# 逐字符回退繁琐 |
| 8.21 | ghost 触发条件 | 不看光标位置 | 依赖 cursorOffset | 光标中部时 ghost 仍挂行尾 | 显示语义 |
| 8.22 | ghost 接受键 | 仅 → 且光标在末尾、无尾随空格 | Tab+→、中部替换+尾随空格 | 接受算法不同 | 输入效率 |
| 8.23 | Ctrl+R 大小写 | OrdinalIgnoreCase | 区分大小写 | 大写查询匹配面不同 | 搜索行为 |
| 8.24 | 历史撤销匹配键 | 按文本相等 | 按引用/时间戳 one-shot | 相同文本重复提交时误撤 | 边缘 |
| 8.25 | @ 文件建议触发 | token 内任意 @ | 正则行首/空白后 | user@h 误弹建议 | 误触发 |
| 8.26 | @ 文件索引 | 自枚举+硬编码忽略+TTL 30s | git ls-files+gitignore+nucleo 索引 | gitignore 化文件被建议/新鲜度 | 建议质量 |
| 8.27 | 命令建议排序 | 六级 Tier+长度 | Fuse.js 模糊+频次 | 顺序可能不同 | 建议质量 |

---

## 三、基准有 C# 缺失的功能（随差异项一并决策）

按用户价值排序（调研代理共识）：

| 优先级 | 功能 | 基准位置 | 说明 |
|--------|------|---------|------|
| 高 | earlyInput 启动期输入捕获 | utils/earlyInput.ts:29-67 | 引擎启动期按键缓冲回填 |
| 高 | kitty keyboard 协议全链 | ink/csi.ts:301, parse-keypress.ts:630-652 | TASK066-B1 已在规划内 |
| 高 | stdin 长静默 gap 检测+模式重断言 | App.tsx:30-35,359-368 | tmux/ssh 重连自愈 |
| 高 | F1-F12/Insert/Clear 功能键映射 | parse-keypress.ts:304-406 | 功能键不可用 |
| 高 | 终端查询响应解析路由（8 类） | parse-keypress.ts:33-175 | 探测链路基础设施 |
| 中 | 用户自定义键位全链路（keybindings.json） | keybindings/loadUserBindings.ts | 配置化键位 |
| 中 | vim 编辑模式 | src/vim/ 全套 | INSERT/NORMAL |
| 中 | 鼠标事件（点击/拖拽/滚轮/悬停） | parse-keypress.ts:594-609 | 滚轮滚动能力 |
| 中 | alt-screen 模式（DEC 1049） | AlternateScreen.tsx | transcript 隔离 |
| 中 | 焦点事件（DEC 1004） | App.tsx:498-519 | 降速/通知抑制 |
| 中 | 图片粘贴全套 | imagePaste.ts:96-242 | ctrl+v/alt+v |
| 中 | 消息选择器 rewind（双击 Esc 进入） | PromptInput.tsx:1254 | 回滚编辑 |
| 中 | 历史条目携带 pastedContents | history.ts:81-100 | 随 8.19 |
| 中 | DECSTBM 硬件滚动 | log-update.ts:149-185 | 滚动性能 |
| 中 | 输入超 10000 字符截断接线 | useMaybeTruncateInput.ts:25-57 | **C# 已实现算法但未接线**（PastedContentTracker.TruncateIfTooLong 无调用点） |
| 中 | 无 bracketed paste 多行粘贴支持 | useTextInput.ts:390-407 | 老终端兼容 |
| 中 | transcript ctrl+e 全展开 / `[` dump / `v` 编辑器 | defaultBindings.ts:163, REPL.tsx:4332-4383 | 阅读增强 |
| 中 | rxvt/putty 修饰键变体 + numpad 应用模式 | parse-keypress.ts:379-454 | 键域覆盖 |
| 低 | OSC 8 超链接 / softWrap 位图 / 文本选择 overlay / 搜索高亮 overlay | ink 各处 | 鼠标相关前置 |
| 低 | 声明式光标停靠（IME/a11y） | ink.tsx:166-179 | IME 预编辑内联 |
| 低 | promptSuggestion 空输入 AI 建议 / bash 历史 ghost | useTypeahead.tsx:1296 | ghost 扩展 |
| 低 | 输入模式体系（! bash 模式态） | inputModes.ts | 随 8.19 |
| 低 | ctrl+t todos / meta+p/o/t / ctrl+b 后台化 / ctrl+x ctrl+k 杀 agents 等 | defaultBindings.ts 各段 | 功能入口键位 |
| 低 | 老终端 wcwidth 补偿 / 宽字符视口边界写保护 / 池定期重置 | log-update.ts:723-750 等 | 边缘防御 |

---

## 五、差异决策后续批次（第 4 问之后）

**2026-10-23 第 4 问：产品语义类 6 项 → 全部对齐基准**，已落地（提交 b5746754f）：

| 差异项 | 对齐内容 | 验证 |
|--------|---------|------|
| 4.3 Esc 忙时打断 | 单击立即 Interrupt（500ms 双击确认层移除） | ExitSafetyTests 单击打断/孤立两击累加 |
| 4.4 Esc 闲时清空 | 双击（800ms）清空 + 草稿入史可翻回 + 首击提示通知（防误触丢文本） | 单击保留/双击清空+入史 |
| 4.5 双击窗口 | ctrl+d 与 Esc 统一 800ms（原 3000/500） | 常量对齐 |
| 1.11 ctrl+c | 忙时打断 + 闲时双击退出（复制语义移除 — 走消息光标态 c 键既有通道） | 忙打断/双击退出/transcript 穿透打断 |
| 4.8 搜索 Esc | 接受当前匹配填入输入框（取消改走 ctrl+c） | CtrlR_Escape_AcceptsMatch |
| 4.9 权限直选 | y/n 单键直选（y=Allow/n=Deny，数字 1/2/3 保留并存） | Permission_Y/N |

批次 10/10b（第 6 问"专项 spec + 小项清尾"）已落地（2026-10-23）：

| 差异项 | 内容 | 判定 |
|--------|------|------|
| 8.19 历史携带 pastedContents | **DSG045 专项两批**：HistoryEntry 二元组 + 导航恢复回放 + undo 登记同步回退 — C2 锁定正式推翻（历史翻回再提交展开原文） | 已落地（d29b76d30） |
| 8.6 搜索 Tab | Tab=接受当前匹配填入不提交 | 已落地（26c64b8d1） |
| 8.10 搜索空查询退格 | 退格=取消搜索 | 已落地 |
| 8.11 PageUp/PageDown | 行首/行尾映射 | 已落地 |
| 4.18 帮助面板 | 忙时可开 + Backspace/Enter 关闭键（旧"忙时不可开"锁定推翻） | 已落地（b2b49398b） |
| 2.7 空粘贴统一 | ReadKey 通道恒产出空 Paste 事件（对齐基准 Always-emit） | 已落地 |

批次 9（粘贴/历史交互）已落地（2026-10-23）：

| 差异项 | 内容 | 判定 |
|--------|------|------|
| 7.11 backslash+Enter 续行 | 光标前为 \ 时 Enter 删 \ 插换行（bash 风格） | 已落地 |
| 7.8 粘贴编号只增不复用 | PastedContentTracker.Clear 不再重置 _nextId（防历史残留撞号误替换） | 已落地（两处旧"重置 #1"断言同步更新） |
| 8.19 历史草稿携带 pastedContents | 涉及推翻 C2 锁定设计 + HistoryRing 结构升级（条目 text+pasted 二元组 + 导航恢复 + tracker 重放） | **登记待专项 spec**（结构改造独立立项更稳） |
| 7.10 无 bracketed paste 多行粘贴 | ReadKey 逐键通道结构性不可行（无块边界感知）；RawStdin 灰度通道整块到达天然支持 | **关闭**（通道差异 — P3 切默认后自然解决，DSG042 衔接） |

注：此批次推翻 TASK059/M8-2 既有有意设计（用户决策"全部对齐基准"）——ChatCancel 表驱动分派 HandleEscape 新语义自动生效；五处旧语义锁定测试同步更新。

## 四、工具使用不适报告（收尾汇总给用户）

1. **并行 Task 代理返回体量失控**：8 个代理各返回 200-400 行表格，单轮上下文压力大；建议 Task 工具支持"返回体上限+分文件落盘"选项，让代理直接把全表写入指定 md 文件、只返回摘要。
2. **Task 代理提示词无法附带文件**：调研要求"返回格式含两侧文件行号"只能靠提示词约定，无法预置模板文件；建议支持提示词引用仓库内模板路径。
3. **判定纪律跨代理执行不一致**：同一现象（bracketed paste 无超时兜底）两个代理分别判为 bug 和差异，需要主智能体人工仲裁；建议调研代理提示词内置统一的"自身注释契约违背才判 bug"判据。

<!-- 🤖 Auto Decision: 2026-10-23 -->
<!-- 决策: 13 项确认 bug 直接 TDD 修复，98 项差异+74 项缺失落差异文档走用户决策 -->
<!-- 原因: AGENTS.md 基准对比工作流第2/3条：确认 bug 直接修（双侧源码证据+自身契约违背），拿不准归差异 -->
<!-- 替代方案: 全部落文档问用户 —— 因 bug 判据充分（注释契约自证）而放弃 -->
<!-- 验证: 修复后全量回归+提交（见 git log）✅ -->
