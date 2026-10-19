# ADR 0140: 自研 TUI 渲染框架 — 像素级对齐 Claude Code + 独立工程摆脱 submodule

> 状态: proposed
> 日期: 2026-10-20
> 关联: [TASK055 对齐任务文档](../task/TASK055-TUI像素级对齐ClaudeCode.md)、ADR 0137（modules 就近隔离，本工程例外：独立于 modules 体系）

## 1. 背景

当前 TUI（`modules/apps/tui/`，jcctui.exe）基于 **Terminal.Gui v2.4.17 NuGet** 构建，
仓库另有三个 git submodule fork（`libs/Terminal.Gui`、`libs/Editor`、`libs/Dock`，均未初始化）。
用户决定**未来清理掉所有 submodule**，且要求 TUI 与 Claude Code 达到**像素级对齐**：

1. **全组件自研** — 不依赖 Terminal.Gui 组件体系，Terminal.Gui 仅作参考实现；
2. **截图校准** — C# 实现与 Claude Code 存在视觉差异时，用同场景截图逐像素对比校准；
3. **动画帧级对齐** — spinner/shimmer 等大量动画按帧率逐帧对齐（120ms spinner / 50ms shimmer / 60fps 渲染）；
4. **独立工程** — 新工程不引用 `modules/` 任何项目、不依赖 `libs/` submodule，submodule 清理后可独立生存。

对齐基准源码（唯一视觉规范来源）：`D:\project\claude-code-rust\claude-code-rev-main\src\`
（Claude Code npm 包 source map 还原版，React + 深度定制 Ink fork + Yoga）。

## 2. 决策

### 决策 1: 渲染架构自研，复刻 Ink fork 的五段式管线

新工程 C# 复刻还原版 Ink 的渲染管线（非 npm 原版 Ink）：

```
组件树（自研声明式或命令式组件）
   ▼ 布局引擎（flexbox 子集）
cell 网格 Screen（CharPool/StylePool/HyperlinkPool 三 intern 池，宽字符 SpacerTail/SpacerHead）
   ▼ 逐 cell diff + patch optimize
Patch 列表 → ANSI 字节流（DEC 2026 BSU/ESU 原子包裹 + DECSTBM 硬件滚动）
   ▼ 60fps 节流（16ms throttle leading+trailing）
终端 stdout
```

核心对齐点：`src/ink/`（screen.ts / frame.ts / log-update.ts / renderer.ts / termio/）。

### 决策 2: Terminal.Gui 定位从"框架依赖"降级为"参考实现"

- 新工程**零依赖** Terminal.Gui（NuGet 与 submodule 均不引用）；
- 参考其 API 设计（View 树、Application 生命周期、ConsoleDriver 分层、keypad 处理），
  尤其 Windows ConPTY/Win32 控制台 API 的实战处理；
- 现有 `modules/apps/tui/`（jcctui）保留不动，新工程成熟后替换。

### 决策 3: 三层校准体系（像素级对齐的验收基建）

| 层 | 机制 | 对齐对象 |
|----|------|---------|
| L1 cell golden | 离屏渲染 → Screen cell 网格序列化 → 与期望快照对比（参考还原版 `renderToScreen` 设计） | 颜色/符号/布局/文本 |
| L2 截图像素级 | 真实终端（Windows Terminal）运行新 TUI 与 Claude Code 同场景 → 截图 → 逐像素 diff 报告 | 最终视觉效果 |
| L3 动画帧级 | 注入虚拟时钟 → 第 N 帧必须等于期望帧（spinner 120ms/帧、shimmer 50ms/帧、渲染 60fps） | 动画时序 |

L2 截图校准工具随工程交付（截图 → 对齐 → 区域 diff → 差异报告），
发现的差异回流 L1 golden 用例固化。

### 决策 4: 独立工程位置与边界

- 位置：仓库顶层新目录 `native_tui/`（全小写下划线，符合目录命名规范）；
- 自带独立 `native_tui.slnx`，可脱离主仓库解决方案独立编译；
- 不引用 `modules/*`、不引用 `libs/*`；允许引用 NuGet（须 AOT 兼容，遵守 nuget 约束）；
- 与引擎（LLM 会话/工具调用/权限交互）的接入方式 = **待定决策**（进程协议 vs 进程内引用，见 TASK055 待定项）；
- 未来 modules/submodule 清理时本工程不受影响。

### 决策 5: 视觉规范唯一数据源

- 主题系统对齐 `src/utils/theme.ts`：80+ 语义色键 × 6 套主题（dark/light/两套色盲/两套 ansi）+ auto；
- 符号常量对齐 `src/constants/figures.ts` 全表（`⏺` darwin/`●` 其他平台的差异保留）；
- spinner 对齐 `·✢✳✶✻✽` 往返弹跳序列（非盲文 ⠋⠙），停滞向 `rgb(171,43,63)` 插值变红；
- 布局范式对齐 `src/components/FullscreenLayout.tsx`：无顶栏沉浸式对话流，信息沉底。

## 3. 影响面

| 项 | 影响 |
|----|------|
| 新增工程 | `native_tui/` 顶层目录（渲染框架 + 组件 + REPL 应用 + 三层校准测试） |
| 现有 TUI | `modules/apps/tui/` 不动，成熟后由新工程替换并归档 |
| submodule 清理 | 新工程零依赖，清理 `libs/Terminal.Gui`/`Editor`/`Dock` 无影响 |
| CI | 新工程纳入独立 workflow（不进 ci-build.yml 的 16 域序列，避免与 modules 耦合） |
| 风险 | 工程量大（估 M1-M7 七个里程碑，见 TASK055）；ConPTY/Win32 控制台细节需参考 Terminal.Gui 实战经验 |

## 4. 放弃的替代方案

1. **Terminal.Gui 深度定制（保留框架，只重构视觉层）** — 工程量小、渐进式，
   但组件体系受框架约束：shimmer 50ms RGB 插值/borderText/单边圆角边框等需 hack 实现，
   且无法摆脱 NuGet/submodule 依赖，与"清理 submodule + 全组件自研"目标冲突。用户明确否决。
2. **在 `modules/apps/tui/` 原地重写** — 与 modules 体系耦合（module.slnx、InternalsVisibleTo、
   EngineSessionFactory 组装链），未来清理 submodule/modules 时被牵连。放弃。
3. **fork Ink 的 C# 移植版（如现有 Gui.cs 类项目）** — 调研未发现维护良好的 AOT 兼容 C# 等价物；
   且 Claude Code 的 Ink 是深度魔改 fork（cell diff/DEC 2026/鼠标均为私有改造），任何现成移植都不对齐。放弃。

## 5. 验收

见 [TASK055](../task/TASK055-TUI像素级对齐ClaudeCode.md) 验收标准：
每里程碑 TDD（L1 golden 先行）+ M6 起截图校准 + ADR 0080 手动 exe 验收。
