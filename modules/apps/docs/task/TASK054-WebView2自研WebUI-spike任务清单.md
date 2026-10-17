# TASK054 — WebView2 + 自研 WebUI spike 任务清单

> 状态: in_progress
> 日期: 2026-10-17
> ADR: [0138](../../../../docs/proposed/adr/architecture/0138-gui-webview2-webui-rendering.md)（proposed，spike 通过后转 accepted）
> 设计: [DSG042](../design/DSG042-DSH像素级对齐对比校验体系.md) · 参考物: `non_deliverables_tools/dsh_parity/reference/`
> 工作流: 每任务 红测试 → 实现 → 编译 → 绿测试 → 文档 → git 提交（脚本类非交付品豁免单测，验收 = 真实执行产出）

---

## S1 — WebUI 骨架 + token→CSS 生成 + 截图/diff 管道（同引擎对齐数据）

目标：证明"自研 WebUI 消费 DSH token 后，与 DSH 参考帧的像素差异可达近 bit-exact"。

- [ ] S1-1 `generate/tokens2css.mjs`：dsh-tokens.json → tokens.css（:root 亮色 / prefers-color-scheme: dark 暗色），禁手抄
- [ ] S1-2 `modules/apps/gui/webui/` 首页骨架（侧栏 + 主区 + composer 输入区），布局几何取自 dsh-dom-1440x900.json
- [ ] S1-3 `compare/screenshot-web.mjs`：CDP 截图本地 webui 页（1440x900, dark, DPR=1）
- [ ] S1-4 `compare/diff-frames.mjs`：Chromium canvas 帧 diff（差异像素比例 + 平均 ΔE + 热力图 PNG）
- [ ] S1-5 产出对齐报告（S1 数据），记录到本文件"spike 数据"节

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

（待 S1 执行后填写）

## 备注

- DSH 暗色激活机制：playwright context colorScheme=dark；webui 侧对应 `prefers-color-scheme`
- 字体：若参考物用非系统字体，差异计入文本掩膜范围（L3 指标只看非文本区）
- 所有截屏/对比脚本归 `non_deliverables_tools/dsh_parity/`（捕获与对比同源）
