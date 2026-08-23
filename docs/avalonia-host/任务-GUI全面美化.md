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
| A1 | token：AccentSubtle/AccentHover/AccentSubtleHover/CardHover + 共享控件样式 GuiControlStyles.axaml | ⏳ |
| A2 | TopBar：ghost 按钮统一 + 分组 + 底部分隔线 | ⏳ |
| A3 | Sidebar：会话卡片悬停态 + 删除按钮悬停显现 + 新建对话 accentSubtle | ⏳ |
| A4 | InputBar：圆角输入框 + primary 发送按钮 + ghost 快捷按钮 | ⏳ |
| A5 | SearchBar 紧凑布局 + StatusBar 模型药丸 | ⏳ |
| A6 | EmptyState 药丸建议 + BackToBottom 浮动药丸 | ⏳ |
| A7 | 编译 + 截图核对 + 提交 A（界面骨架） | ⏳ |
| B1 | 消息卡片：角色色条（MsgBarBrushConverter）+ 悬停显现操作按钮 + 圆角 10 | ⏳ |
| B2 | 编译 + 截图核对 + 全量测试 + 提交 B（消息区） | ⏳ |

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
