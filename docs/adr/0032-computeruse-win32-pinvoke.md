# 0032. ComputerUse P0 纯 Win32 P/Invoke

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
