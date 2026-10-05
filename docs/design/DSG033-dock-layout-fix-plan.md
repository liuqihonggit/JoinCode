# DSG033 — Dock 布局修复计划

## 弹性布局强制要求

**所有组件（无论内层还是外层）必须使用弹性布局，禁止任何绝对定位。**
- 定位用分块比例控制（Dock `Proportion`、Grid `*`/`Auto`）
- 禁止 `Canvas.Left`/`Canvas.Top` 绝对坐标
- 禁止用 `Margin` 做主要定位（Margin 仅用于间距）
- 固定宽度仅限边栏（如 ActivityBar 48px）和装饰元素（如色条 2-3px）

## 当前问题（截图分析 + 代码检查）

| # | 问题 | 严重度 | 根因 |
|---|------|--------|------|
| 1 | PanelView 内部 "输出/终端/问题" tab strip 与 Dock tab 重复 | P0 | PanelView.axaml 有自己的 tab strip，作为 TerminalTool 内容时双重 tab |
| 2 | HeaderTemplate 只为 PanelTool 配置 — InputBarTool/TerminalTool 标题不显示 | P0 | `DataType="dvm:PanelTool"` 未覆盖新 Tool 类型 |
| 3 | 底部面板空间不足 — 输入栏被压缩 | P1 | `Proportion = 0.3` 不够 + InputBarView 内系统日志占空间 |
| 4 | Dock tab 样式与 VSCode 差距大 | P1 | ToolTabStrip/ToolChromeControl 样式未充分定制 |
| 5 | 分割条不明显 | P2 | ProportionalDockSplitter 样式未定制 |

## 修复方案

### 阶段 1：修复双重 tab + HeaderTemplate（P0）

1. **PanelView 简化** — 移除内部 "输出/终端/问题" tab strip，只保留内容区（Dock tab 已提供切换）
2. **HeaderTemplate 扩展** — 为 InputBarTool 和 TerminalTool 添加 HeaderTemplate
3. **编译 + 截图验证**

### 阶段 2：Dock 样式 + 比例调整（P1）

1. **ToolDock(Bottom) Proportion** — 从 0.3 调为 0.35
2. **ToolDock(Left) Proportion** — 从 0.2 调为 0.22
3. **ToolTabStrip 样式** — 紧凑 tab，激活态下划线
4. **ProportionalDockSplitter 样式** — 1px 分割线，悬停变粗
5. **编译 + 截图验证**

### 阶段 3：ActivityBar 整合（P2）

1. **ActivityBar 图标点击** — 切换 Dock 左面板 active tab
2. **编译 + 截图验证**

## 执行顺序

1. ✅ 提取 MessageAreaView
2. ✅ DockFactory 完整布局
3. ✅ 全组件 Dock 化
4. 🔲 阶段 1：修复双重 tab + HeaderTemplate
5. 🔲 阶段 2：Dock 样式 + 比例调整
6. 🔲 阶段 3：ActivityBar 整合
