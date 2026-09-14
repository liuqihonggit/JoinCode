# 0105. 桌面情景模式编排层（场景菜单 + 工具链路推荐 + 状态持久化）

- 状态：accepted
- 日期：2026-09-14
- 决策者：用户（惊惊）+ AI
- 落地日期：2026-09-14（P0+P1+P2 全部完成，AC-07 detect+click 因需 LLM API 环境跳过）

> 关联：[desktop-ai-scene-disclosure-report.md](../design/desktop-ai-scene-disclosure-report.md)、[desktop-scene-acceptance-criteria.md](../design/desktop-scene-acceptance-criteria.md)、[ADR 0032](0032-computeruse-win32-pinvoke.md)、[多模态隐喻显露工具-PRD](../design/多模态隐喻显露工具-PRD.md)

## 背景

项目已实现桌面自动化全部原子能力：四叉树编码/缩放/渲染、GDI 截图、Win32 点击/拖拽/键盘输入、多模态 UI 元素检测、窗口枚举/聚焦、桌面叠加动画。但这些是**散落的 20+ 个细粒度 MCP 工具**，存在三个问题：

1. **AI 看不见代码**：AI 只能看工具 name/description/schema。296 个工具全塞给 AI 会爆 context（~30k token），桌面场景只需 5-6 个，AI 被淹没且不知编排顺序。
2. **夹逼进度跨调用丢失**：每次 mcp_call 是新进程（见 [project-plan-state-no-cross-process-persistence](../../C:/Users/54076/.codeartsdoer/memory/D--project-w1/project-plan-state-no-cross-process-persistence.md)），四叉树夹逼的"当前层/当前格子/历史路径"无法累积，每次从零开始。
3. **AI 遗忘下一步**：调完 screenshot 后 AI 可能不知道该调 quadtree_zoom，无链路引导。

用户需求（来自聊天记录）：把"四叉树夹逼→识图→点击"做成 AI 可执行的工作，场景信息保留给 AI，**面向交互非面向数据**（AI 能逐步参与/纠偏，不是黑盒）。

## 决策

采用**情景模式编排层**，三要素：

### 1. 场景菜单（发现机制）
新增 `desktop_scene_menu` 工具，AI 调用后返回场景清单 + 工具集 + 场景说明 + 建议流程。AI 通过菜单"找到"桌面操作情景有哪些工具可用，渐进式披露（不一次塞 20 个 schema）。

### 2. 工具链路推荐（防遗忘机制）
每个 `desktop_*` 工具返回 `ToolResult` 时附带 `suggested_next: [{tool, reason, params_hint}]`。调完 `desktop_look` → 推荐 `desktop_zoom` → 调完 `desktop_zoom` → 推荐 `desktop_detect` 或继续 `desktop_zoom`。**AI 自己推理决定调不调，推荐是引导非强制**。

### 3. 状态持久化（跨调用机制）
`~/.jcc/scenarios/{scene_id}.json` 存夹逼进度（当前层/格子/历史/截图路径）。AI 每次调工具传 `scene_id`，进度跨 mcp_call 进程保留。复用 agent DryRun + 文件中转方案（见 [project-agent-tool-test-mode-no-llm](../../C:/Users/54076/.codeartsdoer/memory/D--project-w1/project-agent-tool-test-mode-no-llm.md)）。

### 工具集
`desktop_scene_menu`（菜单）、`desktop_look`（截图+网格）、`desktop_zoom`（缩放）、`desktop_detect`（识图）、`desktop_click`（点击）、`desktop_type`（输入）、`desktop_drag`（拖拽）、`desktop_scene_status`（查进度）。现有原子工具（screenshot/quadtree_*/mouse_click 等）保留但标 `Kind=System` 按需注入，不默认暴露给 AI。

## 替代方案

### 方案 A：纯说明文档
只在工具 description 里写清用法 + prompt section。**放弃原因**：296 工具 schema 全塞 AI 爆 context，无运行时引导，无状态持久化。

### 方案 B：说明 + 状态机
方案 A + 状态文件持久化，AI 仍一步步调原子工具。**放弃原因**：token 占用仍高（20+ 工具全展开），AI 仍可能调错顺序（无链路推荐）。

### 方案 C：单入口高层黑盒（desktop_locate_and_click）
一个工具，AI 传入"点保存按钮"，内部自动夹逼→识图→点击。**放弃原因**：AI 看不见中间过程，无法纠偏/介入（场景⑦"不是那个是旁边那个"无法表达），违背用户"面向交互非面向数据"要求。

### 方案 D：单入口菜单分发（desktop_scene + action 枚举）
一个 `desktop_scene` 工具，action=menu/look/zoom/...。**放弃原因**：与方案 C 类似偏黑盒，且 action 枚举不如独立工具的链路推荐灵活——独立工具每个都能被 AI 单独推理选择，链路推荐是返回时附带而非参数分发。

**情景模式（本决策）与方案 D 的区别**：本决策是**多工具 + 链路推荐**（每个工具独立，返回时推荐下一步），方案 D 是**单工具 + action 分发**。本决策更符合"AI 自己推理"——AI 看到多个工具自主选择，而非一个工具的 action 参数。

## 后果

- **正面**：
  - Token 极省（静态只注入 2 个工具 `desktop_scene_menu` + `desktop_scene_status`，其余按需）
  - AI 引导性强（菜单发现 + 链路推荐防遗忘）
  - 交互灵活（AI 每步可纠偏/换动作，面向交互）
  - 状态跨调用保留（夹逼进度不丢）
  - 复用全部现有原子工具（不重写四叉树/截图/点击）

- **负面**：
  - 新增 8 个工具 + 状态持久化层，增加代码量
  - 链路推荐逻辑需维护（每个工具的 suggested_next 需正确）
  - 状态文件需清理（场景完成后删 scene 文件）

- **中性**：
  - 现有原子工具对 AI 隐藏（标 Kind=System），高级用户仍可直调
  - 状态存储位置 `~/.jcc/scenarios/` 与 agent DryRun 的 `~/.jcc/agents/` 同级
