# ADR 0138: GUI 渲染路线采用 WebView2 + 自研 Web UI（B2）

> 📍 **导航**: [docs/](../../../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

> 状态: proposed（待 spike 验证 Avalonia+WebView2+NativeAOT 后转 accepted）
> 创建: 2026-10-17
> 决策者: 用户主导
> 影响: modules/apps/gui 渲染层重写为 Web 前端；引入 HTML/CSS/JS 构建链；.NET↔JS 桥接
> 关联: [DSG041](../../../../modules/apps/docs/design/DSG041-UI全面对齐CLI与精美界面规划.md) · [DSG042](../../../../modules/apps/docs/design/DSG042-DSH像素级对齐对比校验体系.md) · [TASK054](../../../../modules/apps/docs/task/TASK054-WebView2自研WebUI-spike任务清单.md)

## 背景

GUI 需像素级对齐 DSH（DeepSeek Harness）web UI。三条候选路线：

| 路线 | 机制 | 核心问题 |
|------|------|---------|
| A. Avalonia 复刻 | XAML 翻译 DSH 布局 | 跨引擎差异（Skia vs Chromium）：文本光栅化、CSS 效果天生微差；XAML 表达 flex 布局成本高 |
| B1. 直接嵌 DSH 原版界面 | WebView2 加载 DSH web app | 界面显示的是 DSH 的功能（其 Node 后端驱动）；反向工程未公开通信协议更贵更脆；产品目标（GUI 暴露本项目 CLI 能力）落空 |
| B2. WebView2 + 自研 Web UI | 自研 HTML/CSS/JS 前端，样式消费捕获的 DSH token golden | 需验证 Avalonia+WebView2+NativeAOT+离线打包；引入前端工程 |

关键事实：DSH 参考物与本方案成品同为 Chromium 渲染引擎 → 文本光栅化/布局算法完全一致，像素对齐从"跨引擎天生微差"变为"同引擎近似 bit-exact"。

## 决策

GUI 内容区采用 **WebView2 容器渲染自研 Web UI（HTML/CSS/JS）**：

1. **样式唯一数据源 = DSH token golden**（`dsh_parity/reference/dsh-tokens.json`，421 变量双主题）→ 生成 CSS 变量文件，禁止手抄（对齐既有"枚举唯一数据源"原则）
2. **Avalonia 仅保留窗口壳与原生集成**（托盘、原生对话框按需），内容区全部由 WebView2 承载
3. **.NET 后端经桥接暴露 CLI 能力**（斜杠命令、会话流、守卫确认）——桥接协议 spike 后细化
4. **校验体系复用**（L1 token → CSS 变量；L2 几何用 DOM 盒树；L3 像素同引擎更严格）
5. 决策前置条件：spike 验证 Avalonia+WebView2 集成、NativeAOT 发布、离线运行 三项均通过；任一失败则回退 A 路线（校验体系与 token 数据源渲染器无关，切换成本可控）

## 替代方案

1. **A. Avalonia 原位复刻（降为回退方案）** — 单栈、无 WebView 依赖，但跨引擎差异使像素对齐目标成本高企；保留为 spike 失败时的回退
2. **B1. 直接嵌 DSH 原版界面（放弃）** — 功能归属错误（展示 DSH 功能而非本项目 CLI 能力）+ 协议反向工程不可持续
3. **混合：Avalonia 壳 + 部分区域 WebView（放弃）** — 两套渲染栈拼贴，主题/字体/交互一致性碎裂，维护成本最高

## 后果

### 正面
- 像素对齐难度数量级下降：同引擎渲染 + CSS 是 DSH 布局的母语
- token golden 直接变 CSS 变量，L1 校验闭环更简单严格
- Web 前端生态成熟（热重载、DevTools 调试）
- 静态前端零运行时依赖（对比 B1 需打包 Node）

### 负面
- 引入前端工程与构建链（本项目为 .NET 栈，新增第二语言维护面）
- WebView2 Runtime 依赖（Win10/11 Evergreen 自带；需确认离线分发策略）
- 启动顺序变化：原窗即 WebView 宿主
- NativeAOT 与 WebView2 COM 集成需实测（spike 范围）

### 缓解
- 渐进式：spike 先行拿硬数据 → ADR 转 accepted 才全面铺开
- 前端保持薄层（纯静态 + 原生桥），不过度工程化
- 双引擎共存期：Avalonia 按钮/托盘等原生件保留，逐步收敛

## 验证清单

- [ ] S1: 自研 WebUI 首页骨架 + token→CSS 生成器 + 截图/diff 管道跑通，产出对齐数据
- [ ] S2: Avalonia+WebView2 卫星宿主 spike：AOT 发布 + 加载本地页面 + 桥接 ping/pong
- [ ] S3: spike 报告 + 本 ADR 状态更新（accepted / 回退 A）
- [ ] 离线打包策略确认（WebView2 Runtime 检测与分发）
