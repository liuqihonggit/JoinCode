# TASK053 — DSH 像素对齐对比校验体系任务清单

> 状态: in_progress
> 日期: 2026-10-17
> 设计: [DSG042](../design/DSG042-DSH像素级对齐对比校验体系.md) · 路线: C（校验先行，Avalonia 复刻 + WebView2 spike 并行评估）
> 工作流: 每任务 红测试 → 实现 → 编译 → 绿测试 → 文档 → git 提交 → 差评大师 → 修复
> 脚本类任务豁免单元测试（非交付品工具），验收方式 = 真实执行产出 golden 产物（对齐 ADR 0080 精神）

---

## P0 — 固化 capture 原型为正式卫星工具 ✅

- [x] P0-1 创建 `non_deliverables_tools/dsh_parity/` 卫星目录 + package.json（锁 `@deepseek-ai/dsh@0.2.0-rc.2` + playwright-core）
- [x] P0-2 `capture/capture-dsh.mjs`：CDP 截图 + DOM 盒树 + 双主题 token 提取（Edge 路径自动探测，参数化视口）
- [x] P0-3 `capture/capture-all.ps1`：启动 dsh(--no-open, DSH_HOME 隔离) → 捕获 → 按 PID 击杀（防残留）
- [x] P0-4 `.gitignore`（node_modules/.dsh-home/.tmp）+ README（运行方式）
- [x] P0-5 真实执行验证：新位置跑通全流程，golden 入库 `reference/`
- [x] P0-6 提交

## P1 — 场景矩阵扩展

- [ ] P1-1 对话框场景（permission/question）捕获脚本
- [ ] P1-2 设置面板场景
- [ ] P1-3 对话中场景（注入样例数据）
- [ ] P1-4 斜杠面板场景
- [ ] P1-5 场景定义文件 `scenarios.json` 驱动

## P2 — L1 token 对等（CI 快）

- [ ] P2-1 红测试：token 对等测试骨架（缺映射即红）
- [ ] P2-2 `ui-parity-map.json` 初始化（dsw token → 本项目资源键）
- [ ] P2-3 token 生成器（dsh-tokens.json → C# 资源数据，禁手抄）
- [ ] P2-4 GuiPalette 切换到生成数据（Avalonia 路线首个落地）
- [ ] P2-5 绿测试 + 提交

## P3 — L2 几何对等

- [ ] P3-1 VisualTree 盒树 dump（与 dsh-dom-*.json 同构）
- [ ] P3-2 区域映射表（DSH 选择器 ↔ Avalonia 控件路径）
- [ ] P3-3 几何断言（±1px / 字号 / 颜色）+ diff-report.json

## P4 — L3 像素对等

- [ ] P4-1 帧 diff 引擎（文本掩膜 + 非文本区比例 + SSIM）
- [ ] P4-2 热力图输出 + 差异白名单
- [ ] P4-3 双主题 × 场景矩阵报告

## P5 — 双路线评估（已决策：B2 主线）

- [x] P5-1 WebView2 + NativeAOT 卫星 spike → 移至 [TASK054](TASK054-WebView2自研WebUI-spike任务清单.md)（用户选定 B2 后成为关键路径）
- [x] P5-2 路线决策（用户确认）：**B2 WebView2+自研 Web UI**（[ADR 0138](../../../../docs/proposed/adr/architecture/0138-gui-webview2-webui-rendering.md) proposed）；A 降为回退

## P6 — 按区域对齐重构（依 P5 决策：WebUI 区域，S2/S3 通过后启动）

- [ ] P6-1 侧栏（工作区/会话列表）
- [ ] P6-2 composer（输入区）
- [ ] P6-3 对话消息区
- [ ] P6-4 设置面板
- [ ] P6-5 对话框（权限/询问/确认）
- [ ] P6-6 CI 集成（L1/L2 入 CI）+ 验收记录

---

## 备注

- 参考物版本锚定 `0.2.0-rc.2`；升级需显式重捕获 + diff 审计（防参考物漂移）
- golden 产物入库 `reference/`（PNG + JSON，KB 级）；瞬时产物写 `dumps/`（gitignore）
- 卡点教训：`msedge --headless --screenshot --virtual-time-budget` 对 WebSocket 长连接页面永久挂起，必须用 CDP 主动截屏
