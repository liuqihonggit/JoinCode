# 桌面自动化 AI 场景渐进式披露报告

> 📍 **导航**: [docs/](../README.md) › [design/](README.md) | **前置**: [adr/](../adr/README.md)
> 🔗 **上游索引**: [design/README.md](README.md) — 修改本文档后须同步更新此索引

> **版本**：v0.1
> **日期**：2026-09-14
> **状态**：调研 / 方案评估（待用户确认实现方向）
> **背景**：用户（惊惊）在聊天中提出"四叉树夹逼→识图→点击"的桌面控制思路，要求把这些场景做成 AI 可执行的工作，且场景信息要保留给 AI。本报告解决核心矛盾：**AI 看不见代码，如何让它知道有这些工具并知道怎么用**。
> **关联**：[ComputerUse-PRD.md](ComputerUse-PRD.md)、[多模态隐喻显露工具-PRD.md](多模态隐喻显露工具-PRD.md)、[ADR 0032](../adr/0032-computeruse-win32-pinvoke.md)

---

## 0. 核心矛盾

AI（作为工具调用方）**看不见项目源码**，只能看到工具的 `name` / `description` / `inputSchema`。本项目有 **296 个 MCP 工具**，其中桌面自动化相关约 20+ 个。若全部塞给 AI：

- **context 爆炸**：296 个工具 schema 约 30k+ token，桌面场景只需 5-6 个
- **AI 迷失**：工具太多 AI 不知道该按什么顺序调用，不知道"四叉树夹逼"这个编排逻辑
- **状态断裂**：每次 `mcp_call` 是新进程（见 `project-plan-state-no-cross-process-persistence.md`），夹逼进度（当前层/当前格子）无法跨调用累积

**本报告评估四种方案，推荐"渐进式披露菜单 + 状态机持久化"，并产出可直接喂给 AI 的场景说明书。**

---

## 1. 行业模式对比

### 1.1 三种"AI 控制桌面"范式

| 范式 | 代表 | 机制 | 对模型要求 | Token 效率 |
|------|------|------|-----------|-----------|
| **A. 直接给坐标** | Anthropic Computer Use (`computer_20241022`) | 截图→AI 看图直接返回像素坐标 (x,y)→click | 高（需强空间推理，大模型） | 中（每步一张截图） |
| **B. 结构化元素** | OpenAI CUA / Operator | 截图→DOM/Accessibility 树→AI 选元素 id→click | 低（选 id 非算坐标） | 高（元素列表紧凑） |
| **C. 四叉树夹逼** | **本项目** | 截图→画网格→AI 选 1/2/3/4 象限→缩小→重复→识别按钮→click | **最低**（只选数字 1-4，小模型可用） | **最高**（每步只传 4 选 1） |

**本项目四叉树夹逼（范式 C）的优势**：
- 对小模型/弱多模态友好：AI 只需回答"1/2/3/4"，不需要算像素坐标
- Token 极省：每步 4 选 1，而非整张图 + 坐标
- 抗幻觉：格子编号是稳定锚点（`L0.2.1`），比"右上方那个东西"可靠
- 夹逼收敛：数学上保证 N 层后区域 ≤ 原图 / 4^N

**范式 C 的代价**：多步有状态流程，需要编排层 + 状态持久化（这正是本报告要解决的）。

### 1.2 Anthropic Computer Use 工具设计（公开 schema 摘要）

Anthropic 用**单个工具** `computer_20241022` + `action` 参数分发：

```
computer(action, coordinate?, text?)
  action ∈ {screenshot, click, double_click, triple_click,
            move_cursor, type, key, hold_key, scroll,
            left_click_drag, wait, left_mouse_down, left_mouse_up}
```

- **一个工具覆盖所有桌面操作**，靠 action 枚举分发——这是"菜单形式"的极简版
- AI 看截图后自己算坐标，不需要四叉树（依赖强模型）
- **无状态**：每次 screenshot 是独立的，不累积缩放进度

### 1.3 对本项目的启示

| 启示点 | Anthropic 做法 | 本项目应取 |
|--------|---------------|-----------|
| 工具粒度 | 单工具 + action 枚举 | **菜单入口工具**（渐进式披露） |
| 定位方式 | AI 算像素坐标 | **四叉树夹逼**（AI 选 1-4） |
| 状态 | 无状态 | **状态机持久化**（夹逼进度跨调用） |
| 让 AI 知道工具 | 工具 description 写清 | **场景说明书 + 菜单动态展开** |

---

## 2. 用户场景话术模拟

> **说明**：以下话术基于中文桌面自动化用户的常识性表达模拟。网络搜索（Bing）未返回精准语料库（结果被 B 站下载器等无关内容淹没），此处基于 AutoHotkey / PyAutoGUI / RPA 社区的常见用户指令模式 + 中文口语习惯构造。**语气特征**：口语化、目标导向、省略主语、混用"点/按/弄/搞"、常带模糊指代（"那个"/"右边那个"）。

### 2.1 场景分类与真实话术

#### 场景①：打开软件 + 单击按钮（最常见）
```
"帮我打开计算器，点那个等号"
"开个记事本，把这句话打进去：今天天气不错"
"打开微信，点一下扫一扫"
"帮我开个画图，随便画两笔"
```
**语气特征**：动词连用（打开→点），"那个"指代依赖屏幕上下文，AI 必须先截图才能消解指代。

#### 场景②：表单填写（多步序列）
```
"在这个网页里，姓名填张三，电话填13800138000，然后点提交"
"把这个表格填一下：第一行写标题，第二行写日期，保存"
"登录一下，账号用admin，密码123456，登完帮我截个图"
```
**语气特征**：多个字段 + 顺序动作，AI 需要维护"填到哪一步"的状态。

#### 场景③：文件/窗口拖拽（连续坐标操作）
```
"把桌面上那个叫'报告'的文件夹拖到D盘"
"把这个窗口移到左边去"
"把这张图片拖到聊天框里发出去"
```
**语气特征**：涉及两个位置（源/目标），AI 需要分别定位再拖拽。

#### 场景④：看不清的小按钮（夹逼核心场景）
```
"那个按钮太小了，你放大看看再点"
"右下角有个很小的图标，帮我点一下"
"这个下拉菜单里选第三项，选项很密你仔细看"
```
**语气特征**：**直接触发四叉树夹逼**——用户已暗示"看不清"，AI 应主动进入缩放循环。

#### 场景⑤：跨应用操作（复杂编排）
```
"先截图发给我，然后把这个表格的数据复制到Excel里"
"把这边的内容选中，切到另一个窗口粘贴"
"帮我从这个软件导出数据，存到桌面，再打开邮件发出去"
```
**语气特征**：跨窗口、跨应用，AI 需要窗口枚举 + 焦点切换 + 多步操作。

#### 场景⑥：游戏/娱乐（实时交互）
```
"帮我点一下游戏里的开始按钮"
"这个棋盘上，帮我把棋子从这儿挪到那儿"
"一直按着空格键别松手，我说停你再停"
```
**语气特征**：实时性、持续按住、坐标依赖游戏画面，AI 需要识图 + 实时输入。

#### 场景⑦：模糊/纠错（用户修正）
```
"不是那个，是旁边那个"
"再往右一点"
"点错了，撤销一下"
"嗯对就是这个，点它"
```
**语气特征**：用户反馈纠偏，AI 需要记住上一次操作位置并做相对调整。

### 2.2 话术→工具编排映射

| 话术场景 | 触发工具链 | 状态需求 |
|---------|-----------|---------|
| ① 打开+单击 | `window_focus` → `screenshot` → `quadtree_build` → `quadtree_zoom`×N → `mouse_click` | 夹逼层级 |
| ② 表单填写 | `screenshot` → (`quadtree_zoom` → `keyboard_type`)×每个字段 → `mouse_click`(提交) | 字段进度 |
| ③ 拖拽 | `screenshot` → 定位源 → `screenshot` → 定位目标 → `mouse_drag` | 源/目标坐标 |
| ④ 看不清 | `screenshot` → `quadtree_build` → `quadtree_zoom`×N(直到看清) → `mouse_click` | **夹逼历史** |
| ⑤ 跨应用 | `window_enumerate` → `window_focus`×2 → 各自操作 | 窗口句柄 |
| ⑥ 游戏 | `screenshot` → `quadtree_zoom` → `mouse_click` / `keyboard_key` | 实时循环 |
| ⑦ 纠错 | 读上次状态 → 相对调整 → `mouse_click` | **上次操作位置** |

**关键发现**：场景④⑦最依赖状态持久化——夹逼历史和上次位置必须跨调用保留，否则 AI 每次从零开始无法纠偏。

---

## 3. 现有原子工具盘点（AI 可用清单）

> 以下工具**已全部实现**，散落在 `kit/hands/desktop/handlers/` 和 `server/vision/tool_handlers/`。按场景分组，标注 ToolCategory。

### 3.1 视觉感知组（ToolCategory.Vision / DesktopControl）

| 工具名 | 作用 | 位置 |
|--------|------|------|
| `screenshot` | 全屏/窗口/区域截图，返回 base64 PNG | `WindowManagementToolHandlers.cs` |
| `quadtree_build` | 对截图建四叉树网格，返回格子编码集 | `QuadtreeToolHandlers.cs` |
| `quadtree_zoom` | 聚焦某格子，递归细分，返回子网格+子图 | `QuadtreeToolHandlers.cs` |
| `quadtree_paint` | 给格子染色/画虚线标注 | `QuadtreeToolHandlers.cs` |
| `quadtree_render` | 渲染网格叠加图返回 base64 | `QuadtreeToolHandlers.cs` |
| `quadtree_to_screen_rects` | 格子编码→屏幕绝对坐标矩形 | `QuadtreeDesktopOverlayToolHandlers.cs` |
| `look_at_cursor` | 获取鼠标位置 + 四叉树递归截图 | `DesktopOverlayToolHandlers.cs` |
| `show_desktop_overlay` | 在桌面画框（视觉反馈） | `DesktopOverlayToolHandlers.cs` |

### 3.2 输入执行组（ToolCategory.DesktopControl / Interaction）

| 工具名 | 作用 | 位置 |
|--------|------|------|
| `mouse_click` | 单击/双击/右击/中击，支持坐标 | `DesktopInputToolHandlers.cs` |
| `mouse_drag` | 拖拽，20 步插值平滑移动 | `DesktopInputToolHandlers.cs` |
| `keyboard_type` | 输入文字 | `Win32DesktopInputService.cs` |
| `keyboard_key` | 按键/组合键（SendInput） | `Win32DesktopInputService.cs` |
| `right_click_menu` | 右键菜单复合操作 | `CompoundOperationToolHandlers.cs` |
| `drag_with_hover` | 拖拽+悬停复合 | `CompoundOperationToolHandlers.cs` |
| `multi_click` | 多次点击 | `CompoundOperationToolHandlers.cs` |

### 3.3 窗口管理组（ToolCategory.DesktopControl）

| 工具名 | 作用 | 位置 |
|--------|------|------|
| `window_enumerate` | 枚举所有可见窗口 | `WindowManagementToolHandlers.cs` |
| `window_focus` | 激活窗口（Alt 键技巧解除前台锁定） | `WindowManagementToolHandlers.cs` |
| `window_move` | 移动/调整窗口 | `WindowManagementToolHandlers.cs` |
| `window_close` | 关闭窗口 | `WindowManagementToolHandlers.cs` |

### 3.4 多模态识别组（ToolCategory.Vision）

| 工具名 | 作用 | 位置 |
|--------|------|------|
| `detect_ui_elements` | 截图→多模态 LLM→结构化 UI 元素列表 | `VisionToolHandlers.cs` + `MultimodalUiElementDetector.cs` |

### 3.5 缺口总结

| 有 | 缺 |
|----|-----|
| 四叉树编码/缩放/渲染 ✅ | **编排层**（把上述工具串成夹逼→识图→点击）❌ |
| 截图/点击/输入/拖拽 ✅ | **状态持久化**（夹逼进度跨调用）❌ |
| 多模态识图 ✅ | **AI 场景入口**（让 AI 知道有这些工具+怎么编排）❌ |
| 桌面叠加动画 ✅ | **渐进式披露菜单**（按需展开，不爆 context）❌ |

---

## 4. 方案评估

### 方案 A：纯说明文档（工具描述里写清用法）

**做法**：在每个原子工具的 `description` 里写清"本工具用于四叉树夹逼点击场景，用法是先调 X 再调 Y"，再写一个总览 prompt section。

| 维度 | 评价 |
|------|------|
| 实现成本 | 最低（只改 description 文本） |
| Token 占用 | **高**（296 个工具 schema 全塞给 AI，桌面相关 20+ 个全展开） |
| AI 引导性 | 弱（AI 读了描述但仍可能乱调顺序，无运行时引导） |
| 状态持久化 | ❌ 无 |
| 夹逼进度累积 | ❌ 无（每次从零开始） |

**结论**：不够。AI 会被 296 个工具淹没，且无法累积夹逼状态。

### 方案 B：说明 + 状态机（持久化夹逼进度）

**做法**：方案 A + 新增场景状态文件 `~/.jcc/scenarios/{scene_id}.json`，存当前层/当前格子/夹逼历史。AI 仍一步步调原子工具，但状态跨调用保留。

| 维度 | 评价 |
|------|------|
| 实现成本 | 中（加状态持久化层，参考 agent DryRun + 文件中转方案） |
| Token 占用 | 高（工具仍全展开） |
| AI 引导性 | 中（有状态但无编排引导，AI 仍可能调错顺序） |
| 状态持久化 | ✅ |
| 夹逼进度累积 | ✅ |

**结论**：状态问题解决了，但 AI 引导性和 token 问题没解决。

### 方案 C：说明 + 状态机 + 工作流（高层编排工具）

**做法**：方案 B + 新增一个高层工具 `desktop_locate_and_click`，AI 一次调用传入"点那个保存按钮"，工具内部自动循环夹逼→识图→点击。

| 维度 | 评价 |
|------|------|
| 实现成本 | 高（编排逻辑封装进工具，需调多模态 LLM 选象限） |
| Token 占用 | **最低**（AI 只调 1 个工具） |
| AI 引导性 | 强（黑盒，AI 不需要知道编排） |
| 状态持久化 | ✅（工具内部维护） |
| 夹逼进度累积 | ✅ |
| **灵活性** | ❌ **差**（AI 看不见中间过程，无法纠偏/介入，场景⑦"不是那个是旁边那个"无法表达） |

**结论**：对简单场景好，但对用户纠偏/介入场景太黑盒。聊天里惊惊强调"面向交互不是面向数据"，AI 应能逐步参与。

### 方案 D：渐进式披露菜单 + 状态机（推荐）

**做法**：
1. **一个菜单入口工具** `desktop_scene`（ToolCategory.DesktopControl），description 极简："桌面操作场景入口，调用获取可用动作菜单 + 当前夹逼状态"。AI 只看到这 1 个工具（而非 20+ 个）。
2. AI 调 `desktop_scene({action: "menu"})` → 返回**动态菜单**（JSON，非 schema）：
   ```json
   {
     "scene_id": "sc_20260914_001",
     "current_state": "未开始 / 正在夹逼第3层 / 已定位到按钮 / 已点击",
     "actions": [
       {"id": "look",     "label": "看一眼屏幕",          "hint": "截图+四叉树网格标注"},
       {"id": "zoom",     "label": "缩小到某区域",        "hint": "画十字问1/2/3/4，选一个"},
       {"id": "detect",   "label": "识别这里有什么按钮",   "hint": "多模态识图"},
       {"id": "click",    "label": "点击当前位置",        "hint": "左键单击"},
       {"id": "type",     "label": "输入文字",            "hint": "键盘输入"},
       {"id": "drag",     "label": "拖拽到某处",          "hint": "源→目标拖拽"},
       {"id": "status",   "label": "查看夹逼进度",        "hint": "当前层/格子/历史路径"}
     ],
     "suggested_flow": "look → zoom(重复直到看清) → detect → click",
     "tip": "看不清就反复 zoom，每次选 1/2/3/4 缩小到目标所在象限"
   }
   ```
3. AI 选一个动作 → `desktop_scene({action: "zoom", quadrant: 3, scene_id: "sc_..."})` → 工具内部调 `quadtree_zoom` + `screenshot` → 返回结果 + 更新后的菜单
4. **状态持久化**：`scene_id` 绑定 `~/.jcc/scenarios/{scene_id}.json`，存夹逼进度。跨 `mcp_call` 进程通过文件中转（与 agent DryRun 同方案）
5. **原子工具不暴露给 AI**（或标 `Kind=System` 按需注入），AI 只通过 `desktop_scene` 这一个入口交互

| 维度 | 评价 |
|------|------|
| 实现成本 | 中（1 个编排工具 + 状态文件，复用现有原子工具） |
| Token 占用 | **极低**（AI 只见 1 个工具 schema，菜单是运行时 JSON 返回） |
| AI 引导性 | **强**（菜单引导顺序，suggested_flow 明示编排） |
| 状态持久化 | ✅ |
| 夹逼进度累积 | ✅ |
| **灵活性** | ✅ **好**（AI 每步都能看状态、纠偏、换动作，面向交互） |
| 渐进式披露 | ✅（按需展开，不一次塞 20 个工具） |

**结论**：**推荐方案 D**。兼顾 token 效率、AI 引导性、状态持久化、交互灵活性。这正是用户说的"渐进式披露 + 菜单形式"。

---

## 5. 推荐方案 D 详细设计

### 5.1 架构

```
AI（只看到 1 个工具）
  └─ desktop_scene(action, ..., scene_id?)
       │
       ├─ action=menu  → 返回动态菜单 JSON（按当前状态展开可用动作）
       ├─ action=look  → 内部调 screenshot + quadtree_build → 返回截图+网格+菜单
       ├─ action=zoom  → 内部调 quadtree_zoom(quadrant) → 返回子图+菜单
       ├─ action=detect→ 内部调 detect_ui_elements → 返回元素列表+菜单
       ├─ action=click → 内部调 mouse_click → 返回结果+菜单
       ├─ action=type  → 内部调 keyboard_type → 返回结果+菜单
       ├─ action=drag  → 内部调 mouse_drag → 返回结果+菜单
       └─ action=status→ 读场景状态文件 → 返回夹逼进度
       │
       └─ 状态持久化：~/.jcc/scenarios/{scene_id}.json
            ├─ scene_id, created_at, target_description
            ├─ current_depth, current_cell_code (如 "L0.2.1")
            ├─ zoom_history: [{depth, cell, quadrant, timestamp}]
            ├─ last_screenshot_path
            └─ last_action, last_result
```

### 5.2 状态机

```
状态枚举：
  Idle → Looking → Zooming → Detecting → Located → Clicked → Done
                                  ↑              │
                                  └── 看不清 ────┘ (回 Zooming)

转换：
  menu()        : 任意状态 → 返回菜单（不改状态）
  look()        : Idle/Located → Looking（截图+建网格）
  zoom(q)       : Looking/Zooming → Zooming（缩到第 q 象限，depth+1）
  detect()      : Zooming → Detecting（多模态识图）
                  Detecting → Located（识别到按钮）/ → Zooming（看不清继续夹逼）
  click()       : Located → Clicked → Done
  type(text)    : Located → Done
  drag(target)  : Located → Done
```

### 5.3 渐进式披露层级

| 层级 | AI 看到什么 | 何时展开 |
|------|------------|---------|
| L0（静态 schema） | 只有 `desktop_scene` 1 个工具，description ~100 字 | 工具列表注入时 |
| L1（首次调 menu） | 7 个动作的菜单 JSON | AI 主动请求 |
| L2（选某动作） | 该动作的执行结果 + 更新后的菜单 | AI 选择后 |
| L3（detect 展开） | UI 元素列表（按钮/输入框/标签） | AI 调 detect 后 |

**每层只比上层多一点点信息**，AI 不会一次性被淹没。

### 5.4 状态持久化（跨进程）

复用 agent DryRun + 文件中转方案（见 `project-agent-tool-test-mode-no-llm.md`）：
- 场景状态写 `~/.jcc/scenarios/{scene_id}.json`（RelaxedJsonSerializer，AOT 兼容）
- `desktop_scene` 每次调用先按 `scene_id` 加载状态，执行后写回
- 截图存 `~/.jcc/scenarios/{scene_id}/shot_{depth}_{cell}.png`，返回路径（非 base64，省 token）
- AI 拿到 `scene_id` 后续调用传入即可恢复进度

### 5.5 与现有架构对齐

| 约束 | 对齐方式 |
|------|---------|
| NativeAOT | 纯 POCO + JSON 序列化，无反射 emit |
| ToolCategory | 新增工具归 `ToolCategory.DesktopControl` |
| `[McpTool]` + 源码生成器 | 用特性标注，自动注册 |
| 状态持久化 | 文件中转（与 agent DryRun 同模式） |
| 安全 | 复用 `DesktopSafetyChecker` + `DesktopEnvironmentGuard`（CI/无头环境明确报错） |
| 复用现有原子工具 | `desktop_scene` 内部调 `IQuadtreeEncoder` / `IGdiScreenCaptureService` / `IWin32DesktopInputService` 等已有服务接口，不重写 |

---

## 6. AI 场景说明书（可直接用作工具 description / prompt section）

> **以下内容可直接嵌入 `desktop_scene` 工具的 description，或作为 prompt section 注入。这是"场景信息保留给 AI"的产物。**

```markdown
# 桌面操作场景（desktop_scene）

你可以通过 `desktop_scene` 工具操作 Windows 桌面：看屏幕、定位按钮、点击、输入文字、拖拽。

## 为什么用这个工具
桌面上有按钮/输入框/菜单，但你（AI）看不见屏幕。这个工具用"四叉树夹逼法"帮你定位：
1. 先截图，把屏幕分成 4 块（左上1/右上2/左下3/右下4），画十字标注
2. 你选目标在哪个块（回答 1/2/3/4）
3. 把那块再分 4 块，你再选
4. 重复直到能看清单个按钮
5. 点击它

这叫"夹逼法"——每次缩小 3/4，几步后就能精确定位。

## 怎么用
1. 调 `desktop_scene(action="menu")` 获取动作菜单 + 场景 ID
2. 按菜单提示选动作，每次传入 `scene_id` 保持进度
3. 典型流程：look（看一眼）→ zoom（缩小，重复直到看清）→ detect（识别按钮）→ click（点击）

## 动作清单
- **look**：截图 + 画四叉树网格，返回带标注的图。第一次必调。
- **zoom**：选 1/2/3/4 象限缩小，返回更清晰的子图。看不清就反复 zoom。
- **detect**：多模态识别当前区域有哪些 UI 元素（按钮/输入框），返回列表。
- **click**：在当前定位位置点击（左键/右键/双击）。
- **type**：在当前定位位置输入文字。
- **drag**：从当前位置拖到另一个位置。
- **status**：查看当前夹逼到第几层、在哪个格子、历史路径。

## 看不清怎么办
用户说"太小了""放大看看""右下角那个"→ 你应该反复 zoom 直到 detect 能识别出按钮名字。
每次 zoom 只需回答 1/2/3/4，不需要算坐标。

## 点错了怎么办
用户说"不是那个，是旁边那个"→ 调 status 看上次位置，退一层重新 zoom 选另一个象限。

## 场景示例
- "帮我打开计算器点等号" → look → zoom 到等号所在区域 → detect 确认 → click
- "那个按钮太小了放大点" → 反复 zoom → detect → click
- "姓名填张三电话填138xxx然后提交" → look → zoom到姓名框 → type → zoom到电话框 → type → zoom到提交 → click
```

---

## 7. 实现路线（待用户确认后执行）

| 阶段 | 内容 | 依赖 |
|------|------|------|
| P1 | `desktop_scene` 工具骨架 + 状态文件读写 + menu 动作 | 现有原子工具服务接口 |
| P2 | look/zoom 动作（截图+四叉树）+ 状态机转换 | `IGdiScreenCaptureService` + `IQuadtreeEncoder` |
| P3 | detect/click/type/drag 动作 + 状态机完整 | `MultimodalUiElementDetector` + `IWin32DesktopInputService` |
| P4 | 渐进式披露菜单动态生成 + suggested_flow 引导 | P1-P3 |
| P5 | 单元测试（状态机转换/菜单生成/文件持久化）+ E2E 手动 exe 验证 | P1-P4 |

**不重写**：四叉树/截图/点击/识图/窗口管理/桌面叠加——全部复用现有实现。

---

## 8. 待用户确认的决策点

1. **方案选择**：确认走方案 D（渐进式披露菜单 + 状态机）？还是 A/B/C？
2. **工具命名**：入口工具叫 `desktop_scene` 还是其他（如 `desktop_act` / `computer_use`）？
3. **原子工具可见性**：现有 20+ 个桌面工具是否对 AI 隐藏（只留 `desktop_scene` 入口），还是保留可见（高级用户可直接调）？
4. **状态存储位置**：`~/.jcc/scenarios/` 还是项目内 `.scenarios/`？
5. **是否先写 ADR**：此方案涉及新增编排层 + 状态持久化机制，按 AGENTS.md 应先写 ADR（`docs/adr/01xx-desktop-scene-orchestration.md`）再实现？
