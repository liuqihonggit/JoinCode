# DSG042 — DSH 像素级对齐：对比与校验体系

> 状态: proposed
> 日期: 2026-10-17
> 关联: [DSG041](DSG041-UI全面对齐CLI与精美界面规划.md)(功能对齐)、ADR [0080](../../../../docs/implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md)(手动 exe 验收)、ADR [0005](../../../../docs/implemented/adr/architecture/0005-file-driven-ui.md)(文件驱动界面)
> 原型验证: ✅ 已端到端跑通（npm `@deepseek-ai/dsh@0.2.0-rc.2` + playwright-core + Edge CDP）

---

## 一、"像素级对齐"的可度量定义

"像素级"不能是感觉，必须每个维度都有指标和阈值，否则无法验收：

| 维度 | 指标 | 阈值 | 校验层 |
|------|------|------|--------|
| 颜色 token | 颜色值精确相等（含 alpha） | ΔE = 0 | L1 |
| 字体 | font-family 栈 / size / weight / line-height | 精确相等 | L1+L2 |
| 圆角 / 间距 | border-radius / padding / margin / gap | 精确相等 | L1+L2 |
| 几何 | 映射元素的 box（x/y/w/h）@DPR=1 | ±1px | L2 |
| 结构 | 元素树层级 / 顺序 / 可见性 | 完全一致 | L2 |
| 像素 | 非文本区域逐像素 diff 比例 | ≤ 0.5% | L3 |
| 文本渲染 | **不比对**（字形光栅化天生不同，掩膜处理） | — | L3 |
| 状态 | 固定场景枚举（空会话/对话中/设置/弹窗…） | 全覆盖 | 场景矩阵 |

**诚实边界**（写死在验收标准里，防止过度承诺）：
- Chromium 与 Skia 字形光栅化永远存在微差 → 文本区掩膜后比对背景/结构/几何；
- CSS 独有效果（backdrop-filter 模糊、多层 box-shadow）Avalonia 无 1:1 等价物 → 用最接近的近似并登记到差异白名单；
- 滚动条、选区等平台组件视觉差异 → 白名单。

---

## 二、架构总览（三管线 + 三层校验）

```
┌── 参考物管线（DSH 侧, 固定版本 0.2.0-rc.2）──────────────────┐
│ dsh web (DSH_HOME 隔离, --no-open)                           │
│   → playwright-core + Edge CDP（固定视口 1440x900 @DPR=1）    │
│   → 产出: 截图 PNG + DOM盒树 JSON + 设计token JSON + meta      │
└──────────────────────────────────────────────────────────────┘
┌── 被检物管线（本项目侧）─────────────────────────────────────┐
│ Avalonia headless Skia 渲染（现有 CaptureRenderedFrame 基建） │
│   → 同视口同 DPR 截图 PNG                                     │
│   → VisualTree 盒树 dump（新增，与 DOM JSON 同构）            │
│ 固定 fixture: PlaceholderChatSession + InMemoryFileSystem     │
└──────────────────────────────────────────────────────────────┘
                 │                        │
                 ▼                        ▼
┌── 对比引擎（三层，报告驱动修复）──────────────────────────────┐
│ L1 token 对等:  dsh-tokens.json → 生成 C# 资源 → 逐值断言     │
│ L2 几何对等:    映射表配对 box → ±1px / 字号 / 颜色断言        │
│ L3 像素对等:    帧 diff + 文本掩膜 + 热力图 + 差异白名单       │
│ 产出: diff-report.json + heatmap.png → 修复循环 → 收敛        │
└──────────────────────────────────────────────────────────────┘
```

---

## 三、参考物管线（DSH 侧）— 已原型验证

### 3.1 已验证产物（2026-10-17 实跑）

| 产物 | 实测 | 内容 |
|------|------|------|
| `dsh-web-1440x900.png` | 38,887 字节 | 深色工作台完整截图（侧栏/composer/hero） |
| `dsh-dom-1440x900.json` | 188 元素 | 每元素 box + bg/color/字号/圆角/padding/gap |
| `dsh-tokens.json` | 421 个 CSS 变量 | 双主题各 407 个解析值（static 色板 + alias 语义 + 字体 + 动效） |
| `dsh-meta.json` | — | 版本/视口/DPR 锚定 |

### 3.2 工具链（固化到 `non_deliverables_tools/dsh_parity/`）

- `@deepseek-ai/dsh` 固定版本安装（`npm install`，package.json 锁版本），`DSH_HOME` 指向隔离目录，`--no-open` 防打扰
- playwright-core 操控系统 Edge（`msedge.exe`），CDP 驱动：navigate → 等稳定 → 截图 → DOM 提取 → token 提取（含暗色属性切换）
- Windows 无 Chrome 时用 Edge；不下载浏览器

### 3.3 踩坑记录（已解决）

| 坑 | 根因 | 解法 |
|----|------|------|
| `msedge --headless --screenshot --virtual-time-budget=N` 永久挂起 | DSH UI 保持 WebSocket 长连接，虚拟时间永不推进，截图命令不返回 | 弃 CLI 截图，改 playwright CDP 主动截屏（`page.screenshot`） |
| 脚本挂起后遗留 node 进程 | 启动器被沙箱回收但子进程存活 | 单次调用内完成"启动→捕获→按 PID 击杀"，并校验 CommandLine 归属 |

### 3.4 场景矩阵（捕获脚本参数化，逐场景扩展）

| 场景 | 状态 | 说明 |
|------|------|------|
| home（空会话） | ✅ 已捕获 | 侧栏 + hero + composer |
| session（对话中） | 待做 | 需注入样例对话（DSH llm-mock-server 或预置 session） |
| settings（设置面板） | 待做 | 点击设置入口 |
| permission（权限弹窗） | 待做 | 需触发审批 |
| question（询问弹窗） | 待做 | 需触发 ask_user |
| slash（斜杠面板） | 待做 | 输入 `/` |
| 窗口尺寸变体 | 待做 | 1440x900 基准 + 1024x768 响应式抽查 |

---

## 四、被检物管线（本项目侧）

- **截图**：Avalonia headless Skia 渲染（既有基建 `test/unit/join_code_gui.tests` 的 `CaptureRenderedFrame` + `SavePng`，TASK011/GUI001 已用于像素断言与人工核对帧），同视口 1440x900 @DPR=1
- **盒树 dump（新增）**：遍历 Avalonia VisualTree 输出与 `dsh-dom-*.json` **同构**的 JSON（元素路径/box/背景刷/字号/圆角/padding），产出路径统一 `dumps/gui_parity/`
- **固定 fixture**：PlaceholderChatSession + InMemoryFileSystem（测试隔离已有），保证场景可复现

---

## 五、对比引擎（三层）

### L1 — token 对等（CI 快速门，毫秒级）

- 单一数据源：`dsh-tokens.json`（**禁止手抄**，遵守 AGENTS.md 规则 11 硬编码变委托）
- `ui-parity-map.json` 定义 DSH token → 本项目资源键的映射（`--dsw-alias-bg-base` → `Palette.BgBase` 等）
- 由生成器（build 脚本或源生成器）把 token JSON 编译为 C# 资源数据 → 消除手抄
- 测试：逐 token 断言值精确相等（含 alpha）；映射表缺项即红灯

### L2 — 几何对等（CI，headless）

- `ui-parity-map.json` 定义区域映射：DSH CSS 选择器/类名 ↔ Avalonia 控件路径（如 `[data-testid=composer]` ↔ `InputBarView`）
- 断言：box x/y/w/h ±1px、字号、颜色、可见性；未映射区域报告为覆盖率缺口
- 产出：`diff-report.json`（{element, expected, actual, delta}，按 delta 排序定位最大偏差）

### L3 — 像素对等（本地/手动门，对齐 ADR 0080 验收）

- 同尺寸帧逐像素比对：非文本区域 diff 比例、文本区掩膜（字形光栅化不可比）、SSIM
- 产出：`heatmap.png`（差异热力图）+ 比例报告；阈值 ≤0.5% 非文本差异
- 差异白名单文件（登记 CSS 特效近似项），禁止无记录豁免

---

## 六、修复循环（报告驱动）

```
捕获参考物 → L1/L2/L3 跑出 diff-report → 按 delta 排序修复
     ↑                                        │
     └────── 重跑三层验证收敛 ←────────────────┘
```

- 修复对象按层：token 不符 → 改生成数据源；几何不符 → 改 XAML 布局规格；像素不符 → 看热力图定位
- 每轮产物留档 `dumps/gui_parity/{timestamp}/`，收敛轨迹可审计

---

## 七、卫星工具结构（`non_deliverables_tools/dsh_parity/`）

```
dsh_parity/
  package.json            # 锁定 @deepseek-ai/dsh + playwright-core 版本
  capture/                # 参考物管线（node + ps1 编排）
    capture-dsh.mjs       #   CDP 截图 + DOM + token 提取（已有原型）
    capture-all.ps1       #   启动 dsh → 捕获 → 击杀（已有原型）
    scenarios.json        #   场景矩阵定义（选择器/操作脚本）
  compare/                # 对比引擎（L1/L2/L3）
  maps/
    ui-parity-map.json    #   token 映射 + 区域映射
    diff-whitelist.json   #   差异白名单（CSS 特效近似等）
  reference/              # 参考物 golden（PNG + JSON，入库）
```

- 工具为**非交付品**（不合入 slnx / CI build matrix），独立运行
- 本机需 Node（已具备 v26.4.0）与 Edge；无网络时参考物用入库 golden

---

## 八、渲染路线决策（本体系对两条路线都适用）

| 路线 | 做法 | 优点 | 代价 |
|------|------|------|------|
| **A. Avalonia 原位重构** | 保留技术栈，按 DSH token+布局规格逐区域重构 | 零栈变更、既有测试/VM 全保留、AOT 稳 | 字形与 CSS 特效非 bit-exact（几何/颜色精确） |
| **B. WebView2 内核** | GUI 改壳 + HTML/CSS（可参考 DSH CSS，MIT），同 Chromium 渲染 | 真·像素级（同引擎）、可复用 DSH 样式 | 需卫星 spike 验证 AOT+COM 集成；Avalonia 视图层废弃；引入前端构建链 |

**推荐：先落地本校验体系（两路线共用），用 A 路线逐区域对齐；B 路线以卫星 spike 并行验证，若 WebView2+AOT 打通且对齐质量差距显著再切换。** 校验体系与 token 数据源保持渲染器无关，切换成本可控。

---

## 九、阶段任务

| # | 任务 | 说明 |
|---|------|------|
| P0 | 固化 capture 原型入 `dsh_parity/` | 已有脚本 + 版本锁定 + .gitignore 处理 node_modules |
| P1 | 场景矩阵扩展 | session/settings/permission/question/slash 五场景捕获脚本 |
| P2 | L1 token 生成器 + 对等测试 | dsh-tokens.json → C# 资源，CI 毫秒级门 |
| P3 | 本项目侧盒树 dump + L2 对比 | VisualTree dump 同构 JSON + 映射表 + diff-report |
| P4 | L3 像素 diff + 热力图 + 白名单 | 本地门 + ADR 0080 人工核对流程 |
| P5 | 按区域重构 GUI（依路线决策） | 侧栏/composer/对话区/设置/弹窗逐个收敛 |
| P6 | CI 集成 + 验收记录 | L1/L2 入 CI；L3 走手动 exe 验收留档 |

---

## 十、风险与边界

| 风险 | 缓解 |
|------|------|
| DSH 版本升级导致参考物漂移 | 固定版本入库 golden；升级显式重捕获 + diff 审计 |
| 字体光栅化差异被误判为对齐失败 | 文本掩膜 + 白名单登记，指标只看几何与颜色 |
| playwright/Edge 环境依赖 | golden 入库，离线可比对；capture 仅重捕获时需要 Node |
| token 生成器触碰 AOT | build 期生成 C# 源码（无运行时反射），遵守既有生成器模式 |
| 过早承诺 bit-exact | 验收标准按第一节阈值表执行；白名单 reviewer 可见 |

---

## 十一、原型证据索引

- 卫星工具（P0 已固化）：`non_deliverables_tools/dsh_parity/`
  - 编排：`capture/capture-all.ps1`（隔离 DSH_HOME + `--no-open` + 按 PID 击杀）
  - 捕获：`capture/capture-dsh.mjs`（CDP/playwright-core；`colorScheme` 固定主题；首次运行引导弹窗自动关闭）
  - 运行：`powershell -NoProfile -ExecutionPolicy Bypass -File capture/capture-all.ps1 [-Viewport WxH] [theme]`
- 实测版本：`@deepseek-ai/dsh@0.2.0-rc.2`（npm，公开包）+ `playwright-core@1.64.1`；Edge x86；Node v26.4.0
- 参考物（golden，入库）：`non_deliverables_tools/dsh_parity/reference/{dsh-web-1440x900.png, dsh-dom-1440x900.json(183 元素), dsh-tokens.json(421 变量/双主题各 407), dsh-meta.json}`
- 本项目侧基建：`test/unit/join_code_gui.tests/views/DialogRenderTests.cs`（headless 帧捕获与像素断言先例）
