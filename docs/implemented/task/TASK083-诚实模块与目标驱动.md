# TASK083 — 诚实模块与目标驱动提示词

> 本文件是**诚实清单的可视化版本**，与 `resources/honesty/checklist.yaml` 同源。
> 每次用户切换模型，CLI 会复述这里的状态：哪些已完成、哪些可能遗漏。
> 未完成项**不得自动执行**，必须由用户选择方向；确认后再逐项打勾。

## 目标

1. 用户切换模型时，主动说明哪些能力可能因为换模型而遗漏。
2. 每次都诚实列出「已做什么 / 还缺什么 / 证据是什么」，让用户决定方向。
3. 把目标驱动写进提示词：先问目标，无目标不执行；推断出的需求逐条确认；模糊目标说明好坏。

## 检查清单

- [x] **Agent 内部基础工具**：5 个工具内化进 jcc，每轮注册并出现在首轮提示词。
  - 证据：commit `a466cdd69`；真机 `jcc tool base` 9 个；`jcc prompt --section tools` 逐行列出。
- [x] **诚实模块**：模型切换时打印本清单，明示可能遗漏项，不自动执行。
  - 证据：真机 `jcc slash_call /model model_id=probe-honesty` 打印 1 个 `[x]`、3 个 `[ ]` 及「确认前不会动手」；
    测试后已还原 `settings.json`。
- [x] **目标驱动提示词**：先问目标、无目标不执行、逐条确认、模糊目标说明利弊。
  - 证据：真机 `jcc prompt --section goal_driven` 输出四条硬规则，并接线 `goal_get` / `goal_graph_define` / `goal_update`。
- [x] **清理特性排序**：`AppModule` / `PromptSection` 的 `Order` 数字排序改为注册名 + YAML。
  - 证据：`Verify-ExplicitOrder.py` 真机核对 jcc 段落顺序与 `sections.yaml`、模块顺序与 `modules.yaml` 一致，特性里无 `Order`。
- [x] **JCC_DUMP_MESSAGES 在 `-p` 链路落盘**：`MessageDumpMiddleware` 挂在 QueryEngine 管道；假 key 真机跑出 19KB `chat_*.txt`（含 `[System]` 与工具提示词）。
- [x] **`/debuglog -p` 经 `slash_call` 生效**：`GetSplitArgs` 解析 JSON 字段；真机 `slash_call /debuglog '{"flags":"-p"}'` 输出「═══ 系统提示词 ═══」。
  - 边界：`/debuglog` 每段仍截断到 500 字符，看全文用 `jcc prompt`。
- [x] **接口层位次改为注册顺序表派生**：6 个接口的 `Priority` / `Order` 全部改为由 `resources/orders.yaml` 派生，代码里不再有手写数字。
  - 证据：真机 `JCC_DEBUGLOG=1 jcc mcp_call bash` 打出 `[ORDER] command_guards: GitCommitGuard > DriveProtectionGuard > CmdIndirectCallGuard > RobocopyMirrorGuard > HeredocGuard > GhPrBodyGuard > GhTimeoutGuard > VpnRouteGuard`，与 YAML 及迁移前基线逐项一致；`Verify-RegistrationOrder.py` 双向对账通过并已进 CI 门禁。
- [x] **gui profile 装配归一**：手写 YAML 解析 + switch 已删除，改为生成器产出的 `GuiModuleCatalog`；gui 会话首次真机启动。
  - 证据：真机探针 `artifacts/bin/GuiSessionProbe` 建会话成功（退出码 0），`[MODULE]` 顺序 `CoreModule > SlashCommandModule > ClockModule > BrowserModule > HousekeepingModule > McpInitModule` 与 gui profile 逐项一致；已进 CI `module-order` job。
- [x] **工具参数契约统一**：`--参数 值` 打通 245 个 host 工具（此前 100% 报「未知选项」），未知参数名按工具参数表给精确引导。
  - 证据：真机 `jcc tool call search search_symbol --query "TaskUpdate"` 成功执行；`jcc mcp_call glob --pattern '*.md'` 与旧形式 `key=value` 均通；`--patern` 报「可用参数: --pattern, --path; 是不是想写 --pattern?」；`host.tests` 1308 通过。详见 [TASK086](TASK086-工具参数契约统一.md)。
- [x] **参数名归一化两条路径统一**：规则下沉契约层，exe 与宿主对 camelCase/snake_case 给出同一答案。
  - 证据：真机 `jcc-tool-file.exe read --filePath "AGENTS.md"` 成功（修复前报「缺少必填参数 --file_path」）；`Verify-ToolParamNames.py` 117 项抽查全通；契约层 `JoinCode.CliNodes.Tests` 48 通过。详见 [TASK087](TASK087-参数名归一化两条路径统一.md)。

## 用户决策记录

| 日期 | 用户方向 | 对应动作 |
|---|---|---|
| 2026-11-17 | 本轮五项全做，但不得用单元测试替代真机 | 每项完成均启动 jcc/exe 校验 |

## 纪律

- 清单里的 `[ ]` 项只能由用户明确选择后推进；AI 不得自作主张跳过或默默完成。
- 打勾必须附真机证据（命令 + 输出摘要），不接受「单测通过」作为唯一证据。
- 模型切换提示只读清单，不改配置以外的任何状态；提示后等待用户决定。
