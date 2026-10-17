# TASK054 — WebView2 + 自研 WebUI spike 任务清单

> 状态: in_progress
> 日期: 2026-10-17
> ADR: [0138](../../../../docs/proposed/adr/architecture/0138-gui-webview2-webui-rendering.md)（proposed，spike 通过后转 accepted）
> 设计: [DSG042](../design/DSG042-DSH像素级对齐对比校验体系.md) · 参考物: `non_deliverables_tools/dsh_parity/reference/`
> 工作流: 每任务 红测试 → 实现 → 编译 → 绿测试 → 文档 → git 提交（脚本类非交付品豁免单测，验收 = 真实执行产出）

---

## S1 — WebUI 骨架 + token→CSS 生成 + 截图/diff 管道（同引擎对齐数据）✅

目标：证明"自研 WebUI 消费 DSH token 后，与 DSH 参考帧的像素差异可达近 bit-exact"。

- [x] S1-1 `generate/tokens2css.mjs`：dsh-tokens.json → tokens.css（:root 亮色 / prefers-color-scheme: dark 暗色），禁手抄
- [x] S1-2 `modules/apps/gui/webui/` 首页骨架（侧栏 + 主区 + composer 输入区），布局几何取自 dsh-dom-1440x900.json
- [x] S1-3 `compare/screenshot-web.mjs`：CDP 截图本地 webui 页（1440x900, dark, DPR=1）
- [x] S1-4 `compare/diff-frames.mjs`：Chromium canvas 帧 diff（差异像素比例 + 平均 ΔE + 热力图 PNG + 12x10 网格归因）
- [x] S1-5 产出对齐报告（S1 数据），记录到本文件"spike 数据"节

## S2 — Avalonia + WebView2 + NativeAOT 卫星宿主 spike ✅

- [x] S2-1 卫星工程 `non_deliverables_tools/webview_host_spike/`（net10.0-windows10.0.26100.0 + Avalonia 12.1.3 + Microsoft.Web.WebView2 1.0.4258.31）
- [x] S2-2 包选型：**否决**社区 Avalonia.WebView/托管 COM 互操作路线（实测 AOT 运行时不可用）；采用 **Microsoft.Web.WebView2 + NativeControlHost + CsWinRT 投影**（官方 AOT 路线，ILC 零警告）
- [x] S2-3 `dotnet publish -c Release` AOT 发布通过（exe 11.7MB），启动加载 `modules/apps/gui/webui/index.html` ✓
- [x] S2-4 JS↔.NET 桥接 ping/pong：`chrome.webview.postMessage` → C# `WebMessageReceived` → `PostWebMessageAsJson` → JS 确认，AOT 版往返 378ms
- [x] S2-5 离线策略：`--runtime-check` 模式探测 `GetAvailableBrowserVersionString`（缺失返回退出码 3 + 结果 JSON）；file:// 本地页无网运行验证 ✓

## S3 — 收尾

- [ ] S3-1 spike 报告：S1 像素数据 + S2 AOT/桥接结论 → 路线终局建议
- [ ] S3-2 ADR 0138 状态更新（accepted / 回退 A 并记录原因）
- [ ] S3-3 TASK053 P5/P6 依决策重排（P5-1 移至本任务，P6 改 WebUI 区域重构）

---

## spike 数据（实测记录）

### S1 — 首页帧对齐（1440x900 · 深色 · DPR1）

复刻范围：DSH 首页整帧（侧栏 logo/新会话/插件/工作区列表/设置 + 主区 headline/工作区行/composer 卡片）。

| 迭代 | 改动 | meanDelta | 超阈像素% (Δ>4/Δ>12/Δ>24) |
|------|------|-----------|---------------------------|
| v1 | 骨架 + token 直连（几何取自 DOM golden） | 2.246 | 2.31 / 2.13 / 1.98 |
| v2 | 修类名冲突/侧栏分隔线(border-l3)/行宽/徽章/鲸鱼 logo | 1.809 | 1.56 / 1.40 / 1.26 |
| v3 | 新会话边框(border-l3)/⊕图标/composer 行内距 | **1.793** | **1.53 / 1.36 / 1.22** |

- 网格归因（残差 Top10 区域）：全部集中在 **文本字形 AA**（标题/文案/品牌字标 SVG）、**图标近似路径**（搜放大镜/排序/插件/齿轮等）→ 正是 L3 计划的"文本掩膜 + 图标白名单"类别
- 结构/布局/颜色/背景：**达 near-exact**（98.6% 像素 Δ≤11；卡片/按钮/列表盒位与 golden 盒模型一致，含亚像素 AA 行为）
- 意外发现：DOM 存在 ≠ 渲染可见（新会话快捷键 kbd 提示 DOM 有、帧上无，仅悬停显示）→ 复刻以 golden 帧为准，DOM 只作布局规格

**S1 结论**：同引擎（Chromium）下，Web UI 路线"抄 token + 按盒模型落布局"即可在第一版达到 98.6% 像素一致，剩余全部是字形/图形资产类差异（有既定掩膜/白名单机制兜底）。**B2 路线像素对齐可行性成立。**

### S2 — WebView2 宿主 AOT 实测矩阵（1440x900 窗口 · 本地 webui 页 · 真实 Edge WebView2 运行时 155.0.4283.60）

| 路线 | JIT (Debug) | NativeAOT (Release publish) |
|------|-------------|------------------------------|
| 托管 COM 互操作（包默认 `lib_manual/netcoreapp3.0`） | ✅ 993ms | ❌ 运行期 `NotSupportedException: COM Interop requires ComWrapper instance registered for marshalling`（`ComWrappers.ComInterfaceForObject`，AOT 无内置 COM marshalling） |
| **CsWinRT 投影**（`WebView2EnableCsWinRTProjection=true`） | ✅ 495ms | ✅ **378ms，ILC 零警告**，runtime-check + 导航 + 桥接全通 |

上游情报佐证（GitHub WebView2Feedback #4783/#4800/#4962）：托管 COM 路径不支持 AOT；投影 + AOT 自 WebView2 1.0.2817/2849 + CsWinRT 2.1.1 起官方修复，本包 1.0.4258.31 实测确认可用。

**AOT 采纳配置四件套**（缺一即失败）：

| # | 配置 | 缺省后果 |
|---|------|----------|
| 1 | TFM `net10.0-windows10.0.26100.0`（TPV 与本机 UAP SDK 对齐） | cswinrt.exe 报 `Could not read Platform.xml 10.0.17763.0` |
| 2 | `WebView2EnableCsWinRTProjection=true` + `Microsoft.Windows.CsWinRT 2.3.1` | 走托管 COM 路径 → AOT 运行时炸 |
| 3 | `PlatformTarget=x64`（包 targets 对 AnyCPU 判定 EffectivePlatform=x86） | 拷入 win-x86 原生组件 → `BadImageFormatException 0x800700C1`（窗口弹出但永不渲染） |
| 4 | app.manifest：supportedOS + 免注册 WinRT `activatableClass` 条目（Environment/EnvironmentOptions/Controller） | NativeControlHost 子窗口创建失败 / 激活 `ManifestFreeGet` 失败 |

**WinRT 投影 API 形状差异**（与托管 SDK 不同，迁移时注意）：
- `CoreWebView2Environment.CreateAsync()` 仅 0 参；带选项用 `CreateWithOptionsAsync(browserExecutableFolder, userDataFolder, options)`
- 控制器创建：`CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)hwnd)` → `CreateCoreWebView2ControllerAsync(windowRef)`
- `Bounds` 为 `Windows.Foundation.Rect`（double）；`ExecuteScriptAsync` 返回 `IAsyncOperation<string>`（可 await，无 ConfigureAwait）
- 事件仍为 C# 事件（`WebMessageReceived` / `NavigationCompleted`）

**S2 结论**：Avalonia 12.1.3 + NativeControlHost + WebView2 投影 + NativeAOT 全链路可用，热启动桥接往返 < 400ms。**B2 路线技术可行，spike 通过。**

## 备注

- DSH 暗色激活机制：playwright context colorScheme=dark；webui 侧对应 `prefers-color-scheme`
- 字体：若参考物用非系统字体，差异计入文本掩膜范围（L3 指标只看非文本区）
- 所有截屏/对比脚本归 `non_deliverables_tools/dsh_parity/`（捕获与对比同源）
- WebView2 user data folder：spike 用 `%TEMP%\jcc-webview2-spike`；产品接入时改为用户级目录（如 `%LOCALAPPDATA%\JoinCode\webview2`）
- `Chrome_WidgetWin_0` unregister 错误 1412 = WebView2 退出期已知噪音日志，无影响
- 产品 GUI 接入时同样需要四件套（TFM 平台化/投影开关/x64/清单条目），届时由 ADR 0138 落地章节承接
