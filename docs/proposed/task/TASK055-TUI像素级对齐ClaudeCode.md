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

<!-- 🤖 Auto Decision: 2026-10-20 (ADR 0143 阶段一 Step 3c: 49 类型分类搬移首批 24 个) -->
<!-- 分类调查(explore 代理): 49 个 [Register] 类型 = ENGINE 25 + HOST 24 -->
<!--   ENGINE 25 = 17 telemetry hooks(零 cli 专属依赖, 唯一伴生基类 TelemetryPostHook<TContext> -->
<!--   无[Register]必须连搬) + TrustFolderManager(ITrustFolderManager 在 abstractions, 工作目录 -->
<!--   信任是三宿主共性安全基座, 依赖 100% 干净) + user_experience 5 件(AssistantMode/Buddy/ -->
<!--   Clipboard/ProactiveState/Sticker, 全部经 abstractions+IProcessService 抽象零 cli 依赖) -->
<!--   + SystemPromptApplyStep + NonInteractiveExitCleanupStep(此 2 件受 StartupContext 阻塞) -->
<!--   HOST 24 = CliPermissionConfirmationHandler(TerminalHelper y/a/n 交互) + Onboarding 2 件 + -->
<!--   VimEngine + 10 startup/core 步骤(ConfirmationGate/ReplLoop/SessionInit 等重度 TerminalHelper/ -->
<!--   CliSession) + 3 non_interactive + pipe 6 件(用户预设宿主侧) + ConsoleOutput(System.Console) -->
<!-- 本批搬移(开心路径 24 个零阻塞): git mv 18 件 middlewares(17 hooks+基类)→engine/app/middlewares/ -->
<!--   + TrustFolderManager→engine/core/trust/ + user_experience 5 件→engine/services/user_experience/ -->
<!--   (ProactiveTickScheduler/TerminalFocusDetector 是 HOST 伴生物留 cli; 2 个 StartupContext 阻塞 -->
<!--   步骤留待 StartupContext 拆分批次, 见剩余清单) -->
<!-- 接线方案 X: 注册入口挂 CoreModule(三宿主零改动) — CoreModule 加 AddJoinCodeEngine -->
<!--   AutoRegisteredServices/Options 两行("JoinCode.Engine"→"JoinCodeEngine" 由 sanitize 逻辑生成); -->
<!--   弃用 EngineRegistrationModule(曾建 Order=29 又删: UseAllModules 是零消费方死代码, 运行时 -->
<!--   模块链是 EngineSessionFactory 基础5模块+extraModules 手写列表, 新模块不进链=engine 24 类型 -->
<!--   运行时丢注册, 测试绿是假象因单模块直测不走全链) -->
<!-- 生成器新开关: ServiceRegistrationGenerator 加 IsCurrentAssemblyScope — 程序集声明 -->
<!--   AssemblyMetadata("AutoRegisterScope","CurrentAssembly") 则只扫自身; 根因: 生成器对库项目 -->
<!--   扫全部引用闭包(注释明示因子系统库未启用生成器), engine 作为启用生成器的聚合库会扫到 bridge -->
<!--   的 internal pipe 中间件(CS0122, bridge IVT 含 Composition 故 Composition 不炸而 engine 炸) -->
<!--   且加 IVT 会导致重复注册(hook 双执行, 46 注册翻倍≈92 是回归信号); AssemblyMetadata 是 -->
<!--   ItemGroup 项非 PropertyGroup(踩 MSB4066) -->
<!-- 测试联动: CliRegistrationModuleTests 收窄断言(删 IClipboard/IProactiveState 两行, 留 -->
<!--   ConfirmationGate/Onboarding/VimEngine 三个 HOST 代表); 新建 CoreModuleRegistrationTests -->
<!--   (2 用例: 6 服务描述符 + 3 管道 hook 描述符); host.tests GlobalUsings 补 3 行(Interfaces. -->
<!--   Security/Pipeline/Services.Web) -->
<!-- 验证: engine/cli/tui 三编译 0警告0错误 + gui 42 错误全 CS0246 Dock 存量(JccChatSession 零错误, -->
<!--   搬移类型零出现) + host.tests 1341/1341(1339+2 TDD 闭环) + jcc.exe bridge_serve 冒烟 -->
<!--   session_info 真实输出(DI 全链健康: AddJoinCodeEngine 注册链+hooks 管道+会话工厂) -->
<!-- 剩余(Step 3c 后续): StartupContext 拆分(含 CliSession/CliOutputContract 两 cli 专属字段, -->
<!--   是 SystemPromptApplyStep/NonInteractiveExitCleanupStep 搬移前置) + pipe 协议族边界案例 -->
<!--   (BridgeConnectionNegotiator/BridgeHeartbeatService 依赖 100% 干净, 若 0143 协议层唯一数据源 -->
<!--   覆盖 bridge 协议族可升 ENGINE) → Step 4 gui/tui 切直引 engine → 改名批次 → ADR 0143 accepted -->

<!-- 🤖 Auto Decision: 2026-10-20 (ADR 0143 阶段一 Step 4: gui 切直引 engine + tui 宿主整体移除) -->
<!-- 用户决策: JoinCodeTui.csproj 完全移除(自研 native_tui 已接任 TUI 职责, M6i 权限对接+bridge -->
<!--   联调完成), 顺便清理 libs/ 三 submodule(Terminal.Gui/Editor/Dock, ADR 0140 既定清理对象) -->
<!-- tui 移除范围: modules/apps/tui/ 整工程 + test/unit/tui.tests/ 整测试工程 + ci-tui-daily.yml -->
<!--   整 workflow + ci-unit-tests.yml apps 域 matrix 行 + daily-aot-publish.yml jcctui 条目 + -->
<!--   Gui.slnx ×2 行 + apps module.slnx ×1 行 — 全部归档 .xxx/(tui.20261020/tui.tests.20261020) -->
<!--   而非删除, 保留可追溯性 -->
<!-- 依赖归位: CommandQueue 搬回 cli/queue/(tui 删除后仅 cli ReplLoopStep 消费, 且 gui 零消费, -->
<!--   对齐 0143 边界清单 queue 归 CLI 薄壳; 搬回后相对 HEAD 零变化) ; SlashCommandRunner + -->
<!--   Cli.Commands.Prefix 族 4 件留守 engine(gui 的 JccChatSession 消费 SlashCommandRunner+ -->
<!--   !/!! 前缀命令, 共享执行链路是跨宿主引擎能力) -->
<!-- gui 切直引: JoinCodeGui.csproj cli→engine + JccChatSession extraModules 去 CliRegistrationModule -->
<!--   (engine CoreModule 的 AddJoinCodeEngine 注册链覆盖 24 类型) — gui 42 错误=Dock 存量基线 -->
<!--   零新增, JccChatSession 零错误 -->
<!-- 坑: host.tests 基线修正 1341→1337 — worktree 验证法(HEAD 3f0b8c4cd 临时 worktree 跑全量) -->
<!--   证实 HEAD 基线本身 1337, 记忆中 1341 是历史环境数字, 回归对照以 1337 为准 -->
<!-- 验证: engine/cli 编译 0警告0错误 + gui 42=Dock 基线零新增 + host.tests 1337/1337 全绿(与 -->
<!--   HEAD 基线 worktree 对照一致) + jcc.exe bridge_serve 冒烟 session_info 真实输出 -->
<!-- 后续批次: 三 submodule 移除(.gitmodules+gitlink 归档+gui csproj 5 个 Dock ProjectReference) → -->
<!--   StartupContext 拆分 → pipe 协议族边界 → 改名批次 → ADR 0143 accepted; 阶段二 native_tui -->
<!--   宿主(native_tui/apps/NativeTui.Host)另起会话照抄 gui 先例 -->

<!-- 🤖 Auto Decision: 2026-10-20 (commit 9cb7765bd: libs/ 三 submodule 移除) -->
<!-- 落地: .gitmodules 整体归档(三 submodule 全清后无存在意义) + git rm --cached 三 gitlink + -->
<!--   submodule git 数据(Editor/Terminal.Gui)归档 .xxx/submodules.20261020/ + gui csproj 5 个 -->
<!--   Dock ProjectReference 移除 + libs/ 空目录清理(仅留 Directory.Build.targets) -->
<!-- 状态: gui 42 错=Dock 存量基线保持(Dock 类型 CS0246 是 gui 代码引用, 待 gui Dock 体系重构 -->
<!--   批次处理, 本批不动 gui 代码); 全仓 grep 零 libs/ 残留引用; Terminal.Gui/Editor submodule -->
<!--   早已无用(tui 删除前已改 NuGet 包引用) -->

<!-- 🤖 Auto Decision: 2026-10-20 (终局修正: GUI 保留原样 + Dock submodule 恢复, commit fb4ac1c4c) -->
<!-- 用户决策撤销: "我后悔了,你不要碰GUI的任何东西" — gui 工程从 .xxx/ 完整归位(gui + -->
<!--   join_code_gui.tests + Gui.slnx + ci-gui-daily.yml), Dock submodule 恢复(gitlink + .gitmodules -->
<!--   精简为仅 Dock + gui csproj 5 个 ProjectReference); Terminal.Gui/Editor 保持移除(tui 已删 -->
<!--   无消费者); CreateGuiSessionAsync 永久保留(e2e 活代码 + 阶段二 native_tui 接入点, 误删已纠正) -->
<!-- 契约: 本分支禁止触碰 modules/apps/gui/ 任何内容(用户新 GUI 在其他分支开发) -->
<!-- 验证: PR #431 通过 squash 合并 main(680f725cb), CI 全绿含 gui 编译(submodules:recursive -->
<!--   初始化 Dock 后 GUI 实际可编译, 本地 42 错是 submodule 未初始化时的状态) -->

<!-- 🤖 Auto Decision: 2026-10-20 (ADR 0143 阶段一收尾: 两项边界决策关闭 + 改名批次) -->
<!-- StartupContext 拆分批次取消: 调查证实启动中间件管道组装只在 cli 的两个 ModeRunner -->
<!--   (Interactive/NonInteractive), gui 走 CreateGuiSessionAsync(clearPipeEndpoint:true), -->
<!--   native_tui 阶段二走 bridge_serve 路径 — 管道是 CLI 宿主专属启动编排设施; -->
<!--   SystemPromptApplyStep/NonInteractiveExitCleanupStep 的 Step 3c "ENGINE" 判定是过度判定 -->
<!--   (引擎能力已在 IChatService.SetSystemPromptAsync/IStopHookManager 接口上, 步骤只是 CLI -->
<!--   参数翻译层), 12+ 启动步骤全部留 cli(HOST), StartupContext 无需拆分 -->
<!-- pipe 协议族边界关闭: BridgeConnectionNegotiator/BridgeHeartbeatService 留 cli — 命名管道 -->
<!--   是 CLI 宿主专属设施(CreateGuiSessionAsync 显式 clearPipeEndpoint 证明 GUI/TUI 会话无 -->
<!--   命名管道; native_tui 走 stdin/stdout bridge 协议非命名管道), "依赖 100% 干净"不构成升 -->
<!--   ENGINE 的充分条件, 边界判据=是否存在跨宿主消费 -->
<!-- 改名批次: CliServiceContext→EngineServiceContext(git mv 100% rename + 类名 + cli 侧 3 处 -->
<!--   消费点[CliSession ×2 + SessionInitStep ×1 限定写法 Cli.EngineServiceContext]), 命名空间 -->
<!--   保持 JoinCode.Cli 跨程序集兼容; engine 内 Cli 前缀类型清零(唯一残留即此件) -->
<!-- 验证: engine/cli 双编译 0警告0错误 + host.tests 1337/1337 全绿(基线保持) -->
<!-- ADR 0143 阶段一完结: 转 accepted; 剩余=阶段二 native_tui 宿主(native_tui/apps/NativeTui.Host) -->
<!--   另起会话照抄 gui 先例(CreateGuiSessionAsync 是组装入口) -->

<!-- 🤖 Auto Decision: 2026-10-20 (ADR 0143 阶段二完结: native_tui 宿主四批次落地) -->
<!-- 批次1 079065e23: Host 工程 + InProcessEventConsumer(IEventConsumer 第四实现, 语义对齐 -->
<!--   engine BridgeEventConsumer, WriteEvent 序列化 stdout→ConcurrentQueue 内存 record, -->
<!--   对齐 Core BridgeEventPump 邮箱模型先例) -->
<!-- 批次2 9649bb7d5: EngineTurnClient(BridgeServeLoop.DispatchRequest 进程内版, 委托注入 -->
<!--   TurnExecutor/PermissionModeApplier 字符串化避免 PermissionMode 同名冲突, 共享队列 -->
<!--   FIFO 保序: turn_started 必先于回合内部事件) -->
<!-- 批次3 c096a0e11: EngineSession 组装照抄 BridgeServeCommand(CreateGuiSessionAsync→延迟绑定 -->
<!--   SessionController→PermissionChecker 安全闸→session_info 种子事件经 internal Enqueue); -->
<!--   HostSession 只取 DemoSession bridge 模式(inline 独占/无模拟回合/无引擎退出检测); -->
<!--   决策1: 回合 busy 全事件驱动不乐观预挂 BeginTurn(进程内 TurnStarted 零延迟, 预挂+忙时 -->
<!--   拒绝会导致 busy 卡死 — 无 TurnStarted 事件匹配 EndTurn) -->
<!--   决策2: OutputType 修正 WinExe→Exe(TUI 需控制台子系统, WinExe 不继承父控制台 raw mode 不可用) -->
<!--   决策3: 引擎侧 JoinCode.Abstractions.Security.PermissionMode 与 Core NativeTui.Core.Repl.PermissionMode -->
<!--   同名, GlobalUsings 4 别名消歧(EnginePermissionMode 等), 协议值字符串互转 -->
<!--   决策4: Console.SetOut(stderr) + 重定向前捕获独立 writer(对齐 bridge 先例, 引擎日志不污染渲染流) -->
<!-- 批次4: native_tui.slnx 注册 Host+Host.Tests(5 工程), ADR 0143 阶段二实施结果段落, 本决策块 -->
<!-- 验证: Host.Tests 21/21 + Core 458/458 + Demo selftest 回归通过; Host --selftest 真实冒烟 -->
<!--   OK(引擎组装+session_info 真实数据+PermissionChecker 真实回执+无效模式诱导); 负路径 -->
<!--   (重定向检测/未知参数)退出码 1 -->
<!-- ReplShell 提取已完成 (commit a92bc1a16): IReplEngine(Submit/Interrupt/SetPermissionMode/ -->
<!--   DrainEvents/DrainDiagnostics/IsAlive) 统一引擎交互面; HostSession/DemoSession 归档 .xxx; -->
<!--   SimulatedEngine 时钟注入+忙时拒绝 Error 诱导(对齐 EngineTurnClient); BridgeEngine 适配 -->
<!--   BridgeClient; 模拟回合事件驱动(spinner Message 直改弃用=对齐真实路径空消息+随机动词); -->
<!--   权限本地回执统一走 PermissionModeChanged 事件; Core 480/480 + Host 21/21 + 三 selftest 回归 -->
<!-- 遗留: 权限挂起首版按打断处理(M7 扩展交互确认); M7 权限对话框在 ReplShell 单点扩展 -->

### ADR 0143 阶段二手动验收（ADR 0080，待用户执行）

| 步骤 | 命令 | 预期 |
|------|------|------|
| 1 冒烟 | `native_tui/artifacts/bin/NativeTui.Host/Debug/net10.0/NativeTui.Host.exe --selftest` | `HOST SELFTEST OK`，退出码 0 |
| 2 交互 | 同 exe 无参数（真实终端） | banner 显示引擎版本/真实模型/cwd；"引擎启动中…"后进入输入框 |
| 3 提交 | 输入文本 + Enter | 用户消息上屏 → spinner 思考动画 → 助手回复（需已配置模型） |
| 4 打断 | 回合中按 Esc | spinner 消失回空闲（turn_interrupted） |
| 5 权限 | Shift+Tab 循环 | footer 模式 plan→ask→bypass→unattended→auto（引擎回执为准） |
| 6 退出 | 空闲时 Esc 或 Ctrl+C | 清尾部动态区，历史留 scrollback，`NativeTui.Host 已退出。` |


## M7-1 截图校准工具链落地（2026-10-20，L2 像素级验收基建）

<!-- 🤖 Auto Decision: 2026-10-20 -->
<!-- 决策: M7 第一项落地=截图校准工具链, 从青蛙TUI(D:\project\青蛙的TUI, JoinCode 旧分叉)复制四件套 -->
<!-- 来源调查: 两并行探查代理结论 — 青蛙TUI 是 Terminal.Gui v2 驱动的 jcctui, 渲染引擎不可搬, -->
<!--   但 tool/tui 截图校准工具链独有且与像素级对齐方法论完全同构(ANSI→cell→同字体栅格化→ImageChops 像素 diff) -->
<!-- 落位: native_tui/tool/screenshot/{compare/(compare_reference.py+README), reference/(TS 采集端 7 文件)} -->
<!--   + tests/NativeTui.Tests/snapshot/(SnapshotVerifier approved/received 快照基建) -->
<!--   + Core repl/ReferenceScene(80×24 固定 fixture 场景→ToAnsiText) + Demo --capture 薄壳 -->
<!-- 首验结果: clawd 9×3 与 prompt 边框 80×3 两 region 均 0 字符/0 像素差异(退出码 0) — -->
<!--   M6e 的 Clawd/banner 对齐与 prompt 全宽─线修正获基准像素级自动化验收 -->
<!-- 过程修正: 首跑 prompt 差异 12 字符=TS 参考端输入框 fixture("fixture input")与 native_tui 空输入不对齐 — -->
<!--   属两侧 fixture 不对齐非渲染 bug, ReferenceScene.Input 补同 fixture 后归零 -->
<!-- 已知限制: py cells() 解析器不支持 SGR 7(inverse) — 光标反色被按默认样式解析(与青蛙TUI 原版一致, -->
<!--   scope=border properties); 需要光标像素验收时增强 py 支持 code 7 -->
<!-- TDD: 红测试 CS0103 先行→SnapshotVerifier 3 用例+ReferenceScene 4 用例(含 Verify 模式快照) -->
<!--   →approved 快照人工审核批准; 全量 487/487(480+新增 7) + Demo selftest 回归通过 -->
<!-- 验证命令(真实运行): cd native_tui/tool/screenshot/reference && npm ci && node build.mjs <基准src> && -->
<!--   FORCE_COLOR=3 node reference.mjs; dotnet run --project apps/NativeTui.Demo -- --capture .xxx/screen.ansi; -->
<!--   python tool/screenshot/compare/compare_reference.py --reference tool/screenshot/reference --capture .xxx/screen.ansi --output .xxx/visual -->

### M7 前置调查结论（青蛙TUI 资产盘点，2026-10-20）

| M7 项 | 青蛙TUI 状态 | 复用结论 |
|---|---|---|
| 截图校准 | ✅ 完整工具链独有 | ✅ 已落地（本决策块） |
| Markdown 渲染 | ✅ TranscriptWriter(Markdig AST→终端投影) | 可抄投影算法；**用户已决策引入 Markdig NuGet**（纯 AST 解析库 AOT 兼容，不违反 ADR 0140 精神） |
| 代码高亮 | ⚠️ 绑 Terminal.Gui.Editor xshd | 不可搬，维持先调研结论 |
| StructuredDiff | ✅ 完整 | **无需复制** — w1 引擎侧已全通（IEventConsumer.OnToolEnd 带 StructuredPatchHunk[]，InProcessEventConsumer 已收参当前丢弃），M7 只差透传+渲染（⎿ 折叠思路抄青蛙TUI TranscriptDocument） |
| 权限对话框 | ✅ 三档+DialogRequest TCS 桥+Rewind+重试预算 | 高价值复用（桥模式+编排），接 ReplShell 扩展点 |
| 模型列表 UI | ✅ 本地配置拉取 | ⚠️ 与既定要求（后端获取）不符，只抄 Picker 交互 |
| 多子代理分组 | ⚠️ 半成品（基建未接线） | 按已固化基准调查自建；SubAgentCardManager LRU 可参考 |
| resize 事件化 | ✅ TerminalResizeMonitor | 小件可搬，低优先 |

<!-- 决策依据: 青蛙TUI 渲染引擎=Terminal.Gui v2 与 native_tui 自研 cell 管线根本不同不可整体搬运; -->
<!--   复用判据=纯逻辑零耦合优先(diff 全套/权限桥/Markdown 投影算法), 框架耦合组件只抄思路 -->

## M7-2 StructuredDiff 渲染落地（2026-10-20，引擎补丁→TUI diff 块全链路）

<!-- 🤖 Auto Decision: 2026-10-20 -->
<!-- 决策: 四批渐进落地(每批编译+测试+commit): ①Core 零依赖投影 ②Host 投影 ③引擎 JSON 投影同步 ④模拟引擎演示 -->
<!-- ①Core: bridge/DiffModels.cs 自带 DiffHunk/DiffLine/DiffLineKind(DiffLineKind 数值对齐引擎 PatchLineType 0/1/2 可直转 byte); -->
<!--   BridgeEvent.ToolResult 加 Patch 可选参数(旧构造零改动); DiffResultItem 多行渲染(summary ⎿ 行+hunk 块 4 空格缩进+折叠); -->
<!--   着色用基准 theme.ts diff 前景键 DiffAddedWord/DiffRemovedWord 替代青蛙TUI 硬编码十六进制(#4eba65/#ab2b3f — 唯一数据源) -->
<!-- ②Host: InProcessEventConsumer.OnToolEnd ProjectPatch LINQ 链式投影; JoinCode.Cli.DiffLine 同名歧义沿用 -->
<!--   EnginePermissionMode 别名先例加 CoreDiffLine 全局别名 -->
<!-- ③引擎: ADR 0143 先事件模型后投影 — IEventConsumer.OnToolEnd 本就带 patch(协议本体), 本批补 JSON 投影; -->
<!--   定义 BridgeDiffHunk/BridgeDiffLine 镜像 DTO 而非直接序列化 StructuredPatchHunk — 其 type/oldStart 等字段 -->
<!--   会被 native_tui DiffLine 忽略致 kind 全部静默退化为 context(语义丢失); 两侧 Patch 均 WhenWritingNull -->
<!--   (null 不输出字段 — 引擎旧 golden 契约不变, 旧 TUI 反序列化兼容) -->
<!-- 折叠策略: 预览 2 行(summary 外)对齐青蛙TUI ToolResultPreview.Take(3) 先例, 超出折叠 "… +N lines (Ctrl+O)" dim; -->
<!--   Ctrl+O 展开交互=M7 后续项 -->
<!-- TDD: Core 12 用例(渲染 golden cell 级+JSON 往返+驱动分派) + Host 2 用例(投影) + 引擎 2 用例(stdout JSON golden) -->
<!--   全绿: NativeTui.Tests 500/500(基线 487+新增 13) + Host.Tests 23/23 + host.tests 1343/1343 -->
<!-- 自动化回归: M7-1 像素校准重跑 clawd+prompt 两 region 0 字符/0 像素差异(渲染基线未破坏) -->
<!-- 手动验收路径: dotnet run --project apps/NativeTui.Demo(模拟引擎 Edit 回合含 4 行 hunk, 2.4s 后 ToolResult -->
<!--   渲染 diff 块+折叠提示); 真实终端颜色肉眼验收待用户执行(下表) -->

### M7-2 手动验收（ADR 0080，待用户执行）

| 步骤 | 命令 | 预期 |
|------|------|------|
| 1 模拟 diff | `dotnet run --project native_tui/apps/NativeTui.Demo`（真实终端） | 提交任意文本 → 2.4s 后 ⎿ Updated demo/simulated.cs + hunk 块（+行绿/-行红/@@ dim）+ "… +2 lines (Ctrl+O)" |
| 2 真实引擎 | `NativeTui.Host.exe`（需配置模型） | 引擎跑 Edit 类工具 → 工具结果渲染真实补丁 diff 块 |

## M7-3 权限对话框落地（2026-10-20，引擎 PendingConfirmation → TUI 对话框闭环）

<!-- 🤖 Auto Decision: 2026-10-20 -->
<!-- 决策: 三批渐进落地(每批编译+测试+commit): ①Core 对话框渲染+模态改道 ②Host 引擎闭环 ③文档收尾 -->
<!-- ①Core(2def5f5e8): PermissionCoordinator TCS 桥(Begin 取消旧等待者=青蛙TUI DialogRequest 取代语义, -->
<!--   Complete 幂等, 焦点三档环绕) + PermissionRequestItem 像素对齐基准 FallbackPermissionRequest -->
<!--   (仅顶边框 permission 色 round 无角全宽/bold permission 标题 paddingX=1/工具行 3 缩进(外1+内2 paddingX 累计)/ -->
<!--   描述 dim(空折叠)/"Do you want to proceed?"/Select 三档数字前缀选项(1.Yes 2.Yes-dont-ask-again 3.No, -->
<!--   焦点行 ❯ suggestion+index dim+label suggestion, 非焦点空格 indicator+label 默认)/"Esc to cancel" dim; -->
<!--   MeasureHeight 12+描述行) + ReplShell 挂起时键盘模态路由(数字1-3 直选/Esc=Deny/Enter=焦点档/上下环绕, -->
<!--   其余按键吞掉防输入污染)且回合结束(完成/打断)自动 Deny 收尾防对话框残留; -->
<!--   渲染方法按 DiffResultItem 先例收口 MessageRenderer(7 个 RenderPermission*) -->
<!-- ②Host(24a1fd826): TuiPermissionBridge(引擎线程 Confirm→BeginPermissionRequest→TCS 同步阻塞等按键, -->
<!--   未绑定 shell 降级 Deny 防死锁 — 对齐 CLI IConfirmationGate 单通道路由先例) + -->
<!--   TuiPermissionConfirmationHandler(转发 bridge + 临时批准: Allow 1 分钟/AlwaysAllow 30 分钟 + -->
<!--   [红绿灯ask] 标签同级批准 — 对齐 CliPermissionConfirmationHandler) + TuiInteractionModule(Order=80) -->
<!--   经 CreateGuiSessionAsync extraModules 注册(对齐 GuiInteractionModule 先例); -->
<!--   BeginPermissionRequest/Permission 升 public(宿主生产消费面, 与 Run/WriteExit 同级); -->
<!--   权威路径=引擎返回值模式: PermissionCheckMiddleware→PendingConfirmation→ -->
<!--   PermissionAwareToolExecutor→IPermissionConfirmationHandler.Confirm(ADR 0143 进程内闭环, 不扩展事件表) -->
<!-- TDD: Core 21 用例(coordinator TCS 6+渲染 golden 8+shell 模态 7) + Host 10 用例(module 注册 1+bridge 4+ -->
<!--   handler 批准 4+真容器解析 1); 全绿: NativeTui.Tests 521/521(基线 500+新增 21) + Host.Tests 33/33(基线 23+新增 10) -->
<!-- 自动化回归: M7-1 像素校准重跑 clawd+prompt 两 region 0 字符/0 像素差异 + selftest 真实容器组装通过 -->
<!-- 测试陷阱: 引擎 PermissionRequest(JoinCode.Abstractions.Security) 与 Core Repl.PermissionRequest 同名 — -->
<!--   fake 接口实现须全限定; PermissionResult 同在 Security 父命名空间(非 Security.Permission) -->

### M7-3 手动验收（ADR 0080，待用户执行）

| 步骤 | 命令 | 预期 |
|------|------|------|
| 1 权限模式 | （真实终端+配置模型）Shift+Tab 切到 plan | footer 模式轮换 |
| 2 触发确认 | 提交让引擎调用写文件类工具的请求 | 对话框浮现: 顶边框紫 + **Tool use** + 工具名(描述) + Do you want to proceed? + 1/2/3 选项 |
| 3 三档交互 | 按 1 → 引擎继续执行; 再触发按 2 → 同会话不再问; 再触发按 3/Esc → 工具被拒 | 对话框消失, 回合继续, footer 无残留 |
| 4 焦点导航 | 上/下箭头移动 ❯ 指示 + Enter 选择 | 焦点环绕(1→3→2→1) |

## M7-4 Markdown 渲染落地（2026-10-20，Markdig 投影 → 块模型 → 终端渲染全链路）

<!-- 🤖 Auto Decision: 2026-10-20 -->
<!-- 决策: 四批渐进落地(每批编译+测试+commit): ①Core 块模型+布局器+渲染 ②卫星 Markdig 投影器 -->
<!--   ③引擎闭环(bridge 协议+Driver 分流+Host 投影) ④Demo 演示回合 -->
<!-- 架构三层(Core 零依赖铁律 ADR 0140): Core DTO(MarkdownStyle flags+8 块 record, 主题/宽度无关可零依赖直测) -->
<!--   → 卫星 NativeTui.Markdown(Markdig 1.3.2 投影只产结构+行内 flags, 标题级别样式由布局层施加=职责分离) -->
<!--   → Core MarkdownLayout(块→物理行)+MessageRenderer(段样式→SGR); Markdig 不进 Core 保 AOT+零依赖 -->
<!-- 基准对齐(formatToken 权威映射, utils/markdown.ts 381 行全读): h1=bold+italic+underline, h2+=bold; -->
<!--   引用=每行 ▎ dim 前缀+内容叠 Italic(修青蛙TUI 前缀只加一次坑); 列表 2 空格/层+有序分级编号 -->
<!--   (深度 0/1 数字/2 字母/3 罗马); 表格列宽=max(显示宽,3)+"|"分隔行; hr=三字符"---"; -->
<!--   顶层块间空 1 行=基准 Box gap 视觉主路径; codespan→permission 色对齐基准 color('permission') -->
<!-- 青蛙TUI 坑全规避: 标题不分级/引用前缀只加一次/~~删除线误判 Bold/表格单元格样式丢失 -->
<!-- 快路径: ProjectOptional 采样前 500 字符查语法字符(对齐基准 hasMarkdownSyntax), 无语法→null→降级纯文本 -->
<!-- bridge 协议: AssistantMessage 扩 Markdown 可空字段(WhenWritingNull 保旧 JSON 逐字节兼容)+ -->
<!--   MarkdownBlock 树 JsonPolymorphic discriminator(同 ADR 0141 风格); 投影决策在 Host -->
<!--   InProcessEventConsumer.OnDone(进程内直连内存对象零序列化税, 未来引擎 JSON 携带同字段自动升级) -->
<!-- ④Demo: SimulatedEngine.DemoMarkdown 手构 8 类块(对齐 SimulatedPatch 先例直接构 DTO 不经投影器), -->
<!--   完成事件走 Driver 分流; SeedMessages 首条 assistant 回复升级行内样式(启动即见渲染) -->
<!-- TDD: 批1 Core 22 用例(布局 14+消息项 8) + 批2 卫星 18 用例 + 批3a 5+2 用例(Driver 分流/JSON 往返/Host 投影) -->
<!--   + 批3b 1 用例(演示回合全形态); 全绿: NativeTui.Tests 549/549(基线 521+批3a 5+批3b 1+M7-3 基线含 22) -->
<!--   + NativeTui.Markdown.Tests 18/18 + Host.Tests 35/35 + Demo selftest OK -->
<!-- 自动化回归: M7-1 像素校准重跑 clawd+prompt 两 region 0 字符/0 像素差异(渲染基线未破坏) -->
<!-- 测试陷阱: 集合表达式表格 Headers 须扁平单元格构造(深层嵌套推断失败 CS0029, Seg/Cell 辅助方法先例); -->
<!--   Markdig 的 MarkdownDocument 与 Core 同名→卫星全局别名先例; 测试渲染换行用窄 frame 触发按宽布局 -->

### M7-4 手动验收（ADR 0080，待用户执行）

| 步骤 | 命令 | 预期 |
|------|------|------|
| 1 种子渲染 | dotnet run --project native_tui/apps/NativeTui.Demo（真实终端） | 启动即见 ● 圆点 + 行内样式：粗体"Screen cell 网格"、permission 色行内代码 |
| 2 全形态回合 | 同上提交 `md` → 等 5.4s 回合完成 | assistant 消息渲染 8 类块：h2 粗体标题/段落行内样式/无序+有序列表/▎ 引用斜体/csharp 代码块/---/表格（表头粗体+对齐分隔行） |
| 3 真实引擎 | NativeTui.Host.exe（需配置模型） | 引擎回复含 Markdown 语法 → ProjectOptional 投影 → 块模型渲染（无语法回复仍纯文本快路径） |
| 4 窄终端换行 | 终端调窄后重跑步骤 2 | 段落软换行（词边界贪心+CJK 逐字）、列表续行对齐内容列、代码块不换行 |

## M7-4b Demo 演示命令路由（2026-10-21，用户验收模式驱动）

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: SimulatedEngine.Submit 改演示命令路由(md/diff/perm/all 四命令+未知命令 Error 诱导), -->
<!--   回合脚本 TurnScript 枚举四值, AdvanceStages 拆三个有名函数(Thinking/ToolUse/Responding)按脚本分派 -->
<!-- 需求来源: 用户验收方式=直接跑 Demo.exe 输演示命令逐条看效果(不依赖真实引擎/不要启动参数), -->
<!--   单一"任意文本"回合无法分开验收 Markdown/diff/权限三链路 → 命令路由把三链路拆成可独立触发的演示 -->
<!-- 权限对话框演示路径(perm): 1.2s 产 BridgeEvent.PermissionRequest(ToolName/Description) → -->
<!--   挂起期无 TokenDelta(权限等待语义) → 8s 延迟完成(交互窗口加长,用户从容选档) -->
<!-- PermissionRequest 分派架构: Driver 加可选权限钩子 Action<string,string>(null=纯视图断言场景忽略), -->
<!--   ReplShell 构造时注入 BeginPermissionRequest — 权限模态是 shell 层状态,Driver 只路由不持有 -->
<!--   (对齐"Driver 唯一翻译层"先例;真实宿主路径仍走 TuiPermissionBridge.Confirm 同步回调不经此事件) -->
<!-- 未知命令诱导: Error 消息含四命令名+各自用途+怎么做(对齐九荣九耻"错误提示必须有诱导方式") -->
<!-- 基建修复(顺带,缺陷驱动): SnapshotVerifier 行尾无关比对(CRLF→LF 折叠) — 根因 git autocrlf=true -->
<!--   物化改写 approved.txt 为 CRLF 击穿裸 Ordinal 比较; .gitattributes 钉死 *.approved.txt eol=lf -->
<!--   + 工作区 approved 归一 LF 双保险; 先写红测试(Verify_ApprovedWithCrlfLineEndings)再修复 -->
<!-- TDD: 红测试 6 新增(md/diff/perm/perm 挂起/all/未知命令 Theory×2) + 既有 5 测试 hello→all 迁移; -->
<!--   全绿 NativeTui.Tests 558/558 + Markdown.Tests 18/18 + Demo selftest/bridge-selftest OK -->
<!-- M7-4 验收表步骤 2 语义更新: "提交任意文本"已被命令路由取代(任意文本现回 Error 诱导) -->

### M7-4b 手动验收（ADR 0080，待用户执行）

| 步骤 | 命令 | 预期 |
|------|------|------|
| 1 命令提示 | 直接运行 NativeTui.Demo.exe（真实终端） | 种子消息末条显示四命令说明（md/diff/perm/all） |
| 2 md | 输入 `md` 回车 → 等 5.4s | 纯 Markdown 回合：8 类块全形态渲染，全程无工具事件（无 ⎿ 工具行） |
| 3 diff | 输入 `diff` 回车 → 等 5.4s | 纯补丁回合：⎿ 工具结果 + 结构化 diff 块（+绿/-红 着色可见），完成消息纯文本 |
| 4 perm | 输入 `perm` 回车 → 1.2s 后弹权限对话框 | 数字 1-3 直选/Esc=Deny/上下环绕/Enter=焦点档；等待期 spinner 无 token 计数；8s 回合自动收束（未选择时 Deny 收尾） |
| 5 all | 输入 `all` 回车 → 等 5.4s | 完整回合：工具调用+补丁+Markdown 完成消息（既有默认演示） |
| 6 未知命令 | 输入任意其他文本回车 | Error 诱导消息：列出四命令名+用途+正确写法，无 TurnStarted |

## M7-4b 用户验收反馈修复（2026-10-21，真实终端验收驱动）

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: DiffResultItem 预览上限 2→10 行 — 对齐基准 Edit diff 全量渲染先例 -->
<!--   (claude-code-rev-main/src/tools/FileWriteTool/UI.tsx L138-141: "update renders the full diff -->
<!--   regardless of verbose"), 折叠上限采 Write MAX_LINES_TO_RENDER=10 防刷屏先例 -->
<!-- 根因(缺陷驱动定位): 渲染链路无缺陷 — MessageRenderer 红绿前景/Theme 色值/StylePool SGR/ -->
<!--   InlineComposer 输出全链路正常; 真因=旧预览上限 2 行被 hunk header+context 行吃光额度, -->
<!--   Removed/Added 行全被折叠吞掉, 用户在真实终端看到的全是灰(dim)行 -->
<!-- 修复内容: ① PreviewPatchRows 2→10 ② SimulatedPatch 行序 Removed/Added 前置+Context 殿后 -->
<!--   (保证演示补丁红绿行可见) ③ SimulatedPatch 公开 static 供渲染可见性回归测试(对齐 DemoMarkdown 先例) -->
<!-- TDD: 红测试 Render_DemoPatchShowsRedAndGreenLines(编译红=SimulatedPatch 未公开) → -->
<!--   修复后 9/9 绿; 全量回归 NativeTui.Tests 559/559 + Demo selftest OK + Demo.exe 重编 -->
<!-- 用户反馈①输入历史(↑↓)与②/命令补全: 用户明确指示移交其他人做, 本线不实现 -->
<!-- M7-4 接续: M7-5 代码高亮调研(MarkdownCode.Language 已保留消费面) → M7-6 模型列表 → M7-7 多子代理分组 -->

## M7-5 代码高亮调研（2026-10-21，基准源码全读 + nuget 生态调研）

### 基准双引擎体系真相（claude-code-rev-main 全读结论）

| 引擎 | 底层 | 场景 | 颜色策略 |
|------|------|------|---------|
| A: cli-highlight | highlight.js + 库内置主题 | Markdown ```代码块 | 库直出 ANSI（还原树无源码，从调用方推断） |
| B: color-diff-napi | Rust 原版=syntect+bat；TS 移植版=highlight.js+手写 scope 表 | StructuredDiff / HighlightedCode | scope→RGB 手写表 + 自拼 ANSI |

**关键事实**：
1. Rust 原版（syntect+Monokai Extended）才是基准真实架构；TS 移植版用 highlight.js 是 vendor 缺失的无奈替代（`src/native-ts/color-diff/index.ts` L1-17 头注释自证）
2. token 树 → scope 映射表只需 ~24 键（MONOKAI_SCOPES dark / GITHUB_SCOPES light，L188-243），三级回退查表：精确 scope → `scope.split('.')[0]` 前缀 → 默认前景
3. **diff 删除行有意跳过语法高亮**（`-` 行默认样式+红背景；`+`/context 行全高亮，L913-918）
4. 语言检测：特殊文件名表（Dockerfile/Makefile/Rakefile→ruby...）→ 扩展名 getLanguage(ext) → shebang（#!/bin/bash→bash 等，L422-451）
5. highlight.js 全量注册 190+ 语法代价 ~50MB/100-200ms → 惰性 require + LRU 500 缓存（token/render 两级）
6. 降级链五层全落"默认前景纯文本"无崩溃：env 开关 → 设置开关 → 加载失败 → 语言不支持(plaintext/markdown 回退) → emitter 形状校验
7. STORAGE_KEYWORDS 重拆：const/let/var/class/fn 等从 keyword 色重拆为 _storage 色（对齐 syntect storage.type 着色，L248-265）

### C# NativeAOT 候选方案对比

| 方案 | 语言覆盖 | AOT 风险 | 依赖 | 基准接近度 |
|------|---------|---------|------|-----------|
| **A: TextMateSharp + Grammars（推荐）** | VS Code 全量语法 | 中：Onigwrap 原生 DLL（P/Invoke，AOT 支持需卫星实测） | 2 nuget，活跃维护（2.0.4 2026-06） | **=基准 Rust 原版 syntect 路线**（TextMate 引擎同源） |
| B: ColorCode.NET | ~23 语言 | 低（纯托管） | 1 nuget | 低（颜色体系不同需重做映射） |
| C: 手写词法引擎 | 自选子集 | 零 | 零 | 中（scope 表可照抄基准 24 键） |

**A 方案背书**：Terminal.Gui v2（2.3M 下载）高亮即建在 TextMateSharp 上且官方支持 NativeAOT；语法 JSON 数据驱动无反射 emit；Monokai Extended 主题文件可直接复用（对齐基准 Rust 原版行为而非 TS 替代品）。

**A 方案架构（ADR 0140 先例）**：卫星项目 `NativeTui.Highlighting` 隔离 Onigwrap/TextMateSharp（对齐 NativeTui.Markdown 隔离 Markdig），Core 零依赖只消费 DTO（`HighlightedSpan(text, Rgb)` 列表）；Markdown 代码块 + diff 行高亮共用单管线（基准两引擎合一启示）；语言检测照抄基准 detectLanguage 三级；diff 删除行跳过高亮语义直接复用。

<!-- M7-5 调研完成: 待用户确认方案(A/B/C)后进卫星项目 AOT 实测(A 方案前置条件) → 实现 -->
<!-- AOT 实测点: ①Onigwrap 原生 DLL 在 PublishAot 发布的正确携带 ②语法 JSON 嵌入资源加载 ③win/linux 双平台 -->

## M7-5 代码高亮落地（2026-10-21，A 方案 TextMateSharp 全链贯通）

### 实施记录（5 commit 链）

| 阶段 | commit | 内容 |
|------|--------|------|
| AOT 探针 | 7e706d905 | 卫星项目 NativeTui.Highlighting + AotProbe：PublishAot 编译零错 + libonigwrap.dll 正确携带 + AOT 产物输出与 Debug 一致（三关全过） |
| 卫星实现 | 22d8fa618 | TextMateCodeHighlighter：整块高亮 API（独立会话 ruleStack 跨行状态）+ 语言三级检测（手动别名表 14 项→id 精确→Grammar Aliases 遍历）+ Monokai(dark)/LightPlus(light) + 负结果缓存；16/16 测试绿 |
| Core 接线 | c6df3d26a | ICodeHighlighter 接口 + HighlightSpan DTO（Core 零依赖）→ LayoutWithHighlights 双入口 → MessageRenderer.ApplyCodeHighlight 覆写 cell 前景 → ReplBridgeDriver/ReplShell 可选注入（null=纯文本零行为变化）；全量 642/642 绿 |
| Demo 接入 | 18eee0b13 | 交互/bridge 两处 ReplShell 构造注入 TextMateCodeHighlighter(dark)，md 命令 csharp 代码块启动即见 Monokai 配色 |

### TextMateSharp 2.0.4 API 与 README 差异（实现期实证）

- `GetScopeByLanguageId`（非 README 的 GetScopeNameFromLanguageId）；`TextMateSharp.Registry.Registry`（非 Internal.Registry）
- `ThemeTrieElementRule.foreground` 是 **int colorId**（-1=无色），经 `theme.GetColor(id)` 得 "#RRGGBB"；`Language` 无 Name 字段（仅 Id/Aliases/Extensions）

### 四列验收表（AGENTS.md 强制）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| NativeTui.Highlighting 卫星项目（TextMateCodeHighlighter + Monokai/LightPlus 主题） | Demo Program.cs 两处 ReplShell 注入（交互 :70 / bridge :130） | ✅ | ✅ ADR 0080：--selftest 绿 + Onigwrap 原生 DLL 齐备（libonigwrap.dll/Onigwrap.dll/TextMateSharp*.dll）+ capture 基准帧 911 字节不变；真实终端 Monokai 视觉验收待用户跑 md 命令确认 |
| Core ICodeHighlighter + HighlightSpan DTO（CodeHighlighting.cs） | ReplBridgeDriver → MarkdownMessageItem(:98) → MarkdownLayout.LayoutWithHighlights → MessageRenderer.ApplyCodeHighlight | ✅ | ✅ MarkdownHighlightTests 8/8（stub 高亮器纯 Core 断言）+ 全量 642/642 + Highlighting.Tests 16/16 |
| AOT 三关（PublishAot 编译/原生 DLL 携带/AOT 产物等价） | AotProbe 卫星项目（保留为回归工具） | ✅ | ✅ AotProbe 实测三关全过（7e706d905） |

### 像素回归跳过依据（双依据成立）

ReferenceScene.cs 不含任何 Markdown 渲染调用（纯手工 cell 构造，grep 零命中）且本次改动未触碰 → capture 911 字节与历史基准一致 → 高亮属新渲染路径不影响既有基准 region。

<!-- M7-5 完成: 用户真实终端 md 命令视觉验收 → M7-6 模型列表(后端获取) → M7-7 多子代理分组 → resize 事件化 -->

## M7-6 模型列表调研（2026-10-21，基准源码 + 本项目引擎侧现状盘点）

### 基准 /model 链路真相（claude-code-rev-main 全读结论）

| 项 | 基准实现 | 文件:行号 |
|----|---------|----------|
| 命令入口 | local-jsx 懒加载命令 + alt+p 热键内联打开 | commands/model/index.ts:1-16, PromptInput.tsx:2061 |
| 数据来源 | **本地硬编码主体**（5 套订阅层级列表）+ **bootstrap 后端增量**（GET /api/claude_cli/bootstrap → additional_model_options[{model,name,description}] → 磁盘缓存 isEqual 跳写 → 下次启动生效）+ 当前在用模型兜底追加 + 组织 allowlist 过滤 | modelOptions.ts:271-540, bootstrap.ts:63-137 |
| Picker 形态 | 单列平铺 Select **不分组**（排序隐含家族：Default→Opus→Sonnet→Haiku），min(10,n) 可见超出 "and N more…"，✓ 选中标记 + ❯ 焦点指示，Default (recommended) 永远首项（value=null 哨兵） | ModelPicker.tsx:135-136/263-310 |
| effort 行 | 焦点行联动 ←→ 循环 low/medium/high/max，不支持时显示提示 | ModelPicker.tsx:327-336 |
| 切换链路 | setAppState(mainLoopModel) → onChangeAppState diff 自动持久化 userSettings → getRuntimeMainLoopModel（session→flag→env→settings→内置默认五级）→ 请求体 model | model.tsx:47-57, onChangeAppState.ts:94-112 |
| 附带逻辑 | fast mode 自动关（不支持时）+ effort max 降级 + 切换反馈 "Set model to X" + /model ? 显示当前 | model.tsx:63-80, ModelPicker.tsx |

**对齐要点**：基准本质"本地清单优先、后端只做增量补充"，非实时 listModels；本项目语境的"后端获取"= **从引擎（进程内 IModelCatalog）取列表**，非 TUI 本地配置直读（青蛙TUI 的做法已被否决）。

### 本项目引擎侧现状（可直接复用的资产）

| 资产 | 位置 | 状态 |
|------|------|------|
| IModelCatalog（列表/别名/能力/窗口查询 12 方法） | modules/composition/slash/ai/model/IModelCatalog.cs | ✅ 接口齐备 |
| ModelCatalog 实现（[Register] Singleton，注入 IProviderDefinitionRegistry + IModelConfigLoader） | modules/composition/slash/ai/model/ModelCatalog.cs:7-8 | ✅ 待验证 GUI 会话链注册 |
| /model 命令全逻辑（切换+持久化+fast mode 自关+effort 降级+20 条历史） | modules/composition/slash/ai/model/ModelCommand.cs | ✅ 但交互用 TerminalHelper |
| SlashCommandRunner（TUI/GUI 共享执行链路，接 IServiceProvider） | modules/apps/engine/commands/slash/SlashCommandRunner.cs | ✅ Host 可直接调 |
| Host 引擎 DI 暴露 | native_tui/apps/NativeTui.Host/EngineSession.cs:28（Services => _engine.Services） | ✅ 已就绪 |
| ReplShell 模态对话框先例 | M7-3 PermissionCoordinator + BeginPermissionRequest 回调注入（ReplShell.cs:35/89/177） | ✅ 模式可照抄 |

### 核心冲突与方案对比

**核心冲突**：ModelCommand 的 ModelPicker 交互走 TerminalHelper.ReadKey/WriteRaw 直写终端——与 native_tui 的 raw mode + cell 网格 + 差分上屏渲染管线**根本冲突**（绕过渲染管线即破坏像素级对齐，同青蛙TUI 不可搬同理）。

| 方案 | 内容 | 优劣 |
|------|------|------|
| **A: TUI 原生 Picker（推荐）** | Core 加 ModelListItem DTO + ModelPickerCoordinator（照抄 M7-3 权限对话框模式：ReplShell 键盘模态 + driver 回调）；Host 侧 Submit 拦截 /model → Services.GetService&lt;IModelCatalog&gt;() 取列表 → BeginModelPicker；选中后委托 SlashCommandRunner 执行 `/model &lt;id&gt;`（复用全部切换逻辑） | UI 像素级对齐基准 Picker（✓/❯/Default 首项/10 条/effort 行）；切换逻辑零重写；Core 零依赖不受破坏 |
| B: 委托 ModelCommand 现成 Picker | Host 集成 SlashCommandRunner 直跑 /model | 零新 UI 代码；但 TerminalHelper 直写破坏渲染管线，非交互回退是文本列表——像素级对齐不可能 |
| C: 通用斜杠路由 + 交互命令特判 | 全部 / 命令走 SlashCommandRunner，仅 /model 类交互命令走 A 的原生 Picker | 覆盖面最大但超出 M7-6 范畴（通用斜杠执行属独立任务） |

<!-- M7-6 调研完成: 待用户确认方案(A/B/C)后实现 → 实现期前置验证: GUI 会话模块链 GetService<IModelCatalog> 是否可解析(实测,勿猜) -->

## M7-6 模型选择器落地（2026-10-21，用户决策"全部自研"）

### 方案决策（用户原话："全部自研啊，之前的功能说不定有什么隐藏的bug，我们自己干才是最好的"）

| 维度 | 落地选择 | 与调研方案 A 的差异 |
|------|---------|-------------------|
| UI 渲染 | 自研 ModelPickerDialog（IScrollItem + ReplShell 键盘模态） | 同 A |
| 数据来源 | 引擎进程内 IModelCatalog（后端获取） | 同 A |
| 切换链路 | **自研 ApplyModel**（SetPrimaryModel + IConfigurationService.SetAsync("model") 持久化 + IFastModeService 自关），**不经 SlashCommandRunner/ModelCommand** | A 原推荐委托 SlashCommandRunner 执行 /model &lt;id&gt;，用户否决——避免继承 TerminalHelper 隐藏 bug 面 |
| 装配方式 | ModelPickerEngine 装饰 IReplEngine 拦截无参 "/model"（带参透传），Attach(shell) 延迟绑定（new 后调，避免构造环） | A 未细化 |

### 实施记录（2 commit 链）

| 阶段 | commit | 内容 |
|------|--------|------|
| Core 层 | d0b07e441 | ModelPickerItem DTO + Coordinator（MaxVisible=10 窗口滚动 clamp）+ ModelPickerDialog（IScrollItem 渲染：边框/标题/副标题/列表行 ✓❯/描述/溢出 "and N more…"/快捷键行）+ ModelPickerEngine 装饰器（拦截+Attach+确认回调自持清单闭包）+ ReplShell 三方法（BeginModelPicker/CompleteModelPicker/PostInfo）+ HandleModelPickerKey 模态（优先级：权限对话框 > 模型选择器）；全量 672/672 绿（含 27 新测试） |
| Host/Demo 接线 | c56ae3dc3 | Host：ModelPickerAssembly（LoadItems 投影 IModelCatalog→ModelPickerItem + ApplyModel 三步切换链）+ pickerEngine 装配 + selftest 加模型清单断言（IModelCatalog 可解析实证）；Demo：RunInteractive 同装配 + DemoCommands 加 model 条目 + DemoModelItems 模拟清单 |

### 实现期实证

- **IModelCatalog 在引擎 DI 可解析**（Host selftest 断言实证，GUI 会话模块链注册确认——调研块存疑项闭环）
- 键名实证：IConfigurationService.SetAsync("model", id) 的 "model" 与 SettingsJson jsonName 一致（ModelCommand.ApplyModelSwitchAsync 同源）
- ModelPickerCoordinator 用**回调而非 TCS**（区别于权限对话框——无引擎侧阻塞等待方，选中即回调解册）
- 测试坑：IDE0007 强制 var → 可空捕获用 `var selected = new[] { (string?)"sentinel" }` 数组模式；RowText.Of 返回全宽含尾空格 → 行文本断言用 StartsWith

### 四列验收表（AGENTS.md 强制）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| Core ModelPicker.cs（Item DTO + Coordinator：MaxVisible=10/窗口滚动/clamp） | ModelPickerEngine 装饰器 + ReplShell.BeginModelPicker | ✅ | ✅ ModelPickerTests 7/7（Coordinator 窗口/边界） |
| Core ModelPickerDialog.cs（边框/标题/列表行 ✓❯/描述/溢出/快捷键行渲染） | ReplShell 滚动区对话框项 | ✅ | ✅ ModelPickerTests 9/9（渲染断言含溢出行/当前标记） |
| Core ModelPickerEngine.cs（"/model" 无参拦截 + Attach 延迟绑定 + 回调闭包） | Host/Demo Program.cs 装配（装饰 session.Client / SimulatedEngine） | ✅ | ✅ ModelPickerTests 5/5（拦截/透传/确认链） |
| Core ReplShell 模态（HandleModelPickerKey 优先级在权限后 + CompleteModelPicker 收尾） | 键盘路由 + ModelPickerEngine 确认回调 | ✅ | ✅ ModelPickerTests 6/6（模态打开/Esc 取消/Enter 确认）+ 全量 672/672 |
| Host ModelPickerAssembly（LoadItems ← IModelCatalog + ApplyModel 三步切换链） | Host Program.cs pickerEngine 装配 | ✅ | ✅ ADR 0080：Host --selftest 真实 exe 绿（含 IModelCatalog 可解析断言）；真实终端 /model 交互验收待用户跑 NativeTui.Host.exe 确认 |
| Demo 装配（DemoModelItems 模拟清单 + DemoCommands 加 model 条目） | Demo Program.cs RunInteractive | ✅ | ✅ ADR 0080：Demo --selftest 绿；真实终端 /model 交互验收待用户跑 NativeTui.Demo.exe 确认 |

### 基准对齐要点核对（对照调研块 Picker 形态行）

单列平铺不分组 ✅ / min(10,n) 可见 ✅（MaxVisible=10）/ 超出 "and N more…" ✅ / ✓ 当前标记 + ❯ 焦点指示 ✅ / 切换反馈（PostInfo 消息）✅ / fast mode 自关 ✅（ApplyModel 三步之一）。未对齐项：effort 行（基准 ModelPicker.tsx:327-336 的 low/medium/high/max 循环——本项目 effort 概念尚未进入模型配置，属后续里程碑）。

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 切换链路自研 ApplyModel 三步链(SetPrimaryModel + settings 持久化 + fast mode 自关)，不经 SlashCommandRunner -->
<!-- 原因: 用户明令"全部自研"避免继承 ModelCommand/TerminalHelper 隐藏 bug 面；且引擎侧切换逻辑只有 3 步，复刻成本低 -->
<!-- 替代方案: 调研方案 A 的 SlashCommandRunner 委托执行(被用户否决)；ModelPickerCoordinator 用回调而非权限对话框的 TCS 模式(无阻塞等待方) -->
<!-- 验证: 全量 672/672 绿 + Host/Demo selftest 真实 exe 双绿 ✅ -->

<!-- M7-6 完成: 用户真实终端 /model 选择器交互验收 → M7-7 多子代理分组(300 规模) → resize 事件化 → effort 行(后续里程碑) -->

## M7-7 多子代理分组调研（2026-10-21，基准源码 + 本项目渲染现状双 explore）

### 基准机制真相（claude-code-rev-main 全读结论）

| 项 | 基准实现 | 文件:行号 |
|----|---------|----------|
| 分组数据结构 | `GroupedToolUseMessage {toolName, messages[], results[]}`；分组键 = `${messageId}:${toolName}`，**同一条 assistant API 响应内 ≥2 个同类 tool_use 才聚合**；verbose 跳过分组 | utils/groupToolUses.ts:54-181, 76, 91 |
| 分发接口 | Tool.ts 可选 `renderGroupedToolUse`，**目前仅 AgentTool 实现** | Tool.ts:678-694, messageActions.tsx:121 |
| 组头三态 | `● Running N {type} agents…`（运行中，圆点闪烁）/ `N {type} agents finished`（全完成，绿）/ `N background agents launched (↓ manage)`（全后台）；全组同类型时组头带类型名、行内隐藏类型 | AgentTool/UI.tsx:732-756, ToolUseLoader.tsx:11-41 |
| 逐代理行 | 两行式：`├─ Type (desc) · N tool uses · M tokens` + `│  ⎿ <lastToolInfo\|Initializing…\|Done>`，最后一行 `└─`/`   ⎿`；运行中整行 dim | AgentProgressLine.tsx:23-135 |
| 组内规模 | **组内行数无截断**（N 代理 N 行），靠消息级虚拟化 + 行级 React memo + OffscreenFreeze 静态冻结扛 | Messages.tsx:307, VirtualMessageList.tsx:289-343 |
| 状态聚合 | 组级 anyUnresolved/anyError/allComplete → 组头文案与 loader 颜色（错误红）；**无数值汇总**（无 "N succeeded, M failed" 字样），失败仅组级视觉 | AgentTool/UI.tsx:728-737 |
| 单代理进度截断 | 行内只显示最后 3 条工具活动 + `+N more tool uses (ctrl+o to expand)` | AgentTool/UI.tsx:33, 564-567 |
| agent 配色 | 8 色板（red/blue/green/yellow/purple/orange/pink/cyan）按 agentType 全局分配，general-purpose 无色 | agentColorManager.ts:14-50 |
| 数据流 | 子代理每条消息 → `agent_progress` 流式 append 进主消息数组（**禁止替换**否则卡死 Initializing）；UI 每次 commit 整组重算整组替换 | AgentTool.tsx:795-804, REPL.tsx:2647-2650 |
| 小终端自适应 | 行数 < 并发数×9+7 时单代理进度塌缩单行 `In progress… · N tool uses · M tokens` | AgentTool/UI.tsx:181-182, 469-503 |

**关键边界**：分组单元是"一次 API 响应内的 tool_use 批次"——300 个子代理跨多条 assistant 消息派发会拆成多组。

### 本项目现状（可复用资产 + 核心缺口）

| 资产 | 位置 | 状态 |
|------|------|------|
| ModelPicker "Coordinator + Dialog 投影"整套模式（窗口/HiddenCount/"and N more…"/clamp） | repl/ModelPicker.cs:17-99 + ModelPickerDialog.cs:13-84 | ✅ 分组项直接模板 |
| ReplBridgeDriver 唯一翻译层（Apply switch） | bridge/ReplBridgeDriver.cs:48-111 | ✅ 接线点明确 |
| **Theme 子代理八色板（已定义已赋值零消费者）** | Theme.cs:107-130, :259-266 | ✅ 就等消费 |
| SpinnerAnimator 时钟注入 + Render 自绘 | ReplShell.cs:690-692 + SpinnerItem.cs:105-116 | ⚠️ 单时钟硬编码给 spinner，需扩展广播 |
| FooterState.AgentsHint 提示位 | FooterState.cs:19 + FooterBar.cs:41 | ✅ 已埋 |
| 溢出行文案先例（" and N more…" dim / "… +N lines"） | MessageRenderer.cs:223-228, :95-99 | ✅ 照抄 |

**核心缺口**：① BridgeEvent 无子代理粒度事件（ToolCall 仅 Name+Args，tool 名 "Task" 与普通工具同路径——300 子代理=1200 行逐条刷屏，即本任务要解决的问题）；② 无消息级虚拟化（inline 模式全量屏 EnsureScreens，300 组行=600+ 行 Screen，分组折叠同时是性能需求）；③ ScrollBox 无"替换/合并已有项"API（分组项需原地合并非追加）；④ SimulatedEngine 单阶段 int 状态机，并行子回合需改列表。

### 方案对比（组内 300 行渲染策略）

| 方案 | 内容 | 优劣 |
|------|------|------|
| **A: 组内可见窗口（推荐）** | SubagentGroupItem 复用 ModelPicker 模式：MaxVisible 常量 + WindowStart 滚动 + " and N more…" 溢出行 | 性能稳（高度 O(MaxVisible) 不随 N 膨胀，300 规模下 inline 全量屏不炸）；与基准"组头+逐行"视觉一致；**有意偏离基准**（基准组内不截断，但靠 React 虚拟化扛——我们没有虚拟化，窗口是对等机制） |
| B: 组内全量渲染 | 对齐基准 N 代理 N 行全渲染 | 像素级对齐最彻底；但 300×2 行全量屏每帧 O(n) 测量+渲染，且 ScrollBox 内容仍在 sticky 视口外仍测量；性能风险实际存在 |

**分组语义（自主决策）**：本项目 bridge 协议无 messageId 概念，采用**按 turn 分组**——TurnStarted 建组、TurnCompleted 定格（一组 = 一轮用户提交的全部 Task 类子代理）。基准的 messageId 分组跨轮也拆组，语义近似（一次响应 ≈ 一轮内的一个批次），且 turn 边界事件已存在协议中，零新增协议负担。

<!-- M7-7 调研完成: 待用户确认组内渲染策略(A窗口/B全量)后实现 → 事件协议加子代理粒度事件(3个) + SubagentGroupCoordinator/Item + 时钟广播 + Demo agents 演示命令 → Host 接线待引擎侧子代理事件源确认后置 -->

## M7-7 多子代理分组落地（2026-10-21，方案 A 可见窗口全链贯通）

### 实施记录（2 commit 链）

| 阶段 | commit | 内容 |
|------|--------|------|
| Core 层 | 6fa2d10cc | SubagentEntry + SubagentGroupCoordinator（事件驱动状态机 + FinalizePending 回合定格 + 窗口 min(10,n) **活动跟随滚动** — 区别于模型选择器的焦点跟随，分组非模态无焦点）+ SubagentGroupItem（两行式树形 ├─/└─ + "│ ⎿"/" ⎿" 活动行 + Initializing…/Done 状态 + and N more 溢出复用）+ MessageRenderer 三方法（组头●三态色：任一错误 Error/全完成 Success/运行中 Suggestion + Running N {type} agents 文案 + FormatTokens "N.Nk"）+ Theme.SubagentColor（**八色板首个消费者**，FNV-1a 稳定哈希）+ BridgeEvent 3 事件（SubagentStarted/ToolUse/Finished，参数名 TokenGain 避开嵌套类型撞名）+ Driver 惰性建组路由（回合定格脱离 + 多回合多组共存）；全量 694/694 绿（含 22 新测试） |
| Demo 接线 | 14619cd87 | SimulatedEngine 加 Subagents 脚本（**独立并行子回合列表** — 区别于主回合三段状态机；交错时间线 i×25ms 演示窗口活动跟随；agents 默认 12 / agents N 1..999；每第 7 个失败演示组头红态）+ DemoCommands/种子提示加 agents 条目；全量 698/698 绿（含 4 新 engine 测试）+ Demo selftest 绿 |

### 与基准的对齐与偏离（决策记录）

| 项 | 基准 | 本项目 | 依据 |
|----|------|--------|------|
| 组内 300 行渲染 | 不截断，React 虚拟化扛 | **可见窗口 min(10,n) + "and N more…"** | 方案 A 用户确认 — 本项目无虚拟化（inline 全量屏），窗口是对等机制 |
| 分组键 | messageId+toolName（一次 API 响应内 ≥2） | **按 turn**（TurnStarted~TurnCompleted 归一组） | bridge 协议无消息边界；turn 边界事件已存在零新增协议负担；基准跨轮拆组语义近似 |
| 组头动画 | ● 闪烁（ToolUseLoader + useBlink 600ms 方波） | **同基准：600ms 方波 ●↔空格 + IAnimatable 时钟广播（全实例同步闪烁）** | bab57cbfc 落地 — ReplShell 每拍广播 AnimTimeMs 给滚动区全部 IAnimatable（基准共享动画时钟等价物）；运行中色修正为 dim 默认前景（基准 isUnresolved → undefined+dimColor，原 Suggestion 系误用）；错误/完成恒亮不闪（基准 t1 isError 优先） |
| 错误汇总 | 无数值汇总（loader 红 + 行级呈现） | 同基准（● Error 红 + 行级 Done） | 一致对齐 |
| 状态聚合 | anyUnresolved/anyError/allComplete 组头三态 | 同基准 | 一致对齐 |
| 同型去重 | allSameType 组头带类型名 + 行内 hideType | 同基准 | 一致对齐 |
| 子代理事件流 | agent_progress 流式 append + UI 整组重算 | 事件路由组条目**原地更新** | ScrollBox 每帧全量重渲 = 无需失效通知，Coordinator 更新即生效 |

### 四列验收表（AGENTS.md 强制）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| Core SubagentGroup.cs（Entry + Coordinator：事件驱动/FinalizePending/窗口活动跟随） | ReplBridgeDriver 路由 + SubagentGroupItem 投影 | ✅ | ✅ SubagentGroupTests 9/9（状态机/聚合/窗口/断言） |
| Core SubagentGroupItem.cs + MessageRenderer 三方法（组头三态/树形两行式/溢出） | Driver 建组落位（AddMessage Insert spinner 前） | ✅ | ✅ SubagentGroupTests 8/8（渲染断言含树形收口/dim/八色/溢出） |
| Core Theme.SubagentColor（八色板 FNV-1a 稳定取色） | RenderSubagentStatusRow 类型段 | ✅ | ✅ 渲染测试样式断言（SubagentColor("explore") 取色一致） |
| Core BridgeEvent 3 事件 + Driver 路由（惰性建组/回合定格脱离/多组共存） | SimulatedEngine agents 脚本（Demo）+ Host 未来接线 | ✅ | ✅ SubagentGroupTests 4/4（Driver 路由）+ 全量 698/698 |
| Core IAnimatable 接口 + ReplShell.AdvanceAnimClock 广播（50ms/拍喂全部动画项） | SpinnerItem（glimmer/shimmer 相位）+ SubagentGroupItem（组头 ● 600ms 方波闪烁） | ✅ | ✅ ReplShellTests 广播同步断言（spinner 与注入项同值）+ SubagentGroupTests 4/4 相位断言（0 亮/600 暗/错误完成恒亮）；全量 714/714 |
| MessageRenderer.RenderSubagentHeader 闪烁三态（运行中 dim 闪烁 / 错误恒红 / 完成恒绿） | SubagentGroupItem.Render 投影（AnimTimeMs 相位） | ✅ | ✅ 部分错误+运行中 dim 恒亮（基准 isUnresolved 色优先）断言；真实终端闪烁视觉验收归用户 |
| SimulatedEngine Subagents 脚本（并行子回合交错时间线） | Demo agents 命令（真实终端输入 agents / agents 300） | ✅ | ✅ SubagentGroupTests 4/4（虚拟时钟确定性：批量启动/交错活动/完成收束/300 规模一拍批量）+ Demo --selftest 绿；真实终端 agents 交互验收待用户跑 NativeTui.Demo.exe 确认 |
| Host 接线 | — | ⏸ 后置 | 引擎侧子代理事件源未确认（引擎是否发 subagent_* 事件待实测），Core 事件契约已就绪 |

### 遗留项

1. Host 接线（前置：引擎侧子代理事件源实测确认）
2. 基准 P2 细节未对齐：ctrl+o transcript 展开模式 / 小终端自适应单行塌缩 / lastToolInfo "Searched/Read N files" 聚合摘要 / 失焦暂停闪烁（基准 useBlink useTerminalFocus）
3. 用户真实终端验收（agents 12 默认 + agents 300 规模 + 组头闪烁视觉）

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 组内可见窗口(方案A用户确认) + 分组语义按turn(bridge协议无messageId边界) + 组头静态三态色(闪烁归后续) + 窗口活动跟随(分组非模态无焦点,区别于ModelPicker焦点跟随) -->
<!-- 原因: 本项目无React虚拟化(inline全量屏O(n)测量),窗口+"and N more"是对等机制;turn边界事件已存在零协议负担;Coordinator原地更新+ScrollBox每帧全量重渲无需失效通知 -->
<!-- 替代方案: B组内全量渲染(用户否决,性能风险);messageId分组(协议无消息边界);TCS桥(无阻塞等待方,同ModelPicker回调先例) -->
<!-- 验证: 全量698/698绿(26新测试) + Demo selftest真实exe绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-21（组头闪烁补落） -->
<!-- 决策: IAnimatable 广播接口(ReplShell.AdvanceAnimClock 每拍50ms喂滚动区全部动画项) + 600ms方波●↔空格 + 运行中色 dim 默认前景 -->
<!-- 原因: 基准 useBlink 共享动画时钟(全实例同相位同步闪烁)的等价物=单源广播;基准 ToolUseLoader isUnresolved→undefined+dimColor 优先于 isError 色,原 Suggestion 实现系误用;墙钟(WallNowMs elapsed 暂停感知)与动画相位时钟(AnimTimeMs)语义分离,后者才广播 -->
<!-- 替代方案: Screen 挂帧时间(渲染缓冲承担双职责否决);每 item 自持 Stopwatch(无同步性+测试不确定性否决) -->
<!-- 验证: 全量714/714绿含5新测试(相位0亮/600暗/错误完成恒亮/部分错误dim恒亮/广播同步) + Demo selftest真实exe绿 ✅ -->

<!-- M7-7 完成: 用户真实终端 agents 交互验收(含闪烁视觉) → resize 事件化 → Host 接线(引擎侧事件源确认后) -->

