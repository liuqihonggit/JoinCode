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

## S2 — Avalonia + WebView2 + NativeAOT 卫星宿主 spike

- [ ] S2-1 卫星工程 `non_deliverables_tools/webview_host_spike/`（最大程度复用 JoinCodeGui 的 AOT 配置特征）
- [ ] S2-2 WebView2 包选型（Avalonia.WebView 社区包 / Microsoft.Web.WebView2 + NativeControlHost 两条对比）
- [ ] S2-3 `dotnet publish -c Release -r win-x64` AOT 发布通过，exe 启动加载本地 webui 页面
- [ ] S2-4 JS↔.NET 桥接 ping/pong（双向调用 + 事件回传）
- [ ] S2-5 离线策略验证：WebView2 Runtime 缺失检测/提示；无网环境可运行

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

## 备注

- DSH 暗色激活机制：playwright context colorScheme=dark；webui 侧对应 `prefers-color-scheme`
- 字体：若参考物用非系统字体，差异计入文本掩膜范围（L3 指标只看非文本区）
- 所有截屏/对比脚本归 `non_deliverables_tools/dsh_parity/`（捕获与对比同源）
