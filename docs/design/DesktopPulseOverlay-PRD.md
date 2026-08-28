# 桌面脉冲高亮覆盖层 PRD

> **版本**：v0.1
> **日期**：2026-08-29
> **状态**：草案 / 待评审
> **关联**：Vision 工具接入主工程（commit 13d35fa52）、show_desktop_overlay 桌面画框（commit 4ca872aef）

---

## 0. 背景与动机

Vision 工具已接入主工程链路，`screen_indicate` 能在**图片上**标注格子返回 base64，`show_desktop_overlay` 能在**桌面 DC 上**画静态矩形框。但用户实际场景需要**动画引导效果**：

> 用户："我找不到桌面的 xx 了，你帮我高亮找出来"

当前能力缺口：
- `show_desktop_overlay` 只画静态框，无动画引导视线
- 无半透明效果（GDI 实线框遮挡桌面内容）
- 无脉冲圆动画（瞄准镜式从大到小循环，引导用户聚焦目标）

---

## 1. 用户场景

### 1.1 主场景：找不到桌面图标

```
用户："我找不到桌面的回收站了，你帮我高亮找出来"
  ↓
[1] key_press("win+d")                    → 显示桌面（最小化全部窗口）
[2] screenshot(scope="screen")            → 桌面截图 base64
[3] quadtree_build(imageBase64, depth=3)  → 64 格网格编码
[4] LLM 推理：扫描格子描述，找到"回收站"所在格子 cellCode
[5] quadtree_zoom(cellCode)               → 聚焦子图，精确定位坐标
[6] LLM 推理：计算回收站中心坐标 (cx, cy)
[7] show_desktop_pulse(cx, cy, maxRadius=120, minRadius=30, durationMs=5000)
    → 桌面上显示半透明脉冲圆动画，从大到小循环收缩，5秒后自动消失
  ↓
用户在桌面上看到脉冲圆动画，视线被引导到回收站位置 ✅
```

### 1.2 辅助场景：标注 UI 元素

```
用户："帮我标出这个按钮在哪里"
  ↓
[1] screenshot → detect_ui_elements       → 找到按钮坐标
[2] show_desktop_pulse(buttonX, buttonY)  → 脉冲圆标注按钮位置
```

---

## 2. 目标与非目标

### 2.1 目标

- 新增 `show_desktop_pulse` MCP 工具，在桌面上显示**半透明脉冲圆动画**
- 用 **Win32 透明无边框顶层窗口**承载绘制，不侵入桌面 DC（避免残留）
- 圆从大到小**循环收缩**（瞄准镜效果），引导用户视线聚焦目标
- 支持**超时自动关闭**和**手动取消**
- 保持 **NativeAOT 兼容**：纯 P/Invoke + GDI，无 System.Drawing.Common

### 2.2 非目标

- **不**实现点击/交互（窗口 WS_EX_TRANSPARENT，鼠标穿透）
- **不**实现多目标同时标注（单窗口单圆）
- **不**替换 `show_desktop_overlay`（静态框仍有用，脉冲圆是动画增强）
- **不**做跨平台（仅 Windows Win32）

---

## 3. 功能需求

### 3.1 工具签名

```
show_desktop_pulse
  参数:
    centerX     (int, required)  — 目标中心 X（屏幕坐标）
    centerY     (int, required)  — 目标中心 Y（屏幕坐标）
    maxRadius   (int, default=120) — 最大半径（像素）
    minRadius   (int, default=30)  — 最小半径（像素）
    durationMs  (int, default=5000) — 动画总时长（毫秒），超时自动关闭
    frameMs     (int, default=33)  — 帧间隔（毫秒），默认约 30fps
    color       (string, default="yellow") — 圆颜色: red/green/blue/yellow/cyan/magenta
  返回:
    Success: "桌面脉冲圆动画已显示 {durationMs}ms: 中心({cx},{cy}) 半径{minR}-{maxR}"
    Error [OVL200]: 参数校验失败
    Error [OVL201]: 窗口创建失败
```

### 3.2 动画规格

| 属性 | 值 |
|------|-----|
| 效果 | 半透明圆环，从 maxRadius 收缩到 minRadius，循环 |
| 帧率 | 1000/frameMs fps（默认 ~30fps） |
| 收缩步数 | 10 帧（从 maxRadius 线性收缩到 minRadius） |
| 循环 | 到达 minRadius 后跳回 maxRadius，无缝循环 |
| 透明度 | 圆环 alpha=180（半透明，不遮挡桌面内容） |
| 线宽 | 4px |
| 背景 | 窗口其余区域完全透明（颜色键透明） |

### 3.3 窗口规格

| 属性 | 值 |
|------|-----|
| 样式 | WS_POPUP（无边框无标题栏） |
| 扩展样式 | WS_EX_LAYERED \| WS_EX_TOPMOST \| WS_EX_TRANSPARENT \| WS_EX_NOACTIVATE |
| 位置 | (centerX - maxRadius, centerY - maxRadius) |
| 尺寸 | (maxRadius * 2, maxRadius * 2) |
| 透明方式 | SetLayeredWindowAttributes + LWA_COLORKEY（黑色=透明） |
| 层级 | HWND_TOPMOST（始终在最前） |
| 鼠标穿透 | WS_EX_TRANSPARENT（不拦截鼠标事件） |

---

## 4. 技术方案

### 4.1 透明窗口 + GDI 绘制

```
┌─────────────────────────────────┐
│  Win32 透明窗口（WS_EX_LAYERED） │
│  ┌───────────────────────────┐  │
│  │   黑色背景（透明）         │  │
│  │      ┌─────────┐          │  │
│  │      │ 半透明圆 │ ← GDI绘制 │  │
│  │      └─────────┘          │  │
│  │   (颜色键=黑色→透明)       │  │
│  └───────────────────────────┘  │
└─────────────────────────────────┘
```

**为什么用颜色键透明而非 alpha 透明？**
- `LWA_COLORKEY`：指定颜色（黑色）完全透明，其余不透明 — GDI 原生支持，简单可靠
- `LWA_ALPHA`：整个窗口统一透明度 — 无法实现"背景透明 + 圆半透明"
- `UpdateLayeredWindow` + 32位 ARGB：最灵活但复杂度高，需逐像素设置 alpha

选择 `LWA_COLORKEY`：黑色填充背景（透明），彩色画圆（不透明），简单且满足需求。

### 4.2 消息循环架构

```
ShowDesktopPulseAsync (async 工具方法)
  ↓
Task.Run(后台线程)
  ├── RegisterClassEx        — 注册窗口类
  ├── CreateWindowEx         — 创建透明窗口
  ├── SetLayeredWindowAttributes — 设置颜色键透明
  ├── ShowWindow             — 显示窗口
  ├── SetTimer               — 设置动画定时器
  ├── 消息循环               — GetMessage/TranslateMessage/DispatchMessage
  │     ├── WM_TIMER → 更新半径 + InvalidateRect
  │     ├── WM_PAINT → GDI 画当前帧圆
  │     └── WM_DESTROY → PostQuitMessage
  └── 超时/取消 → PostMessage(WM_CLOSE) → 窗口销毁 → 消息循环退出
```

### 4.3 动画状态管理

```csharp
class PulseState
{
    int CurrentRadius;   // 当前半径
    int FrameIndex;      // 当前帧索引 (0..9)
    int MaxRadius;
    int MinRadius;
    uint ColorRef;       // COLORREF
    long StartTicks;     // 动画开始时间
    int DurationMs;      // 总时长
}
```

- WM_TIMER：检查超时 → 更新 FrameIndex → 计算 CurrentRadius → InvalidateRect
- WM_PAINT：黑色填充背景 → 画 CurrentRadius 半径的圆环

### 4.4 P/Invoke 清单

| API | 来源 | 用途 |
|-----|------|------|
| RegisterClassEx | user32 | 注册窗口类 |
| CreateWindowEx | user32 | 创建透明窗口 |
| SetLayeredWindowAttributes | user32 | 设置颜色键透明 |
| ShowWindow | user32 (已有) | 显示窗口 |
| SetTimer / KillTimer | user32 | 动画定时器 |
| GetMessage / TranslateMessage / DispatchMessage | user32 | 消息循环 |
| DefWindowProc | user32 | 默认窗口过程 |
| BeginPaint / EndPaint | user32 | 绘制 |
| DestroyWindow | user32 | 销毁窗口 |
| PostQuitMessage | user32 | 退出消息循环 |
| InvalidateRect | user32 (已加) | 触发重绘 |
| CreatePen / SelectObject / DeleteObject | gdi32 (已有/加) | 画笔 |
| Ellipse | gdi32 (需加) | 画圆 |
| FillRect | user32 (需加) | 填充背景 |
| GetStockObject | gdi32 (已加) | 获取系统画刷 |

### 4.5 线程模型

```
工具调用线程 (async)                后台线程 (消息循环)
    │                                    │
    ├── Task.Run ──────────────────→ 启动窗口 + 消息循环
    │                                    │
    ├── await Task.Delay(durationMs)     ├── WM_TIMER → 重绘
    │                                    │
    ├── 超时/取消                        │
    │   └── PostMessage(WM_CLOSE) ──→ WM_CLOSE → DestroyWindow
    │                                    │
    └── 返回 Success                     └── 消息循环退出 → 线程结束
```

- 工具方法在调用线程 `await Task.Delay(durationMs)`，超时后 `PostMessage(WM_CLOSE)` 通知后台线程关闭窗口
- 后台线程独占消息循环，避免阻塞工具调用线程
- `CancellationToken` 取消时同样 `PostMessage(WM_CLOSE)`

---

## 5. 交互流程（完整调用链）

### 5.1 LLM 调用链

```
用户："我找不到桌面的xx了，你帮我高亮找出来"
  ↓
[1] key_press("win+d")
    → 显示桌面
[2] screenshot(scope="screen")
    → imageBase64, (W, H)
[3] quadtree_build(imageBase64, depth=3)
    → 64 格编码列表
[4] LLM 扫描格子，找到目标格子 cellCode
[5] quadtree_zoom(imageBase64, cellCode, sourceDepth=3, targetDepth=2)
    → 子图 base64 + 精细网格
[6] LLM 在子图中定位目标中心 (cx, cy)
[7] show_desktop_pulse(centerX=cx, centerY=cy, maxRadius=120, minRadius=30, durationMs=5000)
    → 桌面脉冲圆动画 5 秒
  ↓
用户看到脉冲圆引导视线到目标位置 ✅
```

### 5.2 工具描述（供 LLM 理解）

```
show_desktop_pulse:
  "在桌面上显示半透明脉冲圆动画标注目标位置。圆从大到小循环收缩(瞄准镜效果),
   引导用户视线聚焦。用透明窗口绘制不侵入桌面,超时自动关闭。
   前置:需先screenshot+detect_ui_elements或quadtree_build获取目标坐标"
```

---

## 6. 非功能需求

| 维度 | 要求 |
|------|------|
| NativeAOT | 纯 P/Invoke + GDI，无 System.Drawing.Common，IsAotCompatible |
| 性能 | 动画帧率 ~30fps，CPU 占用 < 1%（单窗口单定时器） |
| 内存 | 窗口 + GDI 对象 < 1MB，关闭后全部释放 |
| 线程安全 | 后台线程独占消息循环，工具线程仅 PostMessage 通信 |
| 健壮性 | 窗口创建失败返回 [OVL201]，参数校验返回 [OVL200] |
| 取消支持 | CancellationToken 触发 PostMessage(WM_CLOSE) 立即关闭 |
| 无残留 | 窗口销毁后桌面无任何残留（不画在桌面 DC 上） |

---

## 7. 验收标准

### 7.1 功能验收

- [ ] `show_desktop_pulse(500, 500)` 在桌面 (500,500) 显示黄色脉冲圆动画
- [ ] 圆从 maxRadius=120 收缩到 minRadius=30，循环
- [ ] 5 秒后动画自动消失，桌面无残留
- [ ] 窗口透明（只有圆可见，背景不遮挡桌面）
- [ ] 窗口无边框无标题栏
- [ ] 鼠标穿透（窗口不拦截鼠标点击）
- [ ] 窗口始终在最前（不被其他窗口遮挡）

### 7.2 参数验收

- [ ] `color="red"` 显示红色圆
- [ ] `maxRadius=200, minRadius=50` 显示大范围脉冲
- [ ] `durationMs=1000` 1 秒后消失
- [ ] `frameMs=16` ~60fps 流畅动画
- [ ] 无效参数返回 [OVL200] 错误

### 7.3 链路验收

- [ ] 工具注册到 `GeneratedToolHandlerRegistration_JoinCode_Composition`
- [ ] LLM 通过 `ToolSearch map[desktop]` 能发现 `show_desktop_pulse`
- [ ] 完整调用链 `screenshot → quadtree_build → show_desktop_pulse` 可执行

### 7.4 编译验收

- [ ] Hands.csproj Debug 编译 0 警告 0 错误
- [ ] Composition.csproj Debug 编译 0 警告 0 错误
- [ ] JoinCode.csproj Debug 编译 0 警告 0 错误

---

## 8. 实现计划

| 步骤 | 内容 | 产物 |
|------|------|------|
| 1 | 添加 P/Invoke 声明（RegisterClassEx/CreateWindowEx/SetLayeredWindowAttributes 等） | User32NativeMethods.cs 扩展 |
| 2 | 添加 GDI 声明（Ellipse/FillRect） | Gdi32NativeMethods.cs 扩展 |
| 3 | 创建 DesktopPulseOverlay 封装类（窗口+消息循环+动画） | Desktop/PulseOverlay/DesktopPulseOverlay.cs |
| 4 | 在 DesktopOverlayToolHandlers 添加 ShowDesktopPulseAsync 方法 | DesktopOverlayToolHandlers.cs 扩展 |
| 5 | 编译验证 + 工具注册确认 | 生成代码含 show_desktop_pulse |
| 6 | 手动测试（桌面实际效果） | 截图/录屏验证 |

---

## 9. 风险与对策

| 风险 | 对策 |
|------|------|
| NativeAOT 下 P/Invoke 委托（WndProc）被 trim | 用 `[UnmanagedCallersOnly]` 或 GCHandle.Alloc 钉住委托 |
| 消息循环阻塞工具线程 | 后台线程 Task.Run，工具线程仅 PostMessage |
| 窗口类名冲突（多次调用） | 用 Guid 后缀唯一化类名，用完 UnregisterClass |
| 多显示器坐标偏移 | 用屏幕坐标（虚拟桌面坐标），窗口位置直接传入 |
| 颜色键透明与桌面黑色内容冲突 | 窗口背景用极少用的颜色（如 RGB(1,0,1)）而非纯黑 |

---

<!-- 🤖 Auto Decision: 2026-08-29 -->
<!-- 决策: 用 LWA_COLORKEY 颜色键透明而非 LWA_ALPHA 或 UpdateLayeredWindow -->
<!-- 原因: 颜色键透明 GDI 原生支持，简单可靠，无需逐像素 alpha 混合 -->
<!-- 替代方案: UpdateLayeredWindow + 32位 ARGB 位图（最灵活但复杂度高，暂不采用）-->
<!-- 验证: PRD 待实现验证 -->
