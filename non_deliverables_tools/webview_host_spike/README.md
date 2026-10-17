# webview_host_spike — Avalonia + WebView2 + NativeAOT 宿主探针

> 非交付品探针（non_deliverables_tools）。目的：验证 B2 渲染路线（WebView2 容器 + 自研 Web UI）
> 在 **NativeAOT** 下全链路可用：Avalonia 12.1.3 + NativeControlHost 嵌 WebView2 + JS↔.NET 桥接。
>
> 结论（2026-10-17 实测）：✅ 通过。详见 [TASK054](../../modules/apps/docs/task/TASK054-WebView2自研WebUI-spike任务清单.md) spike 数据节。
> ADR: [0138](../../docs/implemented/adr/architecture/0138-gui-webview2-webui-rendering.md)

## 结论摘要

| 路线 | JIT | NativeAOT |
|------|-----|-----------|
| 包默认托管 COM 互操作 | ✅ | ❌ `COM Interop requires ComWrapper instance registered for marshalling` |
| **CsWinRT 投影（采纳）** | ✅ | ✅ ILC 零警告，桥接往返 378ms |

采纳配置（四件套，缺一失败）：
1. TFM `net10.0-windows10.0.26100.0`（TPV 与本机 UAP SDK 对齐）
2. `WebView2EnableCsWinRTProjection=true` + `Microsoft.Windows.CsWinRT` 2.3.1
3. `PlatformTarget=x64`（AnyCPU 会被包 targets 误判 x86 → 拷错架构 → BadImageFormatException）
4. app.manifest：supportedOS + 免注册 WinRT `activatableClass` 条目

## 运行

```powershell
# 编译 + 交互窗口（手动目视）
dotnet build WebViewHostSpike.csproj -c Debug
artifacts\..\WebViewHostSpike.exe --page <绝对路径>\modules\apps\gui\webui\index.html

# 冒烟（真实 WebView2 运行时，自动退出码：0 通过 / 2 失败 / 3 runtime 缺失）
<publish>\WebViewHostSpike.exe --smoke --page <页路径> --result <结果JSON> --timeout 25

# WebView2 Runtime 探测（离线/缺失检测）
<publish>\WebViewHostSpike.exe --runtime-check --result <结果JSON>

# 输入命中实证（真实 OS 级点击 → 页面 mousedown，Debug 与 AOT 均 PASS）
powershell -File click-probe.ps1 -Exe <exe> -Page <页路径> -Result <结果JSON>

# AOT 发布（Release 自动 PublishAot，来自根 Directory.Build.props）
dotnet publish WebViewHostSpike.csproj -c Release
```

结果 JSON 字段：`Ok / Reason / Mode / Page / Runtime / RoundtripMs / EngineMode`（EngineMode = jit|aot 实证）。

## 交互验收结论（2026-10-17）

- 画面渲染正常；"点击无反应"经 `--input-probe` 实证为**静态复刻页无 JS 行为**（composer 是 `<div>`，无 `:hover`/`:active`/`:focus` 规则），非输入链路问题
- 真实 OS 点击（SendInput）→ 页面 mousedown 坐标精确一致（客户区中心 720,450），JIT/AOT 双通过
- webui 侧待补：hover/active/focus 状态（需先 Capture DSH 对应状态）、真实 input 元素（归 P1 场景矩阵）

## 代码地图

| 文件 | 职责 |
|------|------|
| `Program.cs` | 入口：参数解析（smoke/input-probe/runtime-check）/ 冒烟状态机 / 看门狗 / 结果 DTO(JsonContext 源生成) / runtime-check |
| `WebView2Host.cs` | NativeControlHost 宿主：子 HWND → WebView2 controller 挂载 + Bounds 同步 + 初始化失败事件 |
| `click-probe.ps1` | 输入命中实证：Win32 ClientToScreen + SendInput 级真实点击，等待 `.ready` 标记后点击客户区中心 |
| `app.manifest` | supportedOS（NativeControlHost 子窗口必须）+ 免注册 WinRT 激活条目 |
| `WebViewHostSpike.csproj` | AOT 四件套配置（见摘要） |

## 已知噪音

- `Chrome_WidgetWin_0 ... Error = 1412`：WebView2 退出期日志，无影响
- 交互模式下窗口不会自动关闭；冒烟模式由 `--timeout` 看门狗兜底（写入失败结果 + 退出码 2）
