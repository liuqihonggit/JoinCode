# 任务：Slash 补全面板美化 —— 底部升起式设计

## 背景

用户反馈：GUI 命令补全对话框太丑，且不是从底部弹出来的；要求用截图方式验证渲染效果。

## 现状问题（InputBarView.axaml Popup）

| 问题 | 位置 |
|------|------|
| 浮空 Popup 悬在窗口中部，与输入栏无视觉关联 | `Placement=Top` 独立浮层 |
| 样式朴素：纯文本行、无图标、无模式标识、无键盘提示 | ListBox 默认外观 |
| headless 截图无法捕获 Popup 内容（独立弹层窗口），渲染验证困难 | `CaptureRenderedFrame` 只截主帧 |
| 无出现动画，突兀弹出 | — |

## 设计方案（Claude Code 风格底部命令面板）

1. **内联底部升起面板**：Popup → InputBarView 内 Grid 覆盖层，锚定输入栏正上方、
   与主列同宽，视觉上从输入栏背后向上滑出。
2. **结构三段式**：
   - 头部：模式徽章（⌘ 命令 / @ 文件 / # 工具 / 参数）+ 匹配计数
   - 列表：圆角行 + 选中左侧强调条 + 匹配前缀橙色高亮 + 描述右对齐
   - 底部：键盘提示 `↑↓ 选择 · Tab/↵ 补全 · Esc 关闭`
3. **动画**：Opacity 0→1 + translateY(16→0)，140ms CubicEaseOut。
4. **配色**：新增 `GuiPopupBackground`/`GuiPaletteShadowUp` token（暗 #232327 / 亮 #ffffff），其余复用 GuiPalette。

## 步骤

| 步骤 | 内容 | 状态 |
|------|------|------|
| 1 | 任务文档 | ✅ |
| 2 | 红测试：截图断言面板渲染于主帧内 + 底部锚定 + dumps 存 PNG | ✅ 红（Popup 独立弹层截不到） |
| 3 | VM：SlashModeLabel 计算属性 + NotifySlashPanelChanged 归纳 4 处通知 | ✅ |
| 4 | View：SlashPaletteView 独立组件 + MainWindow 覆盖层 + 动画 | ✅ |
| 5 | 编译 + 绿测试 + 全量 GUI 测试（334 全绿） | ✅ |
| 6 | 暗/亮主题截图人工核对（dumps/gui-slash/*.png） | ✅ |
| 7 | 设计文档更新 + git 提交 | ✅ |

## 踩坑记录

| 坑 | 根因 | 解法 |
|----|------|------|
| XAML 内联 `<Border.Transitions>` 运行时 NRE | XamlIlPopulate 对内联 Transitions 集合填充缺陷 | 改 code-behind 构造 |
| 合成器 Transitions 在 headless 不推进 | headless 无实时渲染循环 | Task.Delay 步进插值（UI 线程同步上下文回投） |
| Opacity=0 面板仍占布局把输入栏顶到窗口中部 | Opacity 不释放布局空间 | IsVisible 管占位 + 动画收尾再隐藏 |
| 面板在 UserControl 内被压成 4px | 内联覆盖层无法超出父布局边界 | 提取独立 SlashPaletteView 挂 MainWindow 主列覆盖层 |
| 测试中建议列表恒为空 | 直接改 VM 文本后防抖 tick 用未聚焦 TextBox 的 CaretIndex=0 重解析清空 | 测试走真实管线：设 TextBox.Text+CaretIndex |
| 选中行亮蓝色刺眼 | Fluent 模板 ContentPresenter 控制选中背景，ListBoxItem.Background 不生效 | `/template/ ContentPresenter#PART_ContentPresenter` 选择器覆盖 |

<!-- 🤖 Auto Decision: 2026-08-23 -->
<!-- 决策: Popup 改为 MainWindow 主列覆盖层内的独立 SlashPaletteView 组件 -->
<!-- 原因: ① Popup 独立弹层无法被 headless 截图验证；② UserControl 内联覆盖层无法超出父布局边界；覆盖层方案两端通吃 -->
<!-- 替代方案: AdornerLayer（定位计算复杂）、保留 Popup（截图不可验证，不采用）-->
<!-- 验证: 全量 GUI 334 测试全绿，暗/亮主题截图人工核对 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-23 -->
<!-- 决策: 动画用 Task.Delay 步进插值而非合成器 Transitions -->
<!-- 原因: headless 测试环境合成器时钟不推进，Transitions 永远停在首帧；Task.Delay 经 UI 线程同步上下文回投，两端行为一致且可测试 -->
<!-- 替代方案: DispatcherTimer(Render 优先级)（headless 下同样不触发，不采用）-->
<!-- 验证: 截图测试 300ms 等待后面板 opacity=1.00 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-23 -->
<!-- 决策: 新增 GuiPopupBackground/GuiPaletteSelectedRow 语义 token -->
<!-- 原因: 面板需比窗口底色略抬升制造层次；选中行用 accent 低饱和暗色调替代 Fluent 亮蓝，对齐配色单一数据源规范 -->
<!-- 替代方案: 硬编码十六进制（违反 GuiPalette 规范，不采用）-->
<!-- 验证: 暗 #232327/#2c3a4d 亮 #ffffff/#d8e4f2 截图核对 ✅ -->

## 涉及文件树

```
app/JoinCodeGui/
├── Theming/
│   ├── GuiPalette.cs            [改] 新增 PopupBackground token
│   └── GuiAppResources.cs       [改] 新增 GuiPaletteShadowUp
├── ViewModels/
│   └── MainViewModel.cs         [改] SlashModeLabel/SlashModeIcon + 通知归纳
└── Views/
    ├── InputBarView.axaml       [改] Popup → 内联底部面板
    └── InputBarView.axaml.cs    [改] 边距同步/动画/ScrollIntoView
tests/Unit/JoinCodeGui.Tests/
└── Views/
    └── SlashPaletteRenderTests.cs [新] 截图红测试 → 绿
docs/avalonia-host/
└── 任务-Slash补全面板美化.md      [新] 本文档
```
