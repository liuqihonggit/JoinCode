# 0032. Win32 P/Invoke 桌面能力总览(ComputerUse P0 + 四叉树叠加延伸)

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

ComputerUse P0 需要桌面输入能力（鼠标点击、键盘输入、截图、窗口枚举）。可选方案：Win32 P/Invoke（`user32.dll`/`gdi32.dll`）或 UIAutomation API。项目强制 NativeAOT（ADR 0002），UIAutomation 的 AOT 兼容性未验证。

## 决策

**P0 纯用 Win32 P/Invoke（SendInput/EnumWindows/BitBlt），不依赖 UIAutomation。**

- SendInput：鼠标/键盘输入
- EnumWindows：窗口枚举
- BitBlt + GetDIBits + ImageSharp：截图
- FocusAsync 加 Alt 键技巧：解除 Windows SetForegroundWindow 前台锁定限制
- 截图 alpha 通道手动设 255：GetDIBits 32位 BI_RGB 的 alpha 可能是 0 导致透明

UI 元素语义检测留给 P1（截图 + 多模态 LLM）。

定位文件：`docs/design/ComputerUse-P0-DesktopInput-Design.md`、`docs/design/ComputerUse-P0-Acceptance.md`

## 替代方案

1. **用 UIAutomation**：放弃。NativeAOT 兼容性未验证（风险🔴），P/Invoke 是 AOT 确定兼容。
2. **用 System.Drawing.Common**：放弃。.NET 5+ 中 System.Drawing.Common 仅 Windows 且 AOT 兼容性差，改用 ImageSharp。
3. **跨平台抽象（Windows/Linux/macOS）**：放弃。P0 聚焦 Windows，跨平台留给后续优先级。

## 后果

- 正面：AOT 确定兼容；无额外依赖；性能好（直接调用 Win32 API）
- 负面：仅 Windows；P/Invoke 声明需手动维护；UI 元素语义检测需 P1 多模态 LLM
- 中性：P/Invoke 目录用 `Native/` 而非 `Win32/`（因 .gitignore 第63行 `[Ww][Ii][Nn]32/` 规则忽略 Win32 目录）

## 延伸应用:四叉树桌面叠加复用 GDI(2026-09-12 补充)

### 背景

四叉树(Vision 子系统 M1)现有可视化仅限"在图片像素上画虚线网格 → 返回 base64 PNG"(`quadtree_render`/`screen_indicate`),不在桌面实际叠加显示。`screen_indicate` 工具注释明说"此工具只在图片上画框返回,不在桌面上实际高亮"。

桌面实际高亮由独立工具 `show_desktop_overlay`(`core/execution/Hands/src/ToolHandlers/Handlers/DesktopTools/DesktopOverlayToolHandlers.cs`)实现,用 GDI 在桌面画框,超时自动清除。但它接受原始像素坐标 `(x, y, width, height)`,不消费四叉树编码/网格。

缺口:没有"四叉树编码 → 屏幕像素坐标 → 桌面叠加显示"的桥接层,用户无法在界面上看到四叉树框框叠加在真实屏幕上。

### 决策

**复用 show_desktop_overlay 的 GDI 能力,新建四叉树编码 → 屏幕坐标转换层,不新建 WPF/Avalonia 透明窗口叠加组件。**

- 转换层:四叉树格子坐标 `(x, y, w, h)` + 原图在屏幕的位置 → 屏幕绝对像素坐标 → 调 `show_desktop_overlay`
- GDI 画框:实线/虚线由 GDI pen style 控制,alpha 强度通过 GDI 半透明画刷近似(不追求 SkiaSharp 级渲染质量)
- 多层网格:按深度逐层画框,每层不同颜色/线宽

### 替代方案

1. **新建 WPF/Avalonia 透明窗口叠加**:放弃。支持虚线/alpha/多层/实时缩放,视觉效果对齐 quadtree_render 的 SkiaSharp 规格,但:① 引入 GUI 框架依赖到 Vision/Hands 层,破坏七层架构(Vision 在 services 层,GUI 在 composition/app 层)② 工作量大 ③ 与 0032 的 Win32 P/Invoke 路线不一致
2. **扩展 quadtree_render 返回桌面坐标而非 base64**:放弃。改变工具契约,破坏 LLM 消费链

### 后果

- 正面:复用 0032 GDI 能力,不引入新依赖;七层架构不破坏;快速落地
- 负面:GDI 不支持 SkiaSharp 级虚线/alpha 渲染质量;多层网格性能不如透明窗口一次绘制
- 中性:四叉树桌面叠加是"辅助可视化",非"精确渲染",GDI 质量足够

### 实现状态

- [x] 转换层:四叉树编码 → 屏幕坐标(`QuadtreeDesktopOverlayMapper`,7 测试通过)
- [x] MCP 工具:`quadtree_to_screen_rects`(`QuadtreeDesktopOverlayToolHandlers`,返回屏幕坐标 JSON,LLM 调 show_desktop_overlay 画框)
- [x] 单元测试:13 测试通过(7 转换层 + 6 工具处理器)
- ADR 0032 范围扩展:从"ComputerUse P0 桌面输入"→"Win32 P/Invoke 桌面能力总览(含四叉树叠加延伸应用)"
