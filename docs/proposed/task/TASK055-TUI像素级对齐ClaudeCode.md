# TASK055: TUI 像素级对齐 Claude Code — 自研渲染框架

> 状态: in-progress（M1.1 ✅ / M1.2 ✅ / M1.3+ 持续推进中 — 用户已进入无人值守模式，要求直接完成整个聊天主界面并逐步补充资料，每步 commit）
> 日期: 2026-10-20
> ADR: [0140 自研 TUI 渲染框架](../adr/0140-native-tui-self-rendered-pixel-alignment.md)
> 对齐基准: `D:\project\claude-code-rust\claude-code-rev-main\src\`（下称"基准源码"）
> 用户要求: 全组件自研 / Terminal.Gui 仅参考 / 截图校准 / 动画按帧率逐帧对齐 / 独立工程（未来清理 submodule 不受影响）

## 1. 工程结构规划

```
native_tui/                        # 顶层独立工程，零 modules/libs 依赖
├── native_tui.slnx
├── src/
│   ├── NativeTui.Core/            # 渲染框架类库
│   │   ├── ansi/                  # 序列库: csi/osc/sgr/dcs/esc + tokenize（对齐 ink/termio/）
│   │   ├── screen/                # cell 网格 + CharPool/StylePool/HyperlinkPool + 宽字符模型
│   │   ├── diff/                  # 逐 cell diff + patch optimize + DEC2026 包裹 + DECSTBM
│   │   ├── layout/                # flexbox 子集: grow/shrink/basis/gap/padding/percent/absolute
│   │   ├── input/                 # 按键解析: kitty CSI u / modifyOtherKeys / bracketed paste / SGR 鼠标
│   │   ├── terminal/              # 能力探测(DECRQM/DA1/XTVERSION/DEC2026 白名单) + raw mode + alt-screen + resize
│   │   ├── theme/                 # 80+ 语义色键 × 6 套主题 + auto（对齐 utils/theme.ts）
│   │   ├── figures/               # 符号常量: figures.ts 全表 + darwin/win 差异（枚举 + [EnumValue]）
│   │   ├── anim/                  # 帧率调度器: 60fps 渲染节流 / 120ms spinner / 50ms shimmer / 停滞变红
│   │   ├── components/            # 自研组件: Box/Text/ScrollBox/边框库(round/dashed/单边/borderText)
│   │   └── bridge/                # 引擎接入层（交互方式待定，见 §5）
│   └── NativeTui.App/             # REPL 应用 exe（组装完整界面）
└── tests/
    ├── NativeTui.Tests/           # L1 cell golden（离屏渲染 → cell 序列化对比）
    ├── NativeTui.FrameTests/      # L3 动画帧级（虚拟时钟 → 第 N 帧 = 期望帧）
    └── NativeTui.ScreenshotKit/   # L2 截图校准工具（截图/对齐/区域 diff/报告）
```

## 2. 对齐元素清单（验收对象）

### A 级：标志性视觉（一眼认出 Claude Code）

| # | 元素 | 规范值 | 基准位置 | 状态 |
|---|------|--------|---------|------|
| A1 | Claude 橙 | `rgb(215,119,87)`，shimmer dark `rgb(235,159,127)`/light `rgb(245,149,117)` | theme.ts:443 | ❌ |
| A2 | 输入框顶部圆角单边框 | `╭───...───╮` 只画顶边，灰 `rgb(136,136,136)`，左端 `❯` 提示符(loading 时 dim)，borderText 顶边嵌字 | PromptInput.tsx:2268 | ❌ |
| A3 | 消息流符号 | `⏺`(darwin)/`●`(其他) 消息头 + `  ⎿  `(dim, NoSelect) 工具结果缩进 + bold 工具名 + `(参数摘要)` | figures.ts / MessageResponse.tsx:22 | ❌ |
| A4 | Spinner | `·✢✳✶✻✽` 往返弹跳 120ms/帧 + 流光文字(50ms RGB 插值) + `(2.3s · ↓ 4,219 tokens · thinking)` 渐进状态 + 停滞变红 `rgb(171,43,63)` + 204 动词 | Spinner/ 目录 | ❌ |
| A5 | 用户消息灰底 | dark `rgb(55,55,55)` / light `rgb(240,240,240)`，paddingRight=1 | UserTextMessage.tsx | ❌ |
| A6 | 底部 footer | `⏺ permission mode on (shift+tab to cycle)` + `? for shortcuts`(dim, ∙ 分隔) | PromptInputFooterLeftSide.tsx | ❌ |
| A7 | 布局范式 | 无顶栏沉浸式: StickyPromptHeader + ScrollBox(stickyScroll) + 底部 PromptInput + footer | FullscreenLayout.tsx | ❌ |

### B 级：完整视觉规范

| # | 元素 | 规范 | 基准位置 | 状态 |
|---|------|------|---------|------|
| B1 | 主题系统 | 80+ 语义色键 × 6 套(dark/light/2 色盲/2 ansi) + auto 跟随系统 | theme.ts(639 行) | ❌ |
| B2 | Markdown 规则 | h1=bold+italic+underline / h2+=bold / 引用=`▎`dim+italic / 行内代码=permission 色 / 列表 2 空格每层 / 链接 OSC 8 / 表格 flexbox | utils/markdown.ts | ❌ |
| B3 | 边框库 | round(主用)+dashed(`╌╎`)+单边显示+每边独立色+borderText | render-border.ts | ❌ |
| B4 | 符号表 | figures.ts 全表: ∙ ✻ ↑ ↓ ↯ ○ ◐ ● ◉ ▶ ⏸ ↻ ← → ⑂ ◇ ◆ ※ ⚑ ▎ ━ ✔︎ × + darwin 差异 | constants/figures.ts | ❌ |
| B5 | 权限对话框色 | bashBorder 粉 `rgb(253,93,177)` / permission 蓝紫 / planMode sage 绿 / autoAccept 电紫 | theme.ts | ❌ |

### C 级：行为级对齐（手感）

| # | 元素 | 规范 | 基准位置 | 状态 |
|---|------|------|---------|------|
| C1 | 渲染节流 | 60fps(16ms throttle leading+trailing) + queueMicrotask | ink.tsx:212 | ❌ |
| C2 | diff 渲染 | cell 级 diff + DEC 2026 原子上屏 + DECSTBM 硬件滚动（不闪屏核心） | log-update.ts | ❌ |
| C3 | 宽字符 cell | SpacerTail/SpacerHead 两格占位（emoji/CJK） | screen.ts | ❌ |
| C4 | 输入 | kitty CSI u + modifyOtherKeys 双协议 Shift+Enter / bracketed paste / ctrl+r / vim 模式 / Esc 双击杀 agent / 有选择时 Ctrl+C=复制 | parse-keypress.ts(801 行) | ❌ |
| C5 | 终端探测 | XTVERSION 穿透 / DEC 2026 白名单 / tmux clamp / VS Code xterm boost / OSC 9;4 进度条(WT 排除) | terminal.ts | ❌ |
| C6 | alt-screen | DEC 1049 + 鼠标 SGR(1000/1002/1003/1006) + 文本选择 + stickyScroll | AlternateScreen.tsx | ❌ |

## 3. 里程碑（渐进式，每步: L1 红测试 → 实现 → 编译 → 绿测试 → commit）

| 里程碑 | 内容 | 验收 |
|--------|------|------|
| M1 终端底座 | ansi 序列库 + screen(cell+三池) + diff 渲染器 + 能力探测 + **虚拟终端测试设备**（可注入 stdin 序列/捕获 stdout/模拟 resize） | cell golden 框架跑通；虚拟终端回放基准序列 |
| M2 布局引擎 | flexbox 子集(grow/shrink/basis/gap/padding/percent/absolute/overflow/textWrap) | 布局 golden：同输入与基准布局结果 cell 对比 |
| M3 主题+基础组件 | theme(80+键×6套+auto) + figures 枚举 + Box/Text/边框库(含单边圆角+borderText) | B1/B3/B4 golden 全绿 |
| M4 REPL 骨架 | 消息流(⏺/⎿/灰底/bold 工具名) + 输入框(顶部圆角单边框+❯+模式指示) + footer + ScrollBox(stickyScroll) | A2/A3/A5/A6/A7 golden；**手动 exe 截图与 Claude Code 初对比** |
| M5 动画系统 | 帧率调度器 + spinner(往返/动词/停滞变红) + shimmer 流光(RGB 插值) | A4 帧级测试全绿(虚拟时钟) |
| M6 引擎接入 | bridge 层接真实引擎（方式待定 §5）→ 完整 REPL 可用 | 端到端会话（LLM 流式输出+工具调用+权限交互） |
| M7 截图校准+高级组件 | ScreenshotKit(截图/对齐/diff/报告) + Markdown/代码高亮/StructuredDiff/权限对话框/vim | L2 截图 diff 报告：A 级元素像素级一致 |

## 4. 三层校准体系（验收基建，ADR 0140 决策 3）

1. **L1 cell golden**（每里程碑 TDD 主力）：离屏渲染 → cell 网格序列化（字符+样式+位置）→ `.approved.txt` 对比；用例从基准源码提取期望值（颜色/符号/布局逐字段翻译）
2. **L2 截图像素级**（M6+）：Windows Terminal 同场景截图（新 TUI vs Claude Code）→ 对齐 → 逐像素 diff → 差异报告 → 差异回流 L1 固化
3. **L3 动画帧级**（M5+）：`IVirtualClock` 注入，第 N 帧渲染结果必须等于期望帧（spinner 120ms/shimmer 50ms/60fps 渲染节流各成用例）

## 5. ✅ 已定决策：bridge 引擎接入方式 = 进程协议（用户 2026-10-20 确认）

- native_tui.exe 通过 **stdio JSON / 本地 socket** 与 jcc.exe 通信，完全解耦
  （清理 modules 后仅需协议契约，双方可独立演进）；
- 对齐"CLI 纯非交互 + TUI 承担交互"的职责分离方向；
- 权限确认 / ask_user 交互走协议回调（TUI 端弹窗 → 协议响应 → 引擎继续）；
- 协议契约（消息 schema）在 M6 用 ADR + DTO 双向转换落地（遵守 JSON DTO 规范）；
- 放弃的替代方案：进程内引用 abstractions/engine（与 modules 耦合，清理时被牵连）。

## 5.1 ✅ 已定决策：构建隔离 = native_tui/Directory.Build.props 遮蔽根部

- MSBuild `Directory.Build.props` 就近优先：`native_tui/Directory.Build.props` 完全遮蔽仓库根部配置；
- 自带全套规范（net10.0 / Nullable / TreatWarningsAsErrors / InvariantGlobalization / AOT 兼容），
  但**不引用** Structura、不引用 JCC 生成器 analyzer（切断全部 modules 依赖）；
- 输出路径独立：`native_tui/artifacts/`（不污染根 artifacts）；
- 效果：把 `native_tui/` 目录整体搬走仍可独立编译（SDK 版本依赖机器默认，nuget 用默认源）。

## 6. 验收四列表（AGENTS.md 强制）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| native_tu i/src/NativeTui.Core（渲染框架） | NativeTui.App REPL | ❌ | ❌ |
| 三层校准体系（L1/L2/L3） | 各里程碑验收 | ❌ | ❌ |
| A/B/C 级对齐元素（§2 清单） | REPL 界面 | ❌ | ❌ |

> 已验收列按 ADR 0080 走手动 exe 验收 + L2 截图 diff 报告。

## 7. 风险与备注

- 基准源码是 source map 还原版，少数模块为 NAPI shim（color-diff/highlight.js），对应能力需 C# 自寻等价实现（highlight.js → 手写或找 AOT 兼容高亮库，进 M7 前调研）
- Windows ConPTY / Win32 控制台细节（resize 时序、UTF-16 输出、VT 直通）参考 Terminal.Gui `ConsoleDriver` 实战实现
- 基准源码无测试文件，golden 期望值全部从源码规范值翻译（theme.ts/figures.ts 均为纯数据表，可机械翻译）

<!-- 🤖 Auto Decision: 2026-10-20 -->
<!-- 决策: 工程位置定为顶层 native_tui/，里程碑 M1-M7 拆分，bridge 接入方式标记为待定 -->
<!-- 原因: 零 modules/libs 依赖满足"未来清理 submodule"；M1-M5 纯渲染不依赖 bridge 决策可先行 -->
<!-- 替代方案: modules/apps/tui 原地改（与 modules 耦合被否决）-->
<!-- 验证: 文档阶段，无编译产物 -->

<!-- 🤖 Auto Decision: 2026-10-20 (M4 第一批: 消息流+输入框+footer) -->
<!-- 决策1: 基准核对修正三处测试臆断 — 用户消息无前导空格(❯在col0, subtle色, 正文text色, 灰底满行+paddingRight=1); -->
<!--   footer 无"permission"模式, 实为 acceptEdits(⏵⏵电紫)/plan(⏸sage绿)/bypass(⏵⏵红), "? for shortcuts"仅默认模式显示(互斥); -->
<!--   spinner 在输入框上方 scrollable 区(REPL.tsx:4641)不在 footer, 分隔符是 U+00B7 middot 非 ∙(U+2219) -->
<!-- 决策2: Theme/Figures 类型上移到 NativeTui.Core 根命名空间(文件同时移到 src 根) — 根因: 类型名与同名子命名空间冲突 -->
<!--   (NativeTui.Core.Repl 内引用 Theme 必然 CS0118), 全层共享数据放根 = 层次化"下层可见上层" -->
<!-- 决策3: 光标语义对齐基准 Cursor.render — cursorCol 为文本内偏移, 屏幕列=前缀2+offset, 光标处字符反色/行尾反色空格块 -->
<!-- 替代方案: Theme 改名 ColorTheme(改动面同等但语义劣化)/别名 using(输给命名空间解析顺序, 无效) -->
<!-- 验证: native_tui 169/169 测试全绿(157 原有 + 12 新 REPL golden) ✅ -->
<!-- M4 剩余: ScrollBox(stickyScroll) + 多行输入编辑(光标移动/删除/插入) → 之后 M5 动画 -->

<!-- 🤖 Auto Decision: 2026-10-20 (M5 动画系统: spinner + shimmer + 状态行) -->
<!-- 决策1: 纯函数(SpinnerMath)与有状态(StallTracker/TokenCounter)分离, SpinnerAnimator 门面组合 — L3 帧级测试直接注入 -->
<!--   时间值逐帧断言, 无需 VirtualClock 抽象(时间即参数, 基准 useAnimationFrame 语义 = 调用方传入 time) -->
<!-- 决策2: 基准语义陷阱三处 — ①responseLength==0 时停滞从 mountTime 起算(lastTokenTime 刷新无效, 勿按直觉改); -->
<!--   ②JS Math.round 半值远离零须用 MidpointRounding.AwayFromZero(默认 ToEven 会差 1); ③停滞优先于 flash 高于 shimmer(基准早退顺序) -->
<!-- 决策3: overrideColor 抑制停滞红(基准 stalledIntensity={overrideColor?0:...}), Color? 非空即抑制, 无需独立开关 -->
<!-- 决策4: 测试命名空间不得以被测类型名结尾 — Format 类 vs NativeTui.Tests.Core.Format 命名空间 CS0234(命名空间优先于 using 的类型) -->
<!-- 决策5: SpinnerItem 双时钟注入 — AnimTimeMs(50ms 动画时钟, 驱动帧/shimmer/停滞)与 WallNowMs(墙钟, 驱动 elapsed)分离, -->
<!--   暂停感知 elapsed = PauseStartMs - LoadingStartMs - TotalPausedMs; M6 真时钟接入时仍可确定性地测 -->
<!-- 替代方案: IClock 接口注入(过度抽象, 时间即参数); 停滞红检测颜色 kind(不如显式 Color? 语义) -->
<!-- 验证: native_tui 251/251 测试全绿(204 动画核心+渲染 + 47 状态行/格式化) ✅ commit da863b25c/f1af111f6/fff250a99 -->
<!-- M5 遗留(并入 M6): 动画时钟真驱动(50ms 定时器), 思考状态生命周期状态机(最短2s展示), spinnerVerbs 动词表随机选 -->
<!-- M6: 终端真实 IO(raw mode/能力探测/resize) + 渲染循环(diff 输出+动画 tick) + 键盘解码 + bridge 进程协议 → M7 截图校准 -->

<!-- 🤖 Auto Decision: 2026-10-20 (M6a 真实终端渲染循环: 输入缓冲 + 终端层 + Demo exe) -->
<!-- 决策1: 帧双缓冲必须共享 StylePool — CellDiffer 比较两帧 cell 的 StyleId, 两个 Screen 各自私有池会 -->
<!--   让同一样式拿到不同 ID → 假 diff(每帧全量重绘); Screen 增 ctor(cols, rows, sharedStyles) 重载 -->
<!-- 决策2: ScrollBox 增 Insert(index, item)/Count — REPL 滚动区尾部常驻 spinner, 新消息必须前插到它之前 -->
<!--   (基准 spinner 在 scrollable 区尾部 REPL.tsx:4641, 不是固定行, 长内容时随滚动区滚走) -->
<!-- 决策3: 双缓冲 ping-pong 用"双屏同建+全量标志"而非 null 交换 — (front,back)=(back,front) 首帧后把 null -->
<!--   换进 back 导致 NRE(实测踩坑); _frontValid 标志触发首帧/resize 后 Compose(null, next) 全量 EraseScreen -->
<!-- 决策4: selftest 冒烟用 120ms 步进非 50ms — glyph 帧=floor(t/120), 前 3 个 50ms tick 同帧会误报"帧输出为空" -->
<!-- 决策5: 重定向守卫(Console.IsInputRedirected)给引导提示退出 1, 符合"错误提示必须有诱导方式"规则 -->
<!-- 替代方案: IClock/ITimer 抽象注入(过度抽象, selftest 直接注入时间值已够); 三屏缓冲(无必要, 标志位足够) -->
<!-- 验证: native_tui 269/269 测试全绿(+4 双缓冲/Insert); Demo --selftest 退出码 0; 重定向守卫退出码 1 ✅ -->
<!--   commit 1746de5c3(双缓冲基建) + 2e7f95487(Demo exe) -->
<!-- M6a 手动验收(ADR 0080)待用户真实终端执行: NativeTui.Demo.exe — spinner 动画/输入编辑/停滞红/↑↓滚动 -->

<!-- 🤖 Auto Decision: 2026-10-20 (M6b/c/d/e-1: 键盘解码 + 动词表 + 思考生命周期 + bridge 协议层) -->
<!-- 决策1(M6b): 解码器 chunk 边界契约 — TryParseNext 把 buffer 视为当前全部可用输入, 孤立 ESC 即 Escape 键 -->
<!--   (基准用 ESC_TIMEOUT flush, 此处以 chunk 边界近似); 未知序列整体吞为 Unknown 防泄漏(对齐基准 suppress 守卫) -->
<!-- 决策2(M6b): kill 链打断下沉到编辑方法 — 基准 onInput 单点 resetKillAccumulation/resetYankState 分散为 -->
<!--   InputBuffer 方法级 BeginEditText(kill/yank 方法自身不走), 语义等价且可单测 -->
<!-- 决策3(M6b): span 消耗方向 — buffer[n..] 是消耗前 n 个, buffer[..n] 是保留前 n 个(曾因写反致解码器死循环) -->
<!-- 决策4(M6c): 基准 useState 挂载采样一次语义用懒加载缓存字段等价实现(_cachedVerb ??= Sample), -->
<!--   显式 Message 优先对齐 overrideMessage ?? randomVerb; Demo StartTurn 不再硬编码 Thinking… -->
<!-- 决策5(M6d): 基准 setTimeout 语义离散化为帧循环 Tick 判定 — ThinkingLifecycle 纯状态机+墙钟注入, -->
<!--   thinking 最短 2s 补足 → thought for Ns 展示 2s → 消失; SpinnerItem 内部 Mode==Thinking 驱动(删外部属性) -->
<!-- 决策6(M6e-1): 协议统一 LF 分隔 — 显式 Write('\n') 不受平台 NewLine 影响(Windows WriteLine 会产生 \r\n); -->
<!--   未知 type 诊断含触发值+合法清单+升级指引(错误提示必须有诱导方式); ADR 0141 已写 -->
<!-- 替代方案: Esc 双击清空(Demo 单击近似); readline 式异步 IO(主循环同步, M6e-2 接真进程再评估) -->
<!-- 验证: 379/379 测试全绿(+110: 解码器 38/词编辑 24/动词 10/thinking 10/bridge 16 + 改造); -->
<!--   Demo --selftest 0 + --bridge-selftest 0(跨进程 stdin/stdout 管道 roundtrip 真进程级) ✅ -->
<!--   commit 6938da1e6(M6b) 3f365ed5c(M6c) 3740a1b7a(M6d) 708e5b029(M6e-1) -->
<!-- M6e-2 剩余(建议新窗口): jcc.exe 服务端事件泵(LLM 流式管道 → BridgeEvent) + Demo --bridge <exe> 真实对接; -->
<!-- M7: 截图校准工具 + Markdown/代码高亮/diff/权限对话框; vim 模式跳过(用户决策 2026-10-20, 代码量大) -->
<!-- resize 事件化: EnsureScreens 每帧对比已工作, 事件化是优化非必需, 并入 M7 顺手做 -->

<!-- 🤖 Auto Decision: 2026-10-20 (M6e-2: bridge 事件泵 + REPL 驱动器 + Demo 真实对接) -->
<!-- 决策1: 事件泵用 Actor 邮箱模型 — 后台 reader 线程生产/主循环消费 ConcurrentQueue, 无共享锁 -->
<!--   (AGENTS.md 死锁处理用 Actor 邮箱模型); IOException(引擎 stdout 崩溃)记录诊断后正常完成 -->
<!-- 决策2: ThreadState 是 [Flags] 枚举 — IsBackground 未启动线程 = Background|Unstarted, 相等比较 -->
<!--   != Unstarted 永真 → Wait 死锁(测试 120s 超时); 必须位测试 (& Unstarted) == 0; -->
<!--   定位手法: throw 探针法三轮二分(方法头过/构造后挂/Thread 探针过且暴露组合值真相) -->
<!-- 决策3: ReplBridgeDriver 是事件语义唯一翻译层 — 模拟路径与真实 bridge 共用同一套回合视图状态 -->
<!--   (BeginTurn/EndTurn 幂等, Submit 后乐观 BeginTurn, turn_started 事件到达重复应用无害) -->
<!-- 决策4: TurnCompleted 复位 ResponseLength → 累加断言必须放在回合结束前(测试时机修正) -->
<!-- 决策5: DemoSession 注入可选 BridgeClient — null=模拟状态机/非 null=事件驱动, 主循环二选一推进; -->
<!--   引擎退出检测(!IsEngineAlive && !IsBusy)追加错误消息后结束会话(不静默退出) -->
<!-- 决策6: echo-child 升级分派式 — TryReadRequest 循环 + HandleEchoRequest 有名函数, -->
<!--   assistant_message 回显提交文本(加强 selftest 断言); Skipped 诊断写 stderr(不吞诊断) -->
<!-- 决策7: BridgeClient stderr 直通不重定向 — 诊断可见且无管道缓冲填满死锁风险 -->
<!-- 替代方案: 事件泵异步流 IAsyncEnumerable(主循环同步 50ms 拍, 队列排空更简单); -->
<!--   引擎退出强杀 Process.Kill(quit 优雅关闭优先, 强杀丢失尾部事件) -->
<!-- 验证: 397/397 测试全绿(+18: 泵 5/驱动 9/请求读取 4); Demo --selftest 0 + --bridge-selftest 0 -->
<!--   (分派式 echo-child + submit 文本回显断言); --bridge 重定向守卫 1 + 诱导消息 ✅ -->
<!--   commit 6ed7cc51e(泵+驱动) + M6e-2c(对接, 本 commit) -->
<!-- M6e-2 手动验收(ADR 0080)待用户真实终端执行: NativeTui.Demo.exe --bridge <引擎exe路径> -->
<!-- 剩余: jcc.exe 服务端事件泵(LLM 流式管道 → BridgeEvent) — M6 完结, M7 截图校准/高亮/diff/权限对话框 -->

<!-- 🤖 Auto Decision: 2026-10-20 (M6g: banner 真实数据源 + Shift+Tab 权限模式循环, 用户反馈驱动) -->
<!-- 决策1: Shift+Tab 双路径归一 BackTab — VT 路径 ESC[Z(KeyPressParser BuildSimple 'Z') + -->
<!--   ReadKey 路径 Tab+Shift 修饰(ConsoleKeyAdapter), Kind 统一 BackTab 消费方无感 -->
<!-- 决策2: PermissionModeCycler 静态纯函数(放 PermissionMode.cs 同文件) — 对齐基准 -->
<!--   getNextPermissionMode.ts 非 ant 链 default→acceptEdits→plan→bypass→auto→default, -->
<!--   bypass/auto 可用性参数化(isBypassPermissionsModeAvailable/canCycleToAuto), Demo 模拟全开 -->
<!-- 决策3: SessionInfo record 为 banner 唯一数据源 — 版本查程序集(VersionPrefix 1.0.0)、 -->
<!--   cwd 进程快照、模型 --model 参数可配置(默认 Opus 5.5 模拟值)、计费模拟配置(无真实登录态); -->
<!--   bridge 模式未来由引擎 session_info 事件提供(留待协议扩展) -->
<!-- 根因: 用户反馈"版本号/模型/工作路径/切换模式都要查数据再展示" — 原硬编码 2.1.303/Opus 5.5 -->
<!-- 替代方案: 环境变量 NATIVETUI_MODEL(CLI 参数更常规); IPermissionService 接口注入(过度抽象, Demo 无真实权限系统) -->
<!-- 验证: 417/417 测试全绿(+12: BackTab 解析 1/适配 2/循环器 9); Demo --selftest 退出码 0 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-20 (M6h: inline 渲染模式 — 可复制粘贴的架构正解, 用户反馈驱动) -->
<!-- 根因: 用户"无法选择界面元素复制粘贴" — 原全屏双缓冲 diff 让整个视口成为永久动态区, -->
<!--   内容永不进终端 scrollback, 选区被刷新破坏, 退出清屏一切消失; -->
<!--   基准真相(fullscreen.ts:128): Claude Code 外部用户默认不开 alt-screen/鼠标捕获, -->
<!--   走 inline 渲染(log-update.ts 主屏路径): 内容溢出自然滚入 scrollback 成为原生文本可复制 -->
<!-- 决策1: InlineComposer 相对光标模型 — 终端滚动后绝对坐标不可知, 只有 CR/LF/CUU/CUB/CUF -->
<!--   相对量可靠(对齐基准 VirtualScreen); 帧尾光标由调用方指定(frameEnd=输入位), 返回 -->
<!--   nextCursor 供下帧传入, 全链路光标状态自洽 -->
<!-- 决策2: 增长行 append 用 LF 前进 — CUD 在视口底会静默失败(clamp), LF 才能滚屏创建新行 -->
<!--   (基准 renderFrameSlice 同款注释); 向上用 CUU(不会创建行) -->
<!-- 决策3: 尺寸重建保留旧帧跨尺寸 diff(_prevOverride) — 消息增长(屏高变化)走 append 而非 -->
<!--   全量重画; 宽度变/scrollback 行变更才 fullReset(EraseScreen 只清可见区, scrollback 历史保留) -->
<!-- 决策4: 首帧无条件 CR — 启动光标物理列未知(提示符后), 归零起点; 首帧不清屏保留终端历史 -->
<!-- 决策5: ↑↓/Ctrl+N/P inline 模式 no-op — 历史由终端滚轮原生回看(基准主屏行为); -->
<!--   --viewport 参数保留旧全屏 diff 模式(未来 fullscreen Ctrl+O 基础) -->
<!-- 决策6: CellDiffer 段扫描提取 ScanSegments 共享(行域取共有行 Min) — CellDiffer 绝对定位 -->
<!--   与 InlineComposer 相对光标消费同一差分扫描, 消除复制粘贴 -->
<!-- 决策7: 退出 WriteExit inline 只清尾部动态区(TailHeight 行), 历史留 scrollback 可复制 -->
<!-- 替代方案: 启用鼠标捕获+自行实现选择复制(违背基准默认行为); 常驻 alt-screen(同前); -->
<!--   每帧全量重画(带宽浪费, 选区更难存活) -->
<!-- 验证: 428/428 测试全绿(+11: InlineComposer 9/MeasureContentHeight 2, CellDiffer 重构零回归); -->
<!--   Demo --selftest 0(含新增 inline 链路冒烟: 首帧不清屏/增长 append/零变化零输出) + -->
<!--   --bridge-selftest 0 ✅ -->
<!-- M6h 手动验收(ADR 0080)待用户真实终端执行: NativeTui.Demo.exe — 鼠标选择 banner/消息 -->
<!--   文本复制粘贴、Enter 回合动画、消息超屏后滚轮回看历史、退出后历史保留 -->

<!-- 🤖 Auto Decision: 2026-10-20 (M6h-2: QuickEdit 保留修复 — 鼠标选择复制的最后一环) -->
<!-- 根因: 用户验收"显示正常但鼠标无法框选" — ToRawInput 禁用了 ENABLE_QUICK_EDIT_MODE; -->
<!--   conhost 宿主(explorer 启动默认窗口)下 QuickEdit 是鼠标选择唯一机制, 禁用即无法选择; -->
<!--   Windows Terminal 不受此位影响(WT 原生选择)但用户环境落在 conhost -->
<!-- 决策: QuickEdit/EXTENDED 保留原状(仅清 Processed/Line/Echo) — 对齐基准 Node 行为 -->
<!--   (Node readline raw mode 不碰 QuickEdit); 副作用=QuickEdit 选择期间 conhost 暂停 -->
<!--   程序输出(Write 阻塞), 取消选择后恢复, 与基准 Claude Code 一致 -->
<!-- 替代方案: 保持禁用+要求用户用 Windows Terminal(违背"可复制"诉求, 环境不可控) -->
<!-- 验证: 428/428 全绿(ToRawInput 期望更新); selftest 0 ✅ 待用户 conhost 真机复验 -->

<!-- 🤖 Auto Decision: 2026-10-20 (M6e-2f: jcc.exe bridge_serve — M6 引擎接入服务端侧完结) -->
<!-- 决策1: jcc 侧三件套 — BridgeProtocol(DTO 镜像+Safe 上下文+TryReadRequest 诱导诊断) + -->
<!--   BridgeEventConsumer(IEventConsumer→ADR 0141 事件行, thinking 回合内单次信号+墙钟差) + -->
<!--   BridgeServeLoop(协议主循环: submit 起回合任务与读循环并发, interrupt 取消, 忙时 submit -->
<!--   诱导拒绝, quit/Eof 取消在途回合收尾后退出, 未知 type stderr 诊断不中断) -->
<!-- 决策2: SessionTurnResult 翻译为 loop 异常语义 — 成功=null(turn_completed)/取消=OCE -->
<!--   (turn_interrupted)/超时+错误=InvalidOperationException(error 事件带诱导消息); -->
<!--   PermissionPending 确认按打断处理(bridge 无 UI), 留 M7 权限对话框时扩展协议 -->
<!-- 决策3: CreateMainAgent 从 CliSession 提取 internal static — BridgeServeCommand 复用同一 -->
<!--   主代理构造(带 prompt injection/审计/上下文持久化完整链路), 禁止复制散落 -->
<!-- 决策4: Console.Out 全程重定向 stderr + 协议走 OpenStandardOutput 独立 writer — -->
<!--   引擎内部任何 Console 输出不得污染 stdout 协议流; JCC_LOG_LEVEL=Error 全程持有 -->
<!-- 决策5: SessionController 首次 submit 时绑定(闭包 controller ??=) — consumer 由 loop -->
<!--   构造(TypedWriter.Synchronized 包 stdout), 事件出口唯一; 构造无副作用延迟安全 -->
<!-- 坑: BridgeJsonContext.Safe 的 TypeInfo 属性(Safe.BridgeEvent)正常工作(生成代码 getter 走 -->
<!--   Options.GetTypeInfo 绑定实例 Options) — 会话早期"必须 context 实例重载"诊断不成立, -->
<!--   真正的编译阻塞是 GlobalUsings 缺 System.Text.Encodings.Web(JavaScriptEncoder) -->
<!-- 顺手修: ApiKeySaveLoadTests 适配 #421 C6 DPAPI 加密(存量失败, 明文断言过时, 对齐 -->
<!--   ApiKeyProtectorTests 跨平台分支断言) -->
<!-- 验证: jcc 侧 26/26 bridge 测试 + host.tests 全量 1319/1319 全绿; 跨进程管道冒烟 ✅ -->
<!--   (quit 立退 0 / submit→error 超时诱导 / interrupt→turn_interrupted / 未知 type stderr -->
<!--   诱导诊断 / 中文不转义) -->
<!-- commit ee128f07f(三件套+21 测试) 62175a691(ApiKey 修复) 73adfca24(命令层+路由+5 测试) -->
<!-- M6e-2f 手动验收(ADR 0080)待用户真实终端执行: NativeTui.Demo.exe --bridge jcc.exe -->
<!--   (jcc.exe = artifacts/bin/JoinCode/Debug/net10.0/jcc.exe), 配好 API key 后验证真实 -->
<!--   LLM 流式回合(token_delta 增量/工具事件/思考事件)在 TUI 侧渲染 -->
<!-- 剩余: M6 完结 — M7 截图校准/Markdown 高亮/diff/权限对话框(session_info 协议扩展) -->

<!-- 🤖 Auto Decision: 2026-10-20 (M6i: 权限系统对接引擎 + banner 真实数据 + 界面汉化) -->
<!-- 决策1: TUI PermissionMode 枚举整体重写为引擎 5 值(plan/auto/ask/bypass/unattended) — -->
<!--   废弃 ClaudeCode 旧值(default/acceptEdits/dontAsk/bypassPermissions, 引擎侧已合并废弃); -->
<!--   Auto=引擎默认(footer 无模式指示仅快捷键提示), Cycler 链 auto→plan→ask→bypass→unattended→auto, -->
<!--   bypass 按 session_info.bypassAvailable 短路(settings.json disableBypassPermissionsMode 安全闸) -->
<!-- 决策2: ADR 0141 协议扩展三消息 — 事件 session_info(version/model?/permissionMode/ -->
<!--   bypassAvailable/cwd, 引擎启动即发, banner 真实数据源) + 事件 permission_mode_changed -->
<!--   (实际生效值, 安全闸拦截时与请求值不同, TUI 永以回执为准) + 请求 set_permission_mode; -->
<!--   model=null 时 TUI 显示"当前模型未设置"(用户明确要求) -->
<!-- 决策3: 引擎侧接线 — BridgeServeCommand 启动即发 session_info(版本=引擎程序集/模型= -->
<!--   FastModeService.PrimaryModelId 回退 --model 参数仍空为 null/权限=PermissionChecker.CurrentMode -->
<!--   /bypass 可用=!DisableBypassGate/cwd); PermissionChecker public 化 DisableBypassGate + -->
<!--   新增 ModeInitialization(Task) 供握手前等待 fire-and-forget 加载完成; BridgeServeLoop 加 -->
<!--   PermissionModeApplier delegate 注入(测试友好), 未知值/无权限系统→error 诱导事件(合法值清单) -->
<!-- 决策4: 界面汉化(功能性文案) — footer "? 查看快捷键"/"esc 打断"/"{symbol} {中文标题} 已开启 -->
<!--   （shift+tab 切换）"/模式中文标题(计划模式/标准模式/询问模式/全放行/无人值守)/登录错误/effort -->
<!--   通知; banner 标题 Claude Code→jcc(自有产品 CLI 名); SpinnerVerbs 187 词保留英文 -->
<!--   (基准像素级对齐资产非功能文案); seed 模式(无引擎)模型显示"当前模型未设置" -->
<!-- 决策5: RowText.Of 测试辅助升级 — 跳过 SpacerTail 占位列(宽字符只输出 rune 一次, -->
<!--   与 Screen 渲染语义一致), 中文断言自然; 对存量 ASCII 断言零影响 -->
<!-- 坑: SpacerTail cell 携带同一 rune(写入时 new Cell(rune,...,SpacerTail)) — 逐列拼接中文会 -->
<!--   双写, 必须跳过; FooterState.Mode 默认值显式设 Auto(新枚举 Plan=0, 默认构造防踩坑) -->
<!-- 验证: native_tui 458/458 + selftest/bridge-selftest 双冒烟(session_info/权限回执断言入冒烟); -->
<!--   host.tests 1333/1333(bridge 36 含 4 个新 case) + guard.security 1104/1104 + gui/tui 编译; -->
<!--   真实进程管道验证 ✅: jcc.exe bridge_serve → session_info(version=1.0.0, model=agnes-video-2.5-flash -->
<!--   真实 FastMode 数据, permissionMode=auto, bypassAvailable=true) + set_permission_mode plan → -->
<!--   permission_mode_changed plan 回执; banner "当前模型未设置"路径由 Driver 单测覆盖(null→占位文案) -->
<!-- 剩余: TUI 真实终端联调验收(--bridge jcc.exe 看真实引擎会话/权限循环/footer 汉化); -->
<!--   M7 权限对话框(permission_pending 确认事件, 替换现"按打断处理"降级)/模型列表切换 UI -->

<!-- 🤖 Auto Decision: 2026-10-20 (M7 预排期: 多子代理分组显示 — 300 子代理场景支撑) -->
<!-- 触发: 用户提出可能开展 300 个子代理, 要求调查基准 ts 的多子代理显示方案; 用户确认排入 M7 -->
<!-- 调查结论(基准唯一数据源: claude-code-rev-main): -->
<!-- 1. 分组条件: utils/groupToolUses.ts — 同一 assistant 消息(同 API 响应)中 2+ 个 Task -->
<!--    工具调用合并为 grouped_tool_use 渲染块; 仅 AgentTool 注册 renderGroupedToolUse 插槽 -->
<!--    (Tool.ts:678); 跨消息不合并, verbose 模式不分组 -->
<!-- 2. 分组视图(AgentTool/UI.tsx:649 renderGroupedAgentToolUse + AgentProgressLine.tsx): -->
<!--    头部行 = ToolUseLoader(⏺ 闪烁 dim, 完成变 success/error 色) + "Running N agents…"; -->
<!--    全完成="N agents finished"; 全后台="N background agents launched (↓ to manage)"; -->
<!--    同类型时="Running 5 Explore agents…"(commonType 替换) -->
<!--    每代理 2 行: 行1="   ├─ "树字符(dim, 最后项└─) + bold 反色背景 agentType(agentColorManager -->
<!--    按类型分配色) + "(description)" + " · N tool uses · N tokens"; -->
<!--    行2="   │  ⎿  " + lastToolInfo 或 "Initializing…"(dim, 最后项无竖线延续) -->
<!--    全同类型 hideType: 省略反色徽章改 "name: description"; resolved 行取消 dim; -->
<!--    async 后台代理省略行2(状态="Running in the background") -->
<!-- 3. 300 个规模: UI 层零截断(agentStats.map 全量渲染, 300 代理=600 行, 超屏靠滚动区 -->
<!--    滚出视口/inline 滚入 scrollback); 无引擎层并发池上限; 头部 N 是唯一汇总; -->
<!--    注意: 单代理才有折叠(最近 3 条 + "+N more tool uses (ctrl+o to expand)")和终端过小 -->
<!--    condensed 单行模式 — 分组视图没有这套保护 -->
<!-- 4. 后台/teammate 第二显示面: CoordinatorAgentStatus.tsx(CoordinatorTaskPanel)渲染在 -->
<!--    输入框 footer 下方(非消息流), 1s tick: "  ○ main" + "  ○ name: desc ▶ elapsed · -->
<!--    ↓ tokens · N queued"(描述 truncate-end 截断, evictAfter 过期自动驱逐, Enter 查看/x 停止) -->
<!-- 5. bridge 协议扩展需求(ADR 0141 演进): ① tool_use 事件带同消息分组标记(或客户端按 -->
<!--    事件顺序自算同批次); ② 新增 agent_progress 事件(子代理内部工具活动+token 计数, 带 -->
<!--    parentToolUseId); ③ 头部 N 由客户端按未解析 task tool_use 数自算 -->
<!-- 实现顺序(M7 内): 协议扩展先行(jcc 侧 agent_progress 事件) → AgentProgressLine 树形 -->
<!--   分组渲染(TDD: cell golden 对齐) → CoordinatorTaskPanel 后台面板(视 M7 进度可后延) -->
<!-- 验证: 本条为纯调查+排期决策, 无代码改动; 实现时按 TDD 红绿流程验证 -->

<!-- 🤖 Auto Decision: 2026-10-20 (架构转向: ADR 0142 进程内嵌入 — jcc.exe 引用 NativeTui.Core) -->
<!-- 触发: 用户"是不是直接写到 jcc.exe" — M7 协议税(每特性协议化)+300 子代理序列化开销+单 exe 诉求 -->
<!-- 决策: 方案 C — jcc.exe 工程引用 NativeTui.Core(依赖单向 jcc→native_tui, ADR 0140 零依赖 -->
<!--   字面成立), 新增 --tui 入口分流 Console, 进程内适配器直连(IEventConsumer→ReplView 零 DTO); -->
<!--   ADR 0141 bridge stdio 降级为可选形态(远程/headless/调试), 协议三件套+bridge_serve 保留不删 -->
<!-- 方向澄清: 0141 当年否决的进程内嵌是"引擎做库进 TUI 宿主", 本决策是反向"TUI 做库进 jcc 宿主", -->
<!--   不复用该否决结论 -->
<!-- 替代方案: 维持 bridge 唯一通路(协议税+序列化+双 exe); TUI 搬进 modules/(428 测试迁移+分析器 -->
<!--   约束+编译耦合, 代价最大无增量收益) — 均放弃 -->
<!-- 风险缓解: 双 Build.props 共存验证先行(实现首步); TUI 适配层异常边界+断言; AOT 风格本就满足; -->
<!--   --tui 与 CLI 入口互斥(对齐 bridge_serve stderr 重定向先例) -->
<!-- M7 多子代理显示实现路径更新: agent_progress/session_info/权限协商改进程内事件直连(原"协议 -->
<!--   扩展先行"自动简化, 无需 ADR 0141 事件表演进) -->
<!-- 验证: 本条为纯架构决策, 实现按 ADR 0142 实施范围 TDD 推进; 待实现后 ADR 转 accepted -->

<!-- 🤖 Auto Decision: 2026-10-20 (架构终局修正: ADR 0142 superseded → ADR 0143 引擎库化多宿主) -->
<!-- 触发: 用户澄清"jcc.exe 是不是可以作为库? GUI/TUI 分别引用使用, 共享协议层, 写到引擎上 -->
<!--   做协议" — 0142 方案C(jcc 当宿主塞 --tui)理解偏了用户意图, 用户要的是多宿主平等 -->
<!-- 决策: ADR 0143 两阶段 — ①先抽引擎库: JoinCode.csproj 拆分为 JoinCode.Engine.csproj -->
<!--   (引擎能力: app/builder[EngineSessionFactory] + core/services[CliSession] + SessionController -->
<!--   + core/bridge[协议三件套]) 与 CLI 薄壳宿主(entry/commands/display/output 等留 Exe); -->
<!--   ②再做 native_tui 宿主: native_tui/apps/NativeTui.Host 照抄 gui hosting 先例, 跨 slnx -->
<!--   ProjectReference 引引擎库 + NativeTui.Core, IEventConsumer 进程内直连 ReplView -->
<!-- 协议层唯一数据源(核心不变式): 引擎侧 IEventConsumer 事件模型=协议本体(进程内形态), -->
<!--   BridgeProtocol DTO=JSON 投影(跨进程形态) — 两种形态同源, 演进必须先改事件模型再同步 -->
<!--   投影, 禁止投影先行; ADR 0141 降级可选形态(远程/headless/调试)保留不删 -->
<!-- 调查依据: gui/tui 已 ProjectReference 引 JoinCode.csproj(引擎当库先例存在, 无需发明新模式); -->
<!--   gui 引用面已查清(App.Builder + Abstractions 门面, 迁移面可控); 引擎/CLI 宿主边界已按目录 -->
<!--   摸清(见 0143 边界清单) -->
<!-- 替代方案: 宿主直接引 jcc Exe 一步到位(拖进 CLI 宿主细节, 边界不干净) — 放弃 -->
<!-- M7 特性全部改道进程内直连: session_info/agent_progress/权限对话框不再扩展 0141 事件表 -->
<!-- 验证: 本条为纯架构决策文档; 阶段一实施每步编译+测试+commit, 全量回归含 host.tests+gui -->
<!--   测试+bridge 冒烟; 待两阶段落地后 ADR 0143 转 accepted -->

<!-- 🤖 Auto Decision: 2026-10-20 (ADR 0143 阶段一第一拔: JoinCode.Engine 库工程落地) -->
<!-- 搬移(六件, git mv 保历史): BridgeProtocol→engine/bridge + SessionController(含 -->
<!--   SessionTurnResult)/AgentStreamChunkAdapter/BridgeEventConsumer→engine/adapters + -->
<!--   SessionIdGenerator/CliServiceContext→engine/session -->
<!-- 关键决策1: 命名空间全部保持不变(JoinCode.Cli.Bridge/JoinCode.Adapters/JoinCode.Cli 跨程序集 -->
<!--   同命名空间) — 宿主 GlobalUsings/源码/host.tests/gui/tui 全部零改动, 破坏面最小化; -->
<!--   CliXxx→EngineXxx 重命名留独立批次(搬移与改名二步走, 禁一步动太多) -->
<!-- 关键决策2: engine 引用面与 cli 相同六工程(composition/clock/dream/browser/pipelines/ -->
<!--   pithosdb) — SessionController 依赖闭包横跨 abstractions/slash/brain/agents 传递链 -->
<!-- 关键决策3: engine/Directory.Build.props 原样复制 cli 版(含 EnableConfigurationBindingGenerator) -->
<!-- 坑: 手写 props 含"→"等特殊字符导致 MSB4066(<Using> 无法识别 Include)+TargetFramework -->
<!--   求值丢失 — 新建 props 必须原样复制既有文件改内容, 勿手写特殊字符注释 -->
<!-- 工程注册: modules/apps/module.slnx + build/sln/JoinCode.slnx /app/ folder; ci-build/ci-e2e/ -->
<!--   daily-aot 等 workflow 经 slnx/ProjectReference 链自动纳入无需改; ci-unit-tests 仅新建独立 -->
<!--   engine 测试工程时才需加 matrix -->
<!-- 验证: engine 0警告0错误 + cli 0警告0错误 + host.tests 全量 1333/1333(bridge 93/93) + tui -->
<!--   0错误; gui 21 错误系本地 Dock submodule 未初始化的存量环境问题(错误全为 IRootDock/ -->
<!--   IDocumentDock 等 Dock 类型, 与搬移零关联, CI ubuntu submodule 正常) — 存量问题不在本拔处理 -->
<!-- 下一拔(Step 2): CliSession 表现层解耦(CliEventConsumer 已在库侧, TerminalHelper/ -->
<!--   PrefixCommandRouter 依赖需搬移或改注入) → Step3 EngineSessionFactory+app/modules+ -->
<!--   CommandLineOptions → Step4 ApplicationBuilder 引擎/宿主逻辑拆分 → Step5 gui/tui 切引 -->
<!-- 🤖 Auto Decision: 2026-10-20 (ADR 0143 阶段一 Step 2: 提取 EngineAgentFactory) -->
<!-- 范围修正: CliSession 本质是 CLI 交互编排器(斜杠命令路由+TerminalHelper IO), 真正引擎侧 -->
<!--   资产只有 CreateMainAgent — Step 2 从"搬整个 CliSession"修正为"提取工厂函数", CliSession -->
<!--   本体留宿主(表现层, 归宿正确) -->
<!-- 落地: engine/session/EngineAgentFactory.cs 新建(sealed class, 从 CliSession.CreateMainAgent -->
<!--   迁移, 去掉未用的 chatService 参数); CliSession/BridgeServeCommand 两处调用点改工厂调用; -->
<!--   host.tests GlobalUsings 补 JoinCode.Abstractions.Entity -->
<!-- 关键决策: 命名空间 JoinCode.Cli 保持(宿主调用点零 using 改动); TDD 双确认(先红后绿) -->
<!-- 坑: 静态类不能做 ILogger<T> 泛型参数(CS0718) — 工厂类用 sealed class+私有构造替代 static -->
<!-- 验证: engine 0警告0错误 + cli 0警告0错误 + 新测试 3/3 绿(TDD 红→绿) + host.tests 全量 -->
<!--   1336/1336(原1333+新增3) — 下一拔(Step 3): EngineSessionFactory + app/modules 七模块 + -->
<!--   CommandLineOptions 搬库(断裂点①②), 消除 gui 对 cli 工程的直达依赖前置条件 -->
<!-- 🤖 Auto Decision: 2026-10-20 (ADR 0143 阶段一 Step 3a: 契约+选项+构建器拆分先行) -->
<!-- 调查修正: AppModuleAttribute 实际未搬(Step1 只搬六件); cli 内 [Register(...)] 带参 -->
<!--   类型共 49 个(telemetry hooks/启动步骤/pipe/Vim/onboarding 混合体) — CoreModule 调 -->
<!--   AddJccAutoRegisteredServices(生成器按程序集名生成) 不可本批搬 — Step3 拆成 3a(契约层)+ -->
<!--   3b(49 类型分类搬移+模块搬移+SessionFactory 搬移) -->
<!-- 3a 落地(git mv 五件+新建两件+改三件): CommandLineOptions/IAppModule/AppModuleContext/ -->
<!--   AppModuleAttribute/DotEnvConfig → engine/app/(命名空间全部保持); 新建 engine/app/ -->
<!--   EngineApplicationBuilder(UseModule/BuildHost(+ILoggerProvider 参数化,TerminalHelper 耦合 -->
<!--   解除)/ConfigureModulesAsync/LoadConfigAsync+DotEnv区域) + EngineConfigInitializer(从 -->
<!--   StartupWorkflow 提取 EnsureConfigFilesExistAsync+两模板; 交互式 Onboarding 留宿主); -->
<!--   cli ApplicationBuilder → 纯静态宿主工具(ParseArgs/ShowHelp/ShowVersion/RunSubCommand); -->
<!--   EngineSessionFactory 接线改 Engine* 类(本批留 cli, provider 在宿主侧计算注入) -->
<!-- 关键决策1: 引擎侧类名用 EngineApplicationBuilder(非同名) — 避免跨程序集同 namespace+name -->
<!--   歧义(CS0433), cli 宿主名零改动(Program.cs+host.tests 17 处 BuildBridgeGuardServices 不动) -->
<!-- 关键决策2: app_module.generator 发射目标 ApplicationBuilder→EngineApplicationBuilder -->
<!--   (UseAllModules 零消费方纯便利API, 生成器 1 行改) -->
<!-- 坑: rg 搜 [Register] 必须用 -F "[Register(" (带参特性不匹配 [Register] 精确模式, 首查 -->
<!--   漏报 49 个); dotnet test 断言序列要与测试模块实现一致(marker 模块不写 calls) -->
<!-- 验证: engine/cli/tui 全 0警告0错误 + 新测试 2/2 绿(模块顺序+provider注入, TDD红→绿) + -->
<!--   host.tests 全量 1338/1338 — 下一拔(Step 3b): 49 个 [Register] 类型分类(引擎侧/宿主侧) -->
<!--   → 引擎侧随迁 engine + 5 通用模块(Core 需拆: AddAiWorkflowServices+AddAllPipelines 归 -->
<!--   引擎, AddJccAutoRegisteredServices 归 cli 的 CliRegistrationModule) → EngineSessionFactory -->
<!--   搬 engine(CreateCliSessionAsync 薄包装留 cli) → Step4 gui/tui 切直引 engine -->
<!-- 🤖 Auto Decision: 2026-10-20 (ADR 0143 阶段一 Step 3b: 模块+SessionFactory 搬库, 用户选定方案) -->
<!-- 用户决策: 先模块+SessionFactory(49 个 [Register] 类型暂留 cli 由 CliRegistrationModule 兜底, -->
<!--   gui 会话行为零变化), 分类搬移留 Step 3c -->
<!-- 前置确认: AddAllPipelines 全部工厂 lambda 延迟解析(sp => new PipelineBuilder...Use(sp.GetRequired -->
<!--   Service...)) — 注册顺序无关, CoreModule 拆分安全; gui/tui 会话当前经 CoreModule→AddJcc 获得 -->
<!--   全部 49 类型注册(telemetry hooks/ConfirmationGate 等), 拆分后必须由 gui/tui extraModules 传 -->
<!--   CliRegistrationModule 兜底才能行为零变化 -->
<!-- 落地: git mv 六件 — 5 模块(Core/Clock/Browser/Housekeeping/McpInit)→engine/app/modules/ + -->
<!--   EngineSessionFactory→engine/app/builder/(目录纯度: app/ 下 8 文件+modules/ 违规 JCC10002, -->
<!--   8 件归入 builder/ 子目录镜像 cli 结构); CoreModule 剧 AddJcc 两行(留 AddAiWorkflowServices+ -->
<!--   AddAllPipelines); cli 新建 CliRegistrationModule(Order=31, 兜底调 AddJccAutoRegisteredServices/ -->
<!--   Options)+CliSessionFactory(CreateCliSessionAsync 薄包装: 8 模块+ConsoleActor provider); engine -->
<!--   CreateCoreAsync→公开 CreateSessionAsync(modules+loggerProvider 参数化, TerminalHelper 块删除); -->
<!--   5 调用点改接(Program/McpCommand/BridgeServe→CliSessionFactory, gui/tui extraModules 补 -->
<!--   CliRegistrationModule); EngineApplicationBuilder cref 恢复类型引用 -->
<!-- 关键决策: CliRegistrationModule Order=31 紧随引擎 CoreModule(30) — AddAllPipelines 延迟解析 -->
<!--   证明顺序无感; gui 无需补 using(JccChatSession 命名空间 walk-up 命中 App.Modules), tui/ -->
<!--   host.tests 需补 global using JoinCode.App.Modules -->
<!-- 坑: 单模块测试只能断言注册描述符(Contains ServiceType) — GetService 解析需完整宿主 DI 图, -->
<!--   [Register] 类型构造依赖来自其他模块, 孤立 BuildServiceProvider 解析必炸 -->
<!-- 验证: engine/cli/tui 全 0警告0错误 + gui 21 错误=Dock 存量基线(JccChatSession 零新错误) + -->
<!--   新测试 1/1 绿 + host.tests 全量 1339/1339 — 剩余: Step 3c 49 类型分类搬移(引擎侧 telemetry -->
<!--   hooks/ConfirmationGate 随迁后 gui/tui 可去 CliRegistrationModule) → Step 4 gui/tui 切直引 -->
<!--   engine 消除 cli 依赖 → 改名批次(CliServiceContext→EngineServiceContext 等) → ADR 0143 状态 -->
<!--   accepted; 阶段二 native_tui 宿主另起会话 -->
