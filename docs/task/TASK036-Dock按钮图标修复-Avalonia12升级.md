# Dock 按钮图标修复 + Avalonia 12.1.1 升级

> 用户需求：Dock 面板标题栏右上角按钮图标（▼📌✕）不可见，需修复

## 已完成（commit a137a0f70）

| 步骤 | 内容 | 状态 |
|------|------|------|
| 1 | Avalonia 11.3.20 → 12.1.1（Directory.Build.props） | ✅ |
| 2 | Dock 12.x 源码引用（ProjectReference 到 DockRef/src/） | ✅ |
| 3 | 移除 AvaloniaEdit 11.3.20（不兼容 12.1.1，IScrollable 移除） | ✅ |
| 4 | App.axaml 按官方 DockMvvmSample 标准重写 | ✅ |
| 5 | 22 个 Avalonia 12.x API 变化修复 | ✅ |
| 6 | **根因修复：GuiControlStyles.axaml 全局 Button 样式只保留 FontFamily** | ✅ |
| 7 | git commit a137a0f70 | ✅ |

## 根因分析

**问题**：Dock 面板标题栏按钮图标不可见

**根因链**：
1. `GuiControlStyles.axaml` 全局 `Button` 样式设了 `Background={GuiButtonBackground}`、`Foreground={GuiButtonForeground}`、`Padding=11,5`、`MinHeight=30`、`CornerRadius=6`
2. Dock chrome 按钮（PART_MenuButton/PART_PinButton/PART_CloseButton）用 `ChromeButton` ControlTheme，`Background=Transparent`
3. 全局 `Button` 样式优先级高于 ChromeButton ControlTheme，覆盖了 `Background=Transparent` 为不透明色
4. 图标用 `Path.Fill={DockChromeButtonForegroundBrush}`（浅色），按钮背景变成不透明浅色后，图标与背景同色不可见

**验证方法**（二分定位）：
- DockProbe2（官方副本）按钮可见 → 加全局 Button 白色背景样式 → 按钮不可见 → 确认根因
- 恢复样式打补丁可行但文档栏 tab × 按钮仍受影响 → 用户决定直接取消全局 Button 样式的覆盖属性

**修复**：全局 `Button` 样式只保留 `FontFamily`，不设 `Background`/`Foreground`/`Padding`/`MinHeight`/`CornerRadius`/`BorderBrush`/`RenderTransform`/`Transitions`

## 当前状态

- **编译**：✅ 0 错误，14 警告（TextBox.Watermark 过时，非阻塞）
- **运行**：✅ 窗口正常显示
- **Dock 按钮图标**：✅ ▼📌✕ 全部可见
- **git**：干净（commit a137a0f70）

## 待完成

| 优先级 | 内容 | 说明 |
|--------|------|------|
| 🔴 高 | 代码编辑器功能恢复 | AvaloniaEdit 移除后编辑器只有 TextBox，无语法高亮/行号/折叠。方案：卫星界面（独立进程用 Avalonia 11.3.x + AvaloniaEdit，IPC 调用） |
| 🟡 中 | 检查主工程按钮外观 | 全局 Button 样式取消 Background 后，无 class 的普通按钮用 FluentTheme 默认样式，需截图确认外观可接受 |
| 🟡 中 | TextBox.Watermark → PlaceholderText | 14 个过时警告，Avalonia 12.x API 变化 |

## 关键文件

| 文件 | 改动 |
|------|------|
| `Directory.Build.props` | AvaloniaVersion 12.1.1, DockVersion 12.1.1 |
| `app/gui/JoinCodeGui.csproj` | Dock 改 ProjectReference 到 DockRef，移除 AvaloniaEdit |
| `app/gui/App.axaml` | 官方标准：IDE 预设 + ThemeDictionaries + FluentTheme + DockFluentTheme |
| `app/gui/theming/GuiAppResources.cs` | 用 app.Styles.OfType<FluentTheme>()，ApplyTheme 设 RequestedThemeVariant |
| `app/gui/theming/GuiControlStyles.axaml` | **全局 Button 样式只保留 FontFamily** |
| `app/gui/views/EditorPanelView.axaml` | TextEditor → TextBox |
| `app/gui/views/WorkbenchWindow.axaml` | TextEditor → TextBox |

## 调试工具

| 工具 | 位置 | 说明 |
|------|------|------|
| DockRef | `D:\project\DockRef\` | 官方 Dock 源码（git clone，master 分支，Avalonia 12.1.1） |
| DockProbe2 | `tool/DockProbe2/` | 官方 DockMvvmSample 副本，按钮可见基线 |
| shot_generic.ps1 | `tool/shot_generic.ps1` | 通用截图脚本 |

<!-- 🤖 Auto Decision: 2026-10-04 -->
<!-- 决策: 全局 Button 样式只保留 FontFamily，不设 Background/Foreground/Padding/MinHeight/CornerRadius -->
<!-- 原因: 全局 Button 样式的 Background 覆盖 Dock chrome 按钮的 Transparent 背景，图标与背景同色不可见 -->
<!-- 替代方案: 追加恢复样式(ToolChromeControl /template/ Button)，但文档栏 tab × 按钮仍受影响，用户否决 -->
<!-- 验证: DockProbe2 二分定位确认根因，主工程编译通过+截图确认按钮图标可见 ✅ -->
