# DSG032 — VSCode 风格 GUI 全局规划

> 状态：accepted  
> 日期：2026-10-02  
> 关联：[DSG031](DSG031-gui-topbar-inputbar-redesign.md) 顶栏+输入栏重设计

## 1. VSCode UI 架构（学习总结）

VSCode 工作区由 7 个核心区域组成，全部用 Grid 布局，禁止绝对定位：

```
┌─────────────────────────────────────────────────────┐
│ Title Bar (菜单栏 + 布局控制按钮)                      │
├───┬────────────┬──────────────────────┬─────────────┤
│ A │ Primary    │ Editor Group(s)      │ Secondary   │
│ c │ Side Bar   │ (标签页 + 代码编辑器)  │ Side Bar    │
│ t │            │                      │             │
│ i │            │                      │             │
│ v │            ├──────────────────────┤             │
│ i │            │ Panel (Terminal/     │             │
│ t │            │  Output/Problems)    │             │
│ y │            │                      │             │
├───┴────────────┴──────────────────────┴─────────────┤
│ Status Bar (左:文件信息/分支/错误 右:模式/编码/行列)    │
└─────────────────────────────────────────────────────┘
```

### 核心区域

| 区域 | 功能 | 当前状态 |
|------|------|---------|
| **Title Bar** | 菜单栏 + 布局控制按钮 + Customize Layout 下拉 | ✅ TopBarView |
| **Activity Bar** | 48px 图标列，切换视图，底部 Account/Manage | ✅ 已实现 |
| **Primary Side Bar** | 当前激活视图内容，可拖拽调整宽度 | ✅ 已实现+磁吸 |
| **Secondary Side Bar** | Primary 对侧，可拖拽放入视图 | ✅ 已实现 |
| **Editor Group** | 代码编辑器，多标签页，可分割 Grid | ✅ 已实现(内嵌) |
| **Panel** | Terminal/Output/Problems/Debug Console | ✅ 已实现 |
| **Status Bar** | 文件信息/分支/错误/模式/编码/行列 | ✅ StatusBarView |

### 关键交互模式

| 模式 | 说明 | 当前状态 |
|------|------|---------|
| 面板拖拽停靠 | 拖拽视图标题栏到上/下/左/右/浮动 | ✅ PanelView标题栏 |
| 面板调整大小 | 拖拽 sash 边缘调整大小 | ✅ Side Bar + Panel(四位置sash) |
| 面板折叠/展开 | 点击 Activity Bar 图标切换 | ✅ |
| 面板位置切换 | Panel 移到底部/右侧/左侧/顶部 | ✅ |
| Side Bar 位置切换 | Primary Side Bar 移到左侧/右侧 | ✅ 代码隐藏Grid.Column |
| 标签页管理 | 拖拽重排序/锁定/关闭/关闭其他/关闭全部/预览/中键关闭/滚轮切换 | ✅ |
| 命令面板 | Ctrl+Shift+P 快速访问所有命令 | ✅ |
| 快速打开 | Ctrl+P 快速打开文件 | ✅ |
| 面包屑导航 | 编辑器顶部文件路径+符号路径 | ✅ |
| Minimap | 代码缩略图 | ✅ 80px 缩略 |
| Sticky Scroll | 粘性滚动（嵌套作用域顶部固定） | ❌ 需深度集成渲染层 |
| 分屏编辑 | 并排编辑多个文件 | ✅ 左右两组+GridSplitter |
| Zen Mode | 全屏专注模式 | ✅ Ctrl+Shift+Z |
| 居中布局 | 居中编辑器 | ✅ |
| 浮动窗口 | 拖出标签页创建独立窗口 | ❌ 用户要求不弹窗 |
| 标签页预览模式 | 单击预览（斜体），双击固定 | ✅ |
| Secondary Side Bar | Primary 对侧边栏 | ✅ |
| 编辑器搜索 | Ctrl+F 查找替换 | ✅ SearchPanel.Install |
| 面板拖拽停靠 | 拖拽面板标题栏切换位置 | ✅ 方向按钮 |

## 2. 当前 GUI 已有功能

| 功能 | 位置 | 状态 |
|------|------|------|
| VSCode 风格菜单栏 | TopBarView | ✅ |
| Activity Bar 图标列 | MainWindow Column 0 | ✅ |
| Side Bar 可拖拽+磁吸 | MainWindow Column 1 | ✅ |
| 消息区（Markdown 渲染） | MainWindow Column 3 Row 0 | ✅ |
| 模型选择器 Popup | InputBarView | ✅ |
| 推理档位 Slider | InputBarView | ✅ |
| 设置面板 | SettingsPanelView | ✅ |
| 状态栏 | StatusBarView | ✅ |
| 主题切换（4 主题） | GuiPalette | ✅ |
| 目录树面板 | FileTreePanelView | ✅ |
| 会话列表面板 | SidebarView | ✅ |

## 3. 全局规划（按优先级）

### P0 — 核心功能（AI 编程工作区必需）

| # | 功能 | 说明 | 依赖 |
|---|------|------|------|
| 1 | **Editor Group** | AvaloniaEdit 代码编辑器，多标签页，打开/编辑/保存文件 | AvaloniaEdit（已安装） |
| 2 | **Panel 区域** | 底部面板，Terminal + Output + Problems 三标签 | 无 |
| 3 | **命令面板** | Ctrl+Shift+P 快速访问所有命令，Fuzzy 搜索 | 无 |
| 4 | **快速打开** | Ctrl+P 快速打开文件，Fuzzy 搜索文件名 | 目录树 |
| 5 | **面包屑导航** | 编辑器顶部显示文件路径 + 符号路径 | Editor Group |

### P1 — 重要功能（提升交互体验）

| # | 功能 | 说明 | 依赖 |
|---|------|------|------|
| 6 | **面板拖拽停靠** | 拖拽视图标题栏到上/下/左/右，面板位置可移动 | ✅ PanelView标题栏Pointer事件 |
| 7 | **浮动窗口** | 拖出标签页/面板创建独立窗口，Always on Top | Editor Group |
| 8 | **标签页管理** | 拖拽重排序、锁定(pinned)、关闭/关闭其他/关闭全部 | Editor Group |
| 9 | **Secondary Side Bar** | Primary 对侧边栏，可拖拽放入视图 | 布局系统重构 |
| 10 | **Panel 位置切换** | Panel 可移到上/下/左/右，对齐方式 Center/Justify/Left/Right | Panel |

### P2 — 增强功能（锦上添花）

| # | 功能 | 说明 | 依赖 |
|---|------|------|------|
| 11 | **Minimap** | 代码缩略图，可拖拽快速跳转 | AvaloniaEdit |
| 12 | **Sticky Scroll** | 嵌套作用域顶部固定显示 | AvaloniaEdit |
| 13 | **分屏编辑** | 并排编辑多个文件，Grid 布局 | Editor Group |
| 14 | **Zen Mode** | 全屏专注模式，隐藏所有 UI 只留编辑器 | 布局系统 |
| 15 | **居中布局** | 编辑器居中显示 | 布局系统 |
| 16 | **Compact Mode** | 浮动窗口紧凑模式，减少间距 | 浮动窗口 |
| 17 | **标签页预览模式** | 单击预览（斜体），双击固定 | Editor Group |
| 18 | **编辑器组锁定** | 锁定编辑器组，新文件不在锁定组打开 | Editor Group |

## 4. 布局系统设计

### 4.1 当前布局（固定 Grid）

```xml
<Grid RowDefinitions="Auto,*">           <!-- TitleBar : 下区 -->
  <TopBarView Grid.Row="0" />
  <Grid Grid.Row="1" ColumnDefinitions="48,Auto,6,*">  <!-- ActivityBar : SideBar : Sash : 主区 -->
    ...
  </Grid>
</Grid>
```

### 4.2 目标布局（可重组 Grid）

```xml
<Grid RowDefinitions="Auto,*,Auto">      <!-- TitleBar : 工作区 : StatusBar -->
  <TopBarView Grid.Row="0" />
  <WorkbenchView Grid.Row="1" />          <!-- 封装整个工作区，支持布局重组 -->
  <StatusBarView Grid.Row="2" />
</Grid>
```

**WorkbenchView** 内部根据布局配置动态重组：

```
默认布局:
┌───┬────────┬──────────────────┬───────────┐
│ A │ Side   │ Editor           │ Secondary │
│ B │ Bar    │ Group(s)         │ Side Bar  │
│   │        │                  │           │
│   │        ├──────────────────┤           │
│   │        │ Panel            │           │
└───┴────────┴──────────────────┴───────────┘

Panel 在右侧:
┌───┬────────┬──────────────────────────────┐
│ A │ Side   │ Editor          │ Panel       │
│ B │ Bar    │ Group(s)       │             │
│   │        │                │             │
└───┴────────┴────────────────┴─────────────┘
```

### 4.3 布局配置（VM）

```csharp
public enum PanelPosition { Bottom, Right, Left, Top }
public enum SideBarPosition { Left, Right }

public sealed partial class LayoutConfig {
    public SideBarPosition PrimarySideBarPosition { get; set; } = Left;
    public SideBarPosition SecondarySideBarPosition { get; set; } = Right;
    public PanelPosition PanelPosition { get; set; } = Bottom;
    public bool IsSecondarySideBarVisible { get; set; }
    public bool IsPanelVisible { get; set; }
    public bool IsZenMode { get; set; }
    public bool IsCenteredLayout { get; set; }
}
```

## 5. Editor Group 设计

### 5.1 结构

```
EditorGroupView
├── TabBar (标签页栏)
│   ├── Tab1 (pinned, locked)
│   ├── Tab2
│   └── Tab3 (preview, italic)
├── Breadcrumbs (面包屑导航)
└── AvaloniaEdit.TextEditor (代码编辑器)
    ├── LineNumbers
    ├── SyntaxHighlighting (TextMate)
    ├── CodeFolding
    ├── Minimap (右侧)
    └── StickyScroll (顶部)
```

### 5.2 多编辑器组 Grid

```
┌──────────┬──────────┐
│ Editor   │ Editor   │
│ Group 1  │ Group 2  │
│          │          │
├──────────┴──────────┤
│ Editor Group 3     │
└────────────────────┘
```

用 Avalonia `Grid` + `GridSplitter`（Pointer 事件实现）分割。

### 5.3 AvaloniaEdit 集成

```xml
<edit:TextEditor ShowLineNumbers="True"
                 FontFamily="Cascadia Code,Consolas,Menlo,Monospace"
                 IsReadOnly="{Binding IsReadOnly}"
                 TextChanged="OnTextChanged" />
```

TextMate 语法高亮：
```csharp
var registry = new RegistryOptions(ThemeName.DarkPlus);
var installation = textEditor.InstallTextMate(registry);
installation.SetGrammar(registry.GetScopeByLanguageId(
    registry.GetLanguageByExtension(extension).Id));
```

## 6. Panel 设计

### 6.1 三标签

| 标签 | 功能 | 实现 |
|------|------|------|
| Terminal | 集成终端（PTY） | Process + AvaloniaEdit 或自定义 |
| Output | 日志输出 | ScrollViewer + SelectableTextBlock |
| Problems | 错误/警告列表 | ItemsControl + 过滤 |

### 6.2 Panel 位置切换

用 `Grid` 行列动态调整：
- Bottom: `RowDefinitions="*,Auto"` Panel 在 Row 1
- Right: `ColumnDefinitions="*,Auto"` Panel 在 Column 1
- Top: `RowDefinitions="Auto,*"` Panel 在 Row 0
- Left: `ColumnDefinitions="Auto,*"` Panel 在 Column 0

## 7. 命令面板设计

```
┌─────────────────────────────────┐
│ > 输入命令名称...               │
├─────────────────────────────────┤
│ 📁 File: New File              │
│ 📁 File: Open File...          │
│ 🎨 View: Toggle Theme          │
│ 🔍 Search: Find in Files...    │
└─────────────────────────────────┘
```

- Ctrl+Shift+P 打开
- Fuzzy 搜索命令名称
- 命令注册：`ICommandRegistry.Register(name, handler, shortcut)`

## 8. 实现路线图

### 阶段 1：P0 核心功能

1. **Editor Group** — AvaloniaEdit 集成，单标签页，打开/编辑/保存
2. **Panel** — Output 标签（最简单），Terminal 后续
3. **命令面板** — 命令注册 + Fuzzy 搜索 + Popup
4. **快速打开** — 文件搜索 + Popup
5. **面包屑** — 编辑器顶部路径显示

### 阶段 2：P1 重要功能

6. **布局系统重构** — WorkbenchView 封装，Panel 位置切换
7. **标签页管理** — 多标签，拖拽重排序，锁定/关闭
8. **面板拖拽停靠** — 拖拽标题栏切换位置
9. **浮动窗口** — 拖出标签页创建独立窗口
10. **Secondary Side Bar** — 右侧边栏

### 阶段 3：P2 增强功能

11. **Minimap** — AvaloniaEdit 缩略图
12. **Sticky Scroll** — 粘性滚动
13. **分屏编辑** — Grid 多编辑器组
14. **Zen Mode** — 全屏专注
15. **居中布局** — 编辑器居中
16. **Compact Mode** — 浮动窗口紧凑

## 9. 技术约束

- **禁止绝对定位** — 全部用 Grid/StackPanel/Border 相对布局
- **AOT 兼容** — AvaloniaEdit 已标记 IsAotCompatible
- **性能** — 大文件用 AvaloniaEdit 行虚拟化
- **主题** — TextMate 主题跟随 GuiThemeVariant
- **不引入新依赖** — AvaloniaEdit + AvaloniaEdit.TextMate 已安装

## 10. 验收标准

| 功能 | 验收方式 |
|------|---------|
| Editor Group | 打开 .cs 文件，语法高亮正确，可编辑保存 |
| Panel | Output 标签显示日志，可切换位置 |
| 命令面板 | Ctrl+Shift+P 打开，Fuzzy 搜索，执行命令 |
| 快速打开 | Ctrl+P 打开，Fuzzy 搜索文件，打开文件 |
| 面包屑 | 显示文件路径，点击跳转 |
| 标签页 | 多文件打开，拖拽重排序，锁定/关闭 |
| 浮动窗口 | 拖出标签页创建独立窗口 |
| 面板拖拽 | 拖拽面板标题栏切换位置 |
| Minimap | 显示代码缩略图，可拖拽跳转 |

<!-- 🤖 Auto Decision: 2026-10-02 -->
<!-- 决策: 制定 VSCode 风格 GUI 全局规划，分 P0/P1/P2 三优先级 -->
<!-- 原因: 用户要求全面学习 VSCode 并做全局规划，需要系统性而非零散实现 -->
<!-- 替代方案: 逐个功能实现不做规划（风险：架构不一致、返工）-->
<!-- 验证: 规划文档已写入 docs/design/DSG032 ✅ -->
