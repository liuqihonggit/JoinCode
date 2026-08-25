# P0 技术设计：桌面输入模拟底座

> **关联 PRD**：`ComputerUse-PRD.md` §4 P0、§5.1 架构落点
> **里程碑**：M1
> **范围**：Win32 桌面输入模拟（鼠标/键盘/窗口/截图采集）+ 工具暴露 + 安全护栏埋点
> **状态**：草案 / 待评审
> **日期**：2026-08-25

---

## 1. 概述

P0 是 Computer Use 的「手」——所有上层能力（视觉理解/环境感知/观察学习）都依赖它落地。本设计在 JoinCode 七层架构中新增**桌面输入模拟层**，复用已验证的 Win32 P/Invoke 路径（`PreventSleepService.cs:104` 已证明 kernel32 P/Invoke + NativeAOT 可行）。

**核心决策**：P0 纯用 Win32 P/Invoke（`user32.dll`/`gdi32.dll`），**不依赖 UIAutomation**。原因：UIAutomation 的 NativeAOT 兼容性未验证（风险🔴），而 SendInput/EnumWindows/BitBlt 是纯 P/Invoke，AOT 确定兼容。UI 元素语义检测留给 P1（截图 + 多模态 LLM）。

---

## 2. 架构落点（目录树）

```
foundation/Abstractions/03-hands/Desktop/          ← 抽象接口（新增）
├── IDesktopInputService.cs        鼠标键盘模拟
├── IWindowManagementService.cs    窗口枚举/激活/移动/关闭
├── IScreenCaptureService.cs       截图采集（P1 复用）
├── DesktopOperation.cs            操作原子单元（record）
├── DesktopOperationLog.cs         操作日志（为 P4/P5 铺垫）
├── MouseAction.cs                 鼠标动作枚举
├── KeyModifier.cs                 键盘修饰键枚举
├── WindowInfo.cs                  窗口信息（record）
└── UnsafeOperationKind.cs         不可逆操作分类枚举（U-01 埋点）

core/execution/Hands/src/Desktop/                  ← Win32 实现（新增）
├── Win32/
│   ├── User32NativeMethods.cs     user32.dll P/Invoke 声明
│   ├── Gdi32NativeMethods.cs      gdi32.dll P/Invoke 声明
│   ├── NativeInputStructs.cs      INPUT/MOUSEINPUT/KEYBDINPUT 结构
│   └── NativeConstants.cs         常量（INPUT_MOUSE/WM_*等）
├── Win32DesktopInputService.cs    IDesktopInputService 实现
├── Win32WindowManagementService.cs IWindowManagementService 实现
├── GdiScreenCaptureService.cs     IScreenCaptureService 实现（BitBlt）
└── DesktopOperationLogger.cs      操作日志写入

core/execution/Hands/src/ToolHandlers/Handlers/DesktopTools/  ← 工具暴露（新增）
├── DesktopInputToolHandlers.cs    mouse_click/move/drag + key_press/type_text
└── WindowManagementToolHandlers.cs list_windows/focus/move/close/screenshot
```

**依赖链**：`Abstractions(03-hands/Desktop)` → `Core(Hands/Desktop)` → `Core(Hands/ToolHandlers/DesktopTools)`，不跨层，不破坏七层编译顺序。

---

## 3. Abstractions 层接口定义

### 3.1 IDesktopInputService

```csharp
namespace Abstractions.Hands.Desktop;

/// <summary>
/// 桌面输入模拟服务 — 鼠标键盘事件注入（Win32 SendInput 封装）
/// </summary>
public interface IDesktopInputService
{
    /// <summary>移动光标到绝对坐标</summary>
    Task<DesktopOperation> MoveToAsync(int x, int y, CancellationToken ct = default);

    /// <summary>执行鼠标动作（单击/双击/右键/中键）</summary>
    Task<DesktopOperation> ClickAsync(int x, int y, MouseAction action, CancellationToken ct = default);

    /// <summary>拖拽：按下→移动到目标→松开，支持中途悬停等待</summary>
    Task<DesktopOperation> DragAsync(int fromX, int fromY, int toX, int toY,
        int? hoverMsAtTarget = null, CancellationToken ct = default);

    /// <summary>按键（单键或组合键）</summary>
    Task<DesktopOperation> KeyPressAsync(int virtualKey, KeyModifier modifiers = KeyModifier.None,
        CancellationToken ct = default);

    /// <summary>输入文本（Unicode，逐字符 SendInput）</summary>
    Task<DesktopOperation> TypeTextAsync(string text, CancellationToken ct = default);
}
```

### 3.2 IWindowManagementService

```csharp
/// <summary>
/// 窗口管理服务 — 枚举/激活/移动/关闭（Win32 EnumWindows 封装）
/// </summary>
public interface IWindowManagementService
{
    /// <summary>枚举所有可见顶层窗口</summary>
    Task<IReadOnlyList<WindowInfo>> EnumerateAsync(CancellationToken ct = default);

    /// <summary>按标题/进程名查找窗口</summary>
    Task<WindowInfo?> FindAsync(string titleOrProcessName, CancellationToken ct = default);

    /// <summary>激活窗口到前台</summary>
    Task<bool> FocusAsync(IntPtr hWnd, CancellationToken ct = default);

    /// <summary>移动/调整窗口大小</summary>
    Task<bool> MoveAsync(IntPtr hWnd, int x, int y, int width, int height, CancellationToken ct = default);

    /// <summary>关闭窗口（发送 WM_CLOSE）</summary>
    Task<bool> CloseAsync(IntPtr hWnd, CancellationToken ct = default);
}
```

### 3.3 IScreenCaptureService（P1 复用，P0 定义）

```csharp
/// <summary>
/// 屏幕截图采集 — GDI BitBlt 封装，返回 PNG base64
/// </summary>
public interface IScreenCaptureService
{
    /// <summary>全屏截图</summary>
    Task<string> CaptureFullScreenAsync(CancellationToken ct = default);

    /// <summary>指定窗口截图</summary>
    Task<string> CaptureWindowAsync(IntPtr hWnd, CancellationToken ct = default);

    /// <summary>指定区域截图</summary>
    Task<string> CaptureRegionAsync(int x, int y, int width, int height, CancellationToken ct = default);
}
```

### 3.4 操作原子单元（为 P4 宏录制/P5 观察学习铺垫）

```csharp
/// <summary>桌面操作原子单元 — 可回放、可审计</summary>
public sealed record DesktopOperation(
    DesktopOperationKind Kind,    // Click/Move/Drag/KeyPress/TypeText/Window
    int X, int Y,
    string? Text,                 // TypeText 内容 / KeyPress 键名
    MouseAction? MouseAction,
    KeyModifier? Modifiers,
    DateTimeOffset Timestamp,
    bool Succeeded,
    string? Error);
```

---

## 4. Win32 P/Invoke 封装

### 4.1 User32NativeMethods.cs（user32.dll）

```csharp
namespace Services.Desktop.Win32;

internal static partial class User32NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}
```

> **注**：用 `static partial class` + `[LibraryImport]`（源码生成器）是 .NET 7+ 的 AOT 推荐写法，可进一步减少封送开销。本期先用 `[DllImport]`（与 `PreventSleepService` 一致），后续可升级 `[LibraryImport]`。

### 4.2 Gdi32NativeMethods.cs（gdi32.dll，截图）

```csharp
internal static class Gdi32NativeMethods
{
    [DllImport("gdi32.dll")]
    internal static extern IntPtr BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, int rasterOp);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
}
```

### 4.3 NativeInputStructs.cs

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint Type;              // INPUT_MOUSE=0, INPUT_KEYBOARD=1
    public InputUnion U;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx, dy;
    public uint mouseData;
    public uint dwFlags;           // MOUSEEVENTF_MOVE/LEFTDOWN/LEFTUP/RIGHTDOWN/RIGHTUP
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;             // 虚拟键码
    public ushort wScan;           // 扫描码
    public uint dwFlags;           // KEYEVENTF_UNICODE/KEYEVENTF_KEYUP
    public uint time;
    public IntPtr dwExtraInfo;
}
```

---

## 5. Core 层实现要点

### 5.1 Win32DesktopInputService

- `ClickAsync`：构造 `MOUSEINPUT`（`MOUSEEVENTF_LEFTDOWN`→`LEFTUP`），`SendInput` 注入
- `DragAsync`：`LEFTDOWN` → `MoveTo` 循环（平滑移动）→ 可选 `Sleep(hoverMs)` → `LEFTUP`
- `KeyPressAsync`：构造 `KEYBDINPUT`（修饰键 down → 主键 down→up → 修饰键 up）
- `TypeTextAsync`：逐字符 `KEYEVENTF_UNICODE` 注入（支持中文等 Unicode）
- 每个方法返回 `DesktopOperation` record，写入 `DesktopOperationLogger`

### 5.2 Win32WindowManagementService

- `EnumerateAsync`：`EnumWindows` 回调 + `IsWindowVisible` 过滤 + `GetWindowText`/`GetWindowRect` 收集
- `FocusAsync`：`SetForegroundWindow`（注意 Windows 前台锁定限制，需 `AllowSetForegroundWindow` 或 Alt 键技巧）
- `CloseAsync`：`PostMessage(hWnd, WM_CLOSE, 0, 0)`（温和关闭，优于 `TerminateProcess`）

### 5.3 GdiScreenCaptureService

- `CaptureFullScreenAsync`：`GetDC(NULL)` → `CreateCompatibleDC` → `BitBlt` → 转 PNG（用现有 ImageSharp）→ base64
- 复用 `ImageResizer.cs:32` 的压缩能力（满足 LLM token 预算）

---

## 6. 工具 Handlers（[McpTool] 暴露）

### 6.1 DesktopInputToolHandlers

```csharp
namespace Tools.Handlers;

[McpToolDispatch(ToolCategory.DesktopControl)]
public class DesktopInputToolHandlers
{
    private readonly IDesktopInputService _input;
    private readonly IScreenCaptureService _capture;

    [McpTool("mouse_click", "在指定坐标执行鼠标点击", "desktop",
        Kind = ToolKindConstants.Normal)]
    public async Task<ToolResult> MouseClickAsync(
        [McpToolParameter("X 坐标", Required = true)] int x,
        [McpToolParameter("Y 坐标", Required = true)] int y,
        [McpToolParameter("点击动作: click/right_click/double_click/middle", Required = false)] string action = "click",
        CancellationToken ct = default)
    {
        var mouseAction = ParseMouseAction(action);
        var op = await _input.ClickAsync(x, y, mouseAction, ct);
        return ToolResultBuilder.Success(op).Build();
    }

    // mouse_move / mouse_drag / key_press / type_text 同理
}
```

### 6.2 WindowManagementToolHandlers

```csharp
[McpToolDispatch(ToolCategory.DesktopControl)]
public class WindowManagementToolHandlers
{
    [McpTool("list_windows", "枚举所有可见顶层窗口", "desktop")]
    public async Task<ToolResult> ListWindowsAsync(CancellationToken ct = default) { ... }

    [McpTool("focus_window", "激活指定窗口到前台", "desktop")]
    public async Task<ToolResult> FocusWindowAsync(
        [McpToolParameter("窗口标题或进程名", Required = true)] string title, CancellationToken ct = default) { ... }

    [McpTool("screenshot", "截取屏幕/窗口/区域", "desktop")]
    public async Task<ToolResult> ScreenshotAsync(
        [McpToolParameter("范围: screen/window/region", Required = false)] string scope = "screen", ...) { ... }

    // move_window / close_window 同理
}
```

> **ToolCategory.DesktopControl** 需新增到 `ToolCategory` 枚举 + `[EnumValue]`，全量重建生成器。

---

## 7. 与现有模块集成

| 现有模块 | 集成方式 |
|---------|---------|
| `Hands/SystemActuator` | **并存**，不修改。SystemActuator 跑命令行，Desktop 服务发 GUI 事件 |
| `PreventSleepService` | **复用 P/Invoke 模式**，Desktop P/Invoke 声明风格与之对齐 |
| `[Register]` DI | 新服务用 `[Register(typeof(IDesktopInputService), ServiceLifetime.Singleton)]` |
| `core/safety/Guard` | **扩展**：`DestructiveCommandDetector` 增加危险坐标/危险窗口检测（U-04 埋点） |
| `ImageResizer.cs:32` | 截图后复用其压缩能力 |
| `ToolCategory` 枚举 | 新增 `DesktopControl` 值 + `[EnumValue]` |
| `DesktopHandoffService` | **保留**，P0 落地后其占位实现可改为委派给真实 Desktop 服务 |

---

## 8. NativeAOT 兼容性策略

| 项 | 策略 | 风险 |
|----|------|------|
| P/Invoke（SendInput/EnumWindows/BitBlt） | ✅ 确定兼容（`PreventSleepService` 已验证路径） | 无 |
| 结构体封送（`[StructLayout]`） | ✅ AOT 兼容，避免 `dynamic` | 无 |
| ImageSharp PNG 编码 | ✅ 已在 `ImageResizer` 使用 | 无 |
| UIAutomation | ⛔ **P0 不用**，留给 P1 卫星项目验证 | 规避 🔴 |
| `[LibraryImport]` 源码生成 | 🟡 后续优化，本期先用 `[DllImport]` | 无 |

---

## 9. 卫星验证项目（P1 前置，可选）

在 `tools/` 下新建 `tools/DesktopAotProbe/`，仅验证 UIAutomation 的 NativeAOT 兼容性：

```
tools/DesktopAotProbe/
├── DesktopAotProbe.csproj   (Exe, PublishAot=true)
└── Program.cs               (尝试引用 UIAutomationClient，枚举窗口)
```

`dotnet publish -c Release` 若成功 → P1 可用 UIAutomation 做确定性 UI 元素检测；若失败 → P1 纯用截图 + 多模态 LLM。**P0 不依赖此项目**。

---

## 10. 安全护栏（P0 埋点）

### 10.1 不可逆操作分类枚举（U-01）

```csharp
public enum UnsafeOperationKind
{
    None,               // 安全
    FileDelete,         // 文件删除
    WindowClose,        // 关闭窗口（可能丢未保存数据）
    ProcessTerminate,   // 结束进程
    DangerousCoordinate // 危险坐标（如"确定删除"按钮）
}
```

### 10.2 点击前危险坐标检查（U-04 钩子）

`ClickAsync` 执行前调用 `IDesktopSafetyChecker.CheckClickAsync(x, y)`：
- P0 先提供 `NoOpDesktopSafetyChecker`（总返回安全）
- P2 实现真实检查器（截图 + LLM 判断按钮语义）

---

## 11. 实现步骤（渐进式，每步编译+提交）

| 步骤 | 内容 | 验证 |
|------|------|------|
| 1 | Abstractions 层接口 + 枚举 + record 定义 | `dotnet build Foundation.slnx -c Debug` |
| 2 | Win32 P/Invoke 声明 + 结构体 | `dotnet build Core.slnx -c Debug` |
| 3 | `Win32DesktopInputService` 实现 + 单元测试（mock SendInput） | 单元测试通过 |
| 4 | `Win32WindowManagementService` 实现 + 单元测试 | 单元测试通过 |
| 5 | `GdiScreenCaptureService` 实现 + 单元测试 | 单元测试通过 |
| 6 | `ToolCategory.DesktopControl` 枚举 + 全量重建生成器 | `--no-incremental` 编译 |
| 7 | `DesktopInputToolHandlers` + `WindowManagementToolHandlers` | 编译 + 工具注册验证 |
| 8 | 安全护栏枚举 + NoOp 检查器 | 编译 |
| 9 | E2E 验收场景 M1（记事本输入 hello world） | 真实 jcc.exe + 记事本断言 |

---

## 12. 验收（对应 PRD §6.3 M1）

- [ ] jcc 接收"在记事本里输入 hello world 并保存到 X.txt" → 文件 X.txt 内容 == "hello world"
- [ ] `mouse_click`/`mouse_move`/`mouse_drag`/`key_press`/`type_text` 工具可用
- [ ] `list_windows`/`focus_window`/`move_window`/`close_window`/`screenshot` 工具可用
- [ ] NativeAOT 编译通过，零警告
- [ ] 每个操作产出 `DesktopOperation` record 并写入日志
- [ ] `UnsafeOperationKind` 枚举就位，`NoOpDesktopSafetyChecker` 钩子就位

---

<!-- 🤖 Auto Decision: 2026-08-25 -->
<!-- 决策: P0 纯用 Win32 P/Invoke（SendInput/EnumWindows/BitBlt），不依赖 UIAutomation -->
<!-- 原因: UIAutomation NativeAOT 兼容性未验证(风险🔴)，而 P/Invoke 路径已被 PreventSleepService 验证；UI 元素语义检测留给 P1(截图+多模态LLM)，P0 只做"盲操作底座" -->
<!-- 替代方案: 直接用 UIAutomation 做元素定位 — 否决，因 AOT 风险且 P0 范围应最小化 -->
<!-- 验证: 架构落点基于对 Abstractions/03-hands + Hands/src/System + ToolHandlers 现有模式的实地考察 ✅ -->
