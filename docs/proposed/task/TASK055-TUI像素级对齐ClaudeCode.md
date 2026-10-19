# TASK055: TUI 像素级对齐 Claude Code — 自研渲染框架

> 状态: in-progress（M1 未开始）
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

## 5. ⚠️ 待定决策：bridge 引擎接入方式（阻塞 M6，不阻塞 M1-M5）

- 选项 1：**进程协议** — native_tui.exe 通过 stdio JSON / 本地 socket 与 jcc.exe 通信，完全解耦（清理 modules 后仅需协议契约），对齐"CLI 纯非交互 + TUI 承担交互"的职责分离方向
- 选项 2：**进程内引用** — 直接引用 abstractions/engine 项目，简单但与 modules 耦合，未来清理需处理
- M1-M5（纯渲染框架+组件）不依赖此决策，可先行

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
