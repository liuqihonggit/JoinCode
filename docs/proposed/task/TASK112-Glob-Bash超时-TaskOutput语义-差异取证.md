# TASK112 — Glob 大小写 / Bash 超时 / TaskOutput 语义（TASK103 §2.3 #45 取证）

> 状态：**Glob/Bash 判不适用；TaskOutput 挂专项设计** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#45 / TASK071-C5**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文三项中两项描述过时或属配置体系差异，一项是新行为：

1. **Glob**：原文"大小写敏感/无条数上限"与两侧代码都不符。本仓 `SearchService:63/70` 用 `OrdinalIgnoreCase` 匹配与去重，`SearchService:110-111` 取 `Take(100)` 并置 `truncated`，上限常量 `WorkflowConstants.DefaultSearchResultLimit=100`（`:208`）；基准也有 `globLimits?.maxResults ?? 100`（`GlobTool.ts:157`）和 `utils/glob.ts:126-127` 的截断，且大小写由 rg `--glob` 的平台默认决定（`glob.ts:100-107`，未传 `-i`）。**两边都截断 100；差别只是本仓恒忽略大小写**。改成区分大小写会让既有调用漏命中，无收益，保留。
2. **Bash 超时**：基准 `BASH_DEFAULT_TIMEOUT_MS`/`BASH_MAX_TIMEOUT_MS`（`utils/timeouts.ts:12-39`）控制默认 120s/上限 600s；本仓 `timeout` 参数默认 180s（`ShellToolHandlers.cs:49`），硬上限走 `AbsoluteTimeoutMiddleware` + `JCC_ABSOLUTE_TIMEOUT_SECONDS`（`ShellExecutionConfig.cs:33-36`），**根本不消费那两个基准 env**。接 env 是新增配置契约（与本仓 `JCC_*` 体系并列双源），不是断链；保留本仓口径。附带记录：基准 Bash 上限在 `BashTool.tsx:860` 未做 `Math.min` 钳制（PowerShell 版做了），属基准自身行为，不跟随。
3. **TaskOutput**：原文"无 block/timeout"属实，但**两者不是同一个工具**：基准 `TaskOutput` 读的是后台 shell 任务（`TaskOutputTool.tsx:30-34` block 默认 true / timeout 30s / 上限 600s，`:118-143` 每 100ms 轮询直到脱离 running/pending）；本仓 `task_output`（`TaskOutputToolHandlers.cs:31-74`）读的是 todo 任务库，另有真正的后台 shell 输出工具 `shell_background_output`（`ShellToolHandlers.cs:192-211`）。本仓已有等待原语 `ISystemActuatorRegistry.WaitForTaskAsync`（`SystemActuatorRegistry.cs:206-215`），但两个输出工具都未暴露 block/timeout。这是**新行为**（默认阻塞会改变调用时机与最长等待），按 #40/#42 同一边界挂专项，无人值守不实现。

---

## 1. 四问回答

### 问1 专家隐性实践
"同一个工具名在两个产品里指向不同域"是常见迁移坑：基准 `TaskOutput` 对应后台进程输出，而本仓 `task_output` 是待办任务查询。判差异前必须先按**消费的数据源**而不是工具名配对，否则会把"块等待"接到 todo 列表上，造出一个既不像基准也不像本仓的混合语义。超时类配置同样要按产品自身的 env 命名空间判断，不能因为基准叫 `BASH_*` 就直接照搬。

### 问2 原始素材（一手）
- 基准 Glob：`tools/GlobTool/GlobTool.ts:26-36`（pattern/path）、`:157`（maxResults 默认 100）、`utils/glob.ts:100-107`（rg `--files --glob <pattern> --sort=modified`，无大小写开关）、`:126-127`（截断 + truncated 标记）。
- 本仓 Glob：`modules/cognition/hands_core/tool_handlers/dev_tools/handlers/SearchToolHandlers.cs:171-175`（pattern/path）、`services/SearchService.cs:63/70/110-111`、`abstractions.primitives.configuration/src/app_data/WorkflowConstants.cs:208/224`（100 条、100000 字符）。
- 基准 Bash：`tools/BashTool/BashTool.tsx:227-259`（timeout schema）、`:860`（`timeout || getDefaultTimeoutMs()`）、`:881-898`（传 exec）；`utils/timeouts.ts:2-3/12-39`（120s 默认、600s 上限 + 两个 env）。
- 本仓 Bash：`modules/cognition/hands_shell/shell/handlers/tool_handlers/ShellToolHandlers.cs:45-55`（timeout 默认 180000ms）、`:67/92`（写 context.Timeout）；`middleware/AbsoluteTimeoutMiddleware.cs:33-50/72-89`（硬上限与打断结果）；`src/app_data/ShellExecutionConfig.cs:36`（AbsoluteTimeoutSeconds=180）+ `JccEnvVar.cs:244`（`JCC_ABSOLUTE_TIMEOUT_SECONDS`）。
- 基准 TaskOutput：`tools/TaskOutputTool/TaskOutputTool.tsx:30-34`（block/timeout/max）、`:118-143`（轮询）、`:219-281`（not_ready/timeout/success 三种结果）。
- 本仓 TaskOutput：`modules/mcp/mcp.tool_handlers/task/core/TaskOutputToolHandlers.cs:31-74`（todo 任务一次性快照）；`modules/cognition/hands_shell/shell/handlers/tool_handlers/ShellToolHandlers.cs:192-211`（后台 shell 输出一次性快照）；`modules/cognition/hands_shell/system_actuator/abstractions/SystemActuatorRegistry.cs:206-215`（现成等待原语）。

### 问3 具体方法与工具
两侧工具定义/schema/服务实现逐行对照；对 Glob 直接核对"上限常量 + 截断分支"，对 Bash 全仓 grep 两个基准 env 的消费者（本仓零命中），对 TaskOutput 按数据源追踪到 `ITaskService`（todo）与 `ISystemActuatorRegistry`（后台进程）两条链。

### 问4 独立验证来源
本仓 `SearchService.cs` 注释自称对齐 TS；benchmark 截断由 `glob.ts:126-127` 独立印证。Bash env 由 `utils/timeouts.ts` 与 `JccEnvVar.cs` 各自独立定义，属于两套配置体系。TaskOutput 的等待原语在本仓已有实现且被 `WaitForTaskAsync` 接口公开（`ISystemActuatorRegistry.cs:54`），证明"缺的是参数面与结果语义"，不是缺底层能力。

---

## 2. 差异与判定

| 项 | 基准 | 本仓 | 判定 |
|---|---|---|---|
| Glob 大小写 | rg 默认（未显式忽略） | 恒 `OrdinalIgnoreCase` | 行为差异但更宽容；改严会漏命中 → 保留 |
| Glob 上限 | 100（可配置） | 100 固定 + 100000 字符体积上限 | 已对齐/更严 → 原文过时 |
| Bash 默认超时 | 120s，env 可覆盖 | 180s，`JCC_ABSOLUTE_TIMEOUT_SECONDS` | 配置体系差异 → 保留 |
| Bash 最大超时 | 600s（且未钳制） | 180s 硬上限（中间件真实截断） | 本仓更稳 → 保留 |
| TaskOutput 数据源 | 后台 shell 任务 | todo 任务（另有 shell_background_output） | 工具名同、域不同 |
| TaskOutput block/timeout | 支持等待 | 一次性快照；有底层等待原语 | 新行为 → 专项 |

---

## 3. TaskOutput 专项启动条件

若要实现，应先回答：①挂在哪个工具上（`shell_background_output` 才是真后台输出，`task_output` 不应改）；②默认 `block` 是 true（对齐基准，改变现有调用时机）还是 false（兼容现状）；③超时后返回什么（`not_ready`/`timeout` 状态 + 已产出部分输出，还是错误）；④是否要把 todo `task_output` 也改造成等待语义（不建议，会混淆两个域）。四问答完再改 handler schema + CLI 节点帮助 + 等待原语的取消/超时链路，配 TDD。

---

## 4. 验收（取证项，无代码改动）

- 两侧上限/超时/等待逻辑均以 `file:line` 记录于 §1。
- 本仓 `BASH_DEFAULT_TIMEOUT_MS`/`BASH_MAX_TIMEOUT_MS` 全仓零消费（grep 验证）。
- 总账 #45 保留 🟡（判据两项不适用 + TaskOutput 专项），指向本文档。
