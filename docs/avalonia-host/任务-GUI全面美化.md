# 任务：GUI 全面美化（设计语言统一）

## 背景

Slash 补全面板改造完成（a0a2d71e8）后，用户要求"全部做剩下的任务，把 GUI 变得好看"。
对全部界面区域做设计语言统一：圆角体系、间距节奏、按钮分级、悬停反馈。

## 设计语言规范

| 维度 | 规范 |
|------|------|
| 圆角 | 控件 6 / 卡片 10 / 浮层 12 / 药丸 16 |
| 按钮分级 | 默认（实底+边框）/ ghost（透明，悬停浮起）/ primary（accent 实底白字）/ accentSubtle（accent 淡底 accent 字）/ pill（药丸形） |
| 间距 | 栏内 8 / 区块 12 / 页边 14 |
| 字号 | 提示 11 / 正文 12 / 强调 13.5 / 标题 20 |
| 悬停反馈 | 全部控件有 pointerover 态；危险操作（删除）默认隐藏悬停显现 |
| 配色 | 全部走 GuiPalette token，禁止硬编码十六进制 |

## 步骤

| 步骤 | 内容 | 状态 |
|------|------|------|
| A1 | token：AccentSubtle/AccentHover/AccentSubtleHover/CardHover + 共享控件样式 GuiControlStyles.axaml | ✅ |
| A2 | TopBar：ghost 按钮统一 + 分组 + 底部分隔线 | ✅ |
| A3 | Sidebar：会话卡片悬停态 + 删除按钮悬停显现 + 新建对话 accentSubtle | ✅ |
| A4 | InputBar：圆角输入框 + primary 发送按钮 + ghost 快捷按钮 | ✅ |
| A5 | SearchBar 紧凑布局 + StatusBar 模型药丸 | ✅ |
| A6 | EmptyState 药丸建议 + BackToBottom 浮动药丸 | ✅ |
| A7 | 编译 + 截图核对 + 提交 A（28525f866 界面骨架） | ✅ |
| B1 | 消息卡片：角色色条（MsgBarBrushConverter）+ 悬停显现操作按钮 + 圆角 10 | ✅ |
| B2 | 编译 + 截图核对 + 全量测试（335 全绿）+ 提交 B | ✅ |
| C1 | 设置面板：卡片分组 + 数值徽章 + ToggleSwitch + 关闭按钮（336 全绿） | ✅ |
| D1 | 红测试：补全面板边缘与输入栏几何对齐断言 + 发送按钮嵌入卡片断言 | ✅ |
| D2 | 补全面板重构：覆盖层(魔法 margin) → 布局行（与输入栏同列约束，物理零重叠） | ✅ |
| D3 | InputBar 重构：composer 卡片内嵌无边框 TextBox + 发送按钮嵌入卡片右下 | ✅ |
| D4 | 全局对齐：消息列表边距 12 与栏节奏统一；面板内容 padding 对齐 12 | ✅ |
| D5 | 编译 + 绿测试 + 截图核对 + 提交 D（338 全绿） | ✅ |

## 踩坑记录

| 坑 | 根因 | 解法 |
|----|------|------|
| AvaloniaXamlLoader.Load(Uri) 编译错误 IL2026 | 动态 XAML 加载破坏 NativeAOT 裁剪（项目强制 AOT） | 编译型 Styles 类（x:Class + partial class : Styles） |
| MainWindow 旧自定义 Button 模板覆盖新全局样式 | Window.Styles 局部样式优先级高于 App.Styles | 删除旧模板块，消除两套实现 |
| 会话卡片悬停态不生效 | Background 是转换器绑定（本地值），样式 Setter 无法覆盖 | 改 Classes.selected 类绑定 + 样式定义背景 |
| 截图像素断言颜色对不上 | CaptureRenderedFrame 是 RGBA 字节序，按 BGRA 读反了 | 按 [R,G,B,A] 读；亮暗主题角色色不同需参数化 |
| 补全面板压住输入栏一半且左右错位 10px | 覆盖层方案用魔法 margin(10,0,10,100) 猜输入栏高度，代码隐藏 SizeChanged 同步边距 | 布局行方案：面板与输入栏同列约束（Row2/Row3），对齐由布局系统保证，删除全部定位代码 |
| 几何断言 TransformToVisual 编译错 | Avalonia 11 返回 Matrix?（非 Point?） | `.GetValueOrDefault().Transform(new Point(0,0))` 取窗口坐标 |

<!-- 🤖 Auto Decision: 2026-08-23 -->
<!-- 决策: 共享控件样式放编译型 GuiControlStyles.axaml(Styles 子类) 而非 App.axaml -->
<!-- 原因: 真实 App 与 headless 测试共用 GuiAppResources.Register 单一入口；编译型类规避 IL2026 AOT 裁剪错误 -->
<!-- 替代方案: App.axaml 内联（测试宿主 VisualTestApp 不加载 App.axaml，两处漂移，不采用）-->
<!-- 验证: 335 测试全绿，暗/亮截图核对 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-23 -->
<!-- 决策: 消息卡片角色色条用 IMultiValueConverter(IsUser+Kind) 而非 VM 计算属性 -->
<!-- 原因: 零 VM 侵入；工具/思考/角色三档配色集中在转换器，主题切换随 DynamicResource 同步刷新 -->
<!-- 替代方案: ChatUiMessage.BarBrush 属性（主题切换后旧消息画刷过期，不采用）-->
<!-- 验证: 暗 #4DA6FF/亮 #1A6BC0 像素断言通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-24 -->
<!-- 决策: 补全面板从覆盖层改为布局行（MainWindow Row2，与输入栏 Row3 同列约束），仅上圆角+无下边框与输入栏融合 -->
<!-- 原因: 覆盖层魔法 margin 无法保证对齐（实测错位 10px+重叠）；同列约束让对齐成为布局系统不变量而非代码维护的约定；打开时消息区自然上顶符合"往上冒"语义 -->
<!-- 替代方案: TransformBounds 动态计算锚定（仍需监听尺寸变化同步，脆弱不采用）-->
<!-- 验证: 几何断言 |Left差|≤0.75 && Bottom≤Top+0.75 通过，338 测试全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-24 -->
<!-- 决策: 发送按钮嵌入 composer 卡片内部右下（TextBox 透明无边框 + :focus-within 点亮卡片边框），新增 ComposerBackground token -->
<!-- 原因: 用户明确要求"发送按钮嵌入发送栏内部右边靠齐"；现代聊天输入区范式（ChatGPT/Claude），动作行与输入区同一卡片视觉聚合 -->
<!-- 替代方案: 按钮悬浮 TextBox 右侧 overlay（遮挡文本，不采用）-->
<!-- 验证: Composer_SendButtonEmbeddedInCard 几何断言通过 ✅ -->

## 涉及文件树

```
app/JoinCodeGui/
├── Theming/
│   ├── GuiPalette.cs            [改] +4 token
│   ├── GuiAppResources.cs       [改] 挂载 GuiControlStyles
│   └── GuiControlStyles.axaml   [新] 共享控件样式（App 与 headless 测试共用）
├── Converters/
│   └── UiConverters.cs          [改] +MsgBarBrushConverter（MultiBinding IsUser+Kind）
└── Views/
    ├── TopBarView.axaml         [改] ghost 分组
    ├── SidebarView.axaml        [改] 会话卡片类名化
    ├── InputBarView.axaml       [改] primary 发送
    └── MainWindow.axaml         [改] 搜索栏/状态栏/EmptyState/消息卡片模板
tests/Unit/JoinCodeGui.Tests/
└── Views/
    └── GuiBeautifyRenderTests.cs [新] 带消息截图基线（暗/亮 + EmptyState）
docs/avalonia-host/
└── 任务-GUI全面美化.md           [新] 本文档
```
