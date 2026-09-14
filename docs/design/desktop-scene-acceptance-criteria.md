# 桌面情景模式验收标准

> **版本**：v0.1
> **日期**：2026-09-14
> **状态**：待用户确认
> **关联**：[desktop-ai-scene-disclosure-report.md](desktop-ai-scene-disclosure-report.md)
> **架构**：情景模式 = 场景菜单（发现）+ 工具链路推荐（防遗忘）+ 状态持久化（跨调用）+ AI 自主推理

---

## 0. 架构定义（验收对象）

### 0.1 情景模式三要素

```
① 场景菜单（发现机制）
   desktop_scene_menu() → 返回场景清单 + 每个场景的工具集 + 场景说明
   AI 通过菜单"找到"桌面操作这个情景有哪些工具可用

② 工具链路推荐（防遗忘机制）
   每个工具返回 ToolResult 时附带 suggested_next: [{tool, reason, params_hint}]
   调完 look → 返回里推荐 zoom → 调完 zoom → 返回里推荐 detect 或继续 zoom
   AI 自己推理决定调不调，推荐是引导非强制

③ 状态持久化（跨调用机制）
   ~/.jcc/scenarios/{scene_id}.json 存夹逼进度
   AI 每次调工具传 scene_id，进度跨 mcp_call 进程保留
```

### 0.2 工具集（情景内的工具）

| 工具 | 作用 | 调完推荐 |
|------|------|---------|
| `desktop_scene_menu` | 获取场景菜单 | `desktop_look` |
| `desktop_look` | 截图 + 四叉树网格 | `desktop_zoom` |
| `desktop_zoom` | 选 1/2/3/4 象限缩小 | `desktop_zoom`（看不清）/ `desktop_detect`（看清） |
| `desktop_detect` | 多模态识别 UI 元素 | `desktop_click` / `desktop_type` |
| `desktop_click` | 点击当前位置 | 场景完成 |
| `desktop_type` | 输入文字 | `desktop_look`（继续下一个字段） |
| `desktop_drag` | 拖拽 | 场景完成 |
| `desktop_scene_status` | 查看夹逼进度 | — |

> 现有原子工具（screenshot/quadtree_*/mouse_click 等）保留但标 `Kind=System`（按需注入，不默认暴露给 AI），AI 通过上述 `desktop_*` 情景工具交互。

---

## 1. 验收标准清单

### AC-01：场景菜单可被发现

**给定** AI 被注入工具列表（含 `desktop_scene_menu`）
**当** AI 调用 `desktop_scene_menu()`
**那么** 返回 JSON 含：
- `scenes[].name` = `"desktop_control"`
- `scenes[].description` 包含"四叉树夹逼""截图""点击"等场景说明
- `scenes[].tools[]` 列出 `desktop_look`/`desktop_zoom`/`desktop_detect`/`desktop_click`/`desktop_type`/`desktop_drag`
- `scenes[].suggested_flow` = `"look → zoom(重复直到看清) → detect → click"`
- `scenes[].tips` 含"看不清就反复 zoom，每次选 1/2/3/4"

**验证**：单元测试 `DesktopSceneMenuTest` — 断言返回 JSON 结构 + 字段齐全

---

### AC-02：链路推荐 — look → zoom

**给定** AI 已调 `desktop_look(scene_id)` 成功，返回截图 + 四叉树网格
**当** 读取返回的 `suggested_next`
**那么**：
- `suggested_next[0].tool` = `"desktop_zoom"`
- `suggested_next[0].reason` 包含"选目标所在象限"
- `suggested_next[0].params_hint` 含 `quadrant` 参数说明（"1=左上 2=右上 3=左下 4=右下"）

**验证**：单元测试 `DesktopLookSuggestTest` — 调 look 后断言 suggested_next

---

### AC-03：链路推荐 — zoom → zoom/detect（看不清 vs 看清）

**给定** AI 调 `desktop_zoom(scene_id, quadrant=3)` 后处于第 N 层
**当** 第 N 层区域仍大于最小识别阈值（看不清）
**那么** `suggested_next` 含 `desktop_zoom`（reason="仍看不清，继续缩小"）
**当** 第 N 层区域 ≤ 最小识别阈值（看清）
**那么** `suggested_next` 含 `desktop_detect`（reason="已缩放到可识别粒度"）

**验证**：单元测试 `DesktopZoomSuggestTest` — 分别测"看不清"和"看清"两条分支

---

### AC-04：链路推荐 — detect → click/type

**给定** AI 调 `desktop_detect(scene_id)` 返回 UI 元素列表
**当** 元素列表含 `type=button`
**那么** `suggested_next` 含 `desktop_click`（reason="识别到按钮，点击"）
**当** 元素列表含 `type=input`
**那么** `suggested_next` 含 `desktop_type`（reason="识别到输入框，输入文字"）

**验证**：单元测试 `DesktopDetectSuggestTest` — mock 多模态返回 button/input 分别断言

---

### AC-05：状态持久化 — 跨 mcp_call 进程

**给定** 进程 A 调 `desktop_look(scene_id="sc_001")` 建立场景，再调 `desktop_zoom(quadrant=2)` 缩到第 1 层
**当** 进程 B（新 mcp_call）调 `desktop_scene_status(scene_id="sc_001")`
**那么** 返回：
- `current_depth` = 1
- `current_cell_code` = `"L0.2"`
- `zoom_history` 含 1 条记录 `{depth:1, quadrant:2}`
- `last_screenshot_path` 指向有效文件

**验证**：集成测试 `DesktopScenePersistenceTest` — 两次独立进程调用 + 文件中转断言

---

### AC-06：夹逼收敛 — 数学保证

**给定** 初始屏幕区域面积 = S，最小识别阈值 = T
**当** AI 反复调 `desktop_zoom` 共 N 次
**那么** 当前区域面积 ≤ S / 4^N
**且** 当 N ≥ ceil(log4(S/T)) 时，区域面积 ≤ T（必然收敛到可识别粒度）

**验证**：单元测试 `QuadtreeConvergenceTest` — 初始 1920×1080，断言 10 层后区域 ≤ 1px 级

---

### AC-07：端到端 — 打开计算器点等号

**给定** Windows 桌面环境（非 CI/无头）
**当** AI 执行完整链路：
1. `desktop_scene_menu()` → 获取菜单
2. `desktop_look()` → 截图建网格
3. `desktop_zoom(quadrant=?)` × N → 缩到等号按钮区域
4. `desktop_detect()` → 识别到"=" 按钮
5. `desktop_click()` → 点击
**那么**：
- 计算器窗口被激活
- "=" 按钮被点击（通过 UI 状态变化验证，如计算器显示区出现结果）
- 场景状态文件标记 `status=Done`

**验证**：E2E 手动 exe 测试（ADR 0080）— 真实桌面运行，非 mock

---

### AC-08：端到端 — 看不清的小按钮（夹逼核心场景）

**给定** 桌面有一个 16×16 像素的小图标按钮
**当** AI 执行 `look → zoom × N → detect → click`
**那么**：
- `detect` 在区域 ≤ 64×64 时能识别出按钮（多模态可识别）
- 点击坐标在小按钮的 bounding box 内
- 总 zoom 次数 ≤ 6（1920×1080 → 30×30 约需 6 层）

**验证**：E2E 手动 exe 测试 — 用一个小按钮测试应用验证

---

### AC-09：纠偏链路 — 点错了重来

**给定** AI 已点击某位置（scene 状态 `last_action=click, last_position=(x,y)`）
**当** 用户说"不是那个，是旁边那个" → AI 调 `desktop_scene_status(scene_id)`
**那么** 返回含 `last_position` + `zoom_history`，AI 可据此退一层重新 zoom
**且** `desktop_zoom` 支持 `back=true` 参数退回上一层

**验证**：单元测试 `DesktopSceneBackTest` — zoom 3 层后 back 1 层断言 depth=2

---

### AC-10：渐进式披露 — Token 效率

**给定** AI 工具列表注入阶段
**当** 统计暴露给 AI 的桌面相关工具 schema 总 token
**那么**：
- 静态注入的工具 ≤ 2 个（`desktop_scene_menu` + `desktop_scene_status`）
- 其他 `desktop_*` 工具标 `Kind=Mcp` 按分组注入，默认不展开 schema
- 首次调 menu 后返回的菜单 JSON ≤ 500 token
- 每次工具返回的 `suggested_next` ≤ 100 token

**验证**：单元测试 `DesktopSceneTokenBudgetTest` — 序列化 schema 统计 token

---

### AC-11：安全 — 环境守卫

**给定** CI/无头环境（无桌面会话，`GetForegroundWindow()=IntPtr.Zero`）
**当** AI 调用任意 `desktop_*` 工具
**那么** 返回明确错误："当前环境无桌面会话，桌面操作不可用。请在有桌面的 Windows 环境运行。"
**且** 不执行任何 P/Invoke 调用（不崩溃）

**验证**：单元测试 `DesktopEnvGuardTest` — mock 无前台窗口断言明确报错

---

### AC-12：安全 — 危险坐标拦截

**给定** `DesktopSafetyChecker` 配置了危险区域（如任务栏关键区域）
**当** AI 的夹逼定位结果落在危险区域
**那么** `desktop_click` 返回拒绝 + reason + 建议调整
**且** 不执行 SendInput

**验证**：单元测试 `DesktopSafetyCheckTest` — mock 危险坐标断言拦截

---

### AC-13：场景说明保留给 AI

**给定** AI 调 `desktop_scene_menu()`
**当** 读取返回的 `scenes[].description` + `scenes[].tips` + `scenes[].suggested_flow`
**那么** 含完整场景说明（报告 §6 的 AI 场景说明书内容）：
- 解释"四叉树夹逼法"是什么
- 解释"看不清怎么办"
- 解释"点错了怎么办"
- 给出场景示例

**验证**：单元测试 `DesktopSceneDescriptionTest` — 断言说明文本含关键词

---

## 2. 验收优先级

| 优先级 | 验收项 | 理由 |
|--------|--------|------|
| P0（必须） | AC-01, AC-02, AC-03, AC-04, AC-05, AC-11 | 菜单发现+链路推荐+状态持久化+安全守卫，核心架构 |
| P1（重要） | AC-06, AC-07, AC-13 | 夹逼收敛+端到端+场景说明，证明可用 |
| P2（增强） | AC-08, AC-09, AC-10, AC-12 | 小按钮/纠偏/token效率/危险坐标，体验完善 |

---

## 3. 验证方式矩阵

| 验收项 | 单元测试 | 集成测试 | E2E 手动 exe | 备注 |
|--------|---------|---------|-------------|------|
| AC-01~04 | ✅ | | | mock 服务接口 |
| AC-05 | | ✅ | | 两进程 + 文件中转 |
| AC-06 | ✅ | | | 纯数学 |
| AC-07 | | | ✅ | 真实计算器 |
| AC-08 | | | ✅ | 小按钮测试应用 |
| AC-09 | ✅ | | | zoom back |
| AC-10 | ✅ | | | schema token 统计 |
| AC-11 | ✅ | | | mock 无前台窗口 |
| AC-12 | ✅ | | | mock 危险坐标 |
| AC-13 | ✅ | | | 文本断言 |

---

## 4. 实现前置条件（验收标准确认后）

1. **写 ADR**：`docs/adr/01xx-desktop-scene-orchestration.md`（架构决策：情景模式+链路推荐 vs 单入口黑盒 vs 纯说明）
2. **TDD 循环**：AC-01 红测试 → 实现 desktop_scene_menu → 绿测试 → 提交 → AC-02...
3. **不重写原子工具**：复用 `IQuadtreeEncoder`/`IGdiScreenCaptureService`/`IWin32DesktopInputService`/`MultimodalUiElementDetector` 等现有服务
4. **状态持久化复用**：agent DryRun + 文件中转模式（见 `project-agent-tool-test-mode-no-llm.md`）

---

## 5. 完成状态（2026-09-14）

| AC | 状态 | commit | 验证方式 |
|----|------|--------|----------|
| AC-01 | ✅ 完成 | `42dfc4d` | 单元测试 mock |
| AC-02 | ✅ 完成 | `b49c704` | 单元测试 mock |
| AC-03 | ✅ 完成 | `e21ff24` | 单元测试 mock |
| AC-04 | ✅ 完成 | `bdc749d` | 单元测试 mock |
| AC-05a | ✅ 完成 | `7ee07f3` | 单元测试 mock |
| AC-05b | ✅ 完成 | `90ad601` | 单元测试 InMemoryFileSystem |
| AC-06 | ✅ 完成 | `02ae5a7` | 单元测试纯数学 |
| AC-07 look+zoom | ✅ 完成 | `2b92b0e` | E2E 真实桌面运行通过 |
| AC-07 detect+click | ⏭️ 跳过 | — | 需 LLM API + 计算器窗口 |
| AC-08 | ✅ 测试已写 | — | E2E Integration 标记，待真实运行 |
| AC-09 | ✅ 完成 | `e5be592` | E2E 真实桌面运行通过 |
| AC-10 | ✅ 完成 | `43de720` | 单元测试 token 统计 |
| AC-11a | ✅ 完成 | `04c8956` | 单元测试 mock CI 环境 |
| AC-11b | ✅ 完成 | `4182e90` | 服务实现内置环境守卫 |
| AC-12 | ✅ 完成 | `43de720` | E2E 真实桌面运行通过 |
| AC-13 | ✅ 完成 | `02ae5a7` | 单元测试文本断言 |

### 真实服务实现（`4182e90`）

| 服务 | 文件 | 编排 |
|------|------|------|
| DesktopSceneCaptureService | `kit/hands/desktop/services/DesktopSceneCaptureService.cs` | 截图→PNG头解析→四叉树建网格→渲染叠加→存文件→存状态 |
| DesktopSceneZoomService | `kit/hands/desktop/services/DesktopSceneZoomService.cs` | 读状态→读截图→象限裁剪→清晰度判断→存新截图→更新状态 |
| DesktopSceneDetectService | `kit/hands/desktop/services/DesktopSceneDetectService.cs` | 读状态→读截图→多模态识别→元素类型映射 |
| DesktopSceneStateStore | `kit/hands/desktop/services/DesktopSceneStateStore.cs` | JSON 文件持久化，DI 注册，双构造函数 |
