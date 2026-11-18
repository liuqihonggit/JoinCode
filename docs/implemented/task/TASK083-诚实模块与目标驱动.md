# TASK083 — 诚实模块与目标驱动提示词

> 本文件是**诚实清单的可视化版本**，与 `resources/honesty/checklist.yaml` 同源。
> 每次用户切换模型，CLI 先复述**已确认的目标**（取自 `IGoalEngine` 的运行时状态，不是从对话推测），
> 再只列这里**未完成**的项 —— 已完成的项不复述，否则真正的缺口会被一堆 `[x]` 埋掉。
> 未完成项**不得自动执行**，必须由用户选择方向；确认后再逐项打勾。

## 目标

1. 用户切换模型时，主动说明哪些能力可能因为换模型而遗漏。
2. 每次都诚实列出「已做什么 / 还缺什么 / 证据是什么」，让用户决定方向。
3. 把目标驱动写进提示词：先问目标，无目标不执行；推断出的需求逐条确认；模糊目标说明好坏。

## 检查清单

- [x] **Agent 内部基础工具**：5 个工具内化进 jcc，每轮注册并出现在首轮提示词。
  - 证据：commit `a466cdd69`；真机 `jcc tool base` 9 个；`jcc prompt --section tools` 逐行列出。
- [x] **诚实模块**：模型切换时先复述已确认的目标，再只列清单里的未完成项，不自动执行。
  - 证据：真机隔离目录 `/model` 输出「已确认的目标」段 + 未完成项段；把一条临时改成 `pending` 后只列该条，
    还原后打印「15 项全部有验证证据，无未完成项」；目标引擎未就绪 / 状态加载失败两种情形都打印「无法核实」
    而不是「（无）」（`HonestyNoticeTests` 5 条锁住这三条界线）；`Composition.Tests` 1079 通过。
  - 同时修掉：`/model` 经 `slash_call` 调用时把整串 `{"model_id":"x"}` 当模型名写进配置（改用 `GetSplitArgs`）。
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
- [x] **密钥红线按值判定**：不再按参数名拦 `--key`/`--token`/`--secret`，改为按密钥语义 + 值形态判定。
  - 证据：真机 `jcc tool call search search_symbol --key theme` 不再被阻断；`--api-key sk-xxx`、`--token ghp_xxx`、`--api-key=sk-xxx` 仍被拦（等号形式修复前漏检）；`host.tests` 1329 通过（新增 21 条）。详见 [TASK088](TASK088-密钥红线按值判定.md)。
- [x] **配置存储源收敛**：`/theme`、GUI、`config_get`/`config_set` 对同一键给同一答案；存储源由契约层唯一清单决定，旧 `settings.json` 的 global 键一次性搬走。
  - 证据：真机同目录交叉验证 —— `/theme light` 写 `global.json` 后 `jcc tool call system config_get --setting theme` 读到 `light`；工具写 `dark` 后 `/config get theme` 读回 `dark`；GUI 探针 `GuiSessionProbe` 读写一致；`config_source_probe` 三场景 12 项断言通过。详见 [TASK089](TASK089-配置存储源分叉.md)。
- [x] **配置值语义归一**：`config_get model` 与 `config_set model` 落在同一个字段（`vendor.{当前供应商}.model`），嵌套键（`permissions.defaultMode`）按清单声明的 JSON 路径读写；写盘失败不再报成功。启动输出从 126 行降到 10 行，控制台与 `jcc_error.log` 默认不含栈帧。详见 [TASK090](TASK090-配置读取语义归一.md)。
  - 证据：真机隔离目录 `config_get model` 读 `vendor.p1.model`、`config_set` 后两处读回一致且只改该字段；`permissions.defaultMode` 写入后 `PermissionChecker` 读到；`Guard.Config.Tests` 1305 通过。
- [x] **配置键收敛为唯一清单**：`ConfigKey` 枚举 + `SupportedSettings` + `SettingSourceMap` + `SettingPathMap` 四张表合并为 `ConfigKeyRegistry`（20 键），配置写入只改一个字段，配置迁移带版本号。详见 [TASK091](TASK091-配置键唯一清单与迁移框架.md)。
  - 证据：真机 `config_get effortLevel` 从「Unknown setting」变为可读写、`config_list` 输出 20 键并分组、`config-migrations.json` 只记录一次迁移；`ConfigKeyRegistryTests` 13 条 + `ConfigMigrationRunnerTests` 8 条通过。
- [x] **文件签名保持**：有 BOM 保留且只一个、没有就不新增，编码也跟着原文件走。详见 [TASK092](TASK092-文件签名保持.md)。
  - 证据：真机 8 场景 —— `settings.json` 无 BOM 写入前后都是 `7b 22 63`、带 BOM 仍 `ef bb bf` 且不重复；`file edit` 三种编码分别保持；`ConfigFileEncodingTests` 4 条（临时回退旧实现实测 3 红）+ `BclBridge` 41 条通过。
- [x] **斜杠命令 JSON 参数归一化**：`slash_call` 传的 `key=value`/JSON 不再被当成整串参数值，40 个 `GetNormalizedArgs` 命令零改动兼容。详见 [TASK094](TASK094-斜杠命令JSON参数归一化.md)。
  - 证据：真机隔离目录 `vendor vendor=openai` 从「未知供应商 `{"vendor":"openai"}`」变为正常切换；`effort`/`color`/`trust`/`hooks`/`thinkback`/`goal` 单值与多段全对；`GetNormalizedArgs` 对 JSON 复用 `GetSplitArgs` 抽值拼回文本、非 JSON 原样；`SlashCallArgumentTests` 11 条（先红 4 条）+ `Host.Tests` 1336 + `Composition.Tests` 1079 通过。

## 用户决策记录

| 日期 | 用户方向 | 对应动作 |
|---|---|---|
| 2026-11-17 | 本轮五项全做，但不得用单元测试替代真机 | 每项完成均启动 jcc/exe 校验 |

## 纪律

- 清单里的 `[ ]` 项只能由用户明确选择后推进；AI 不得自作主张跳过或默默完成。
- 打勾必须附真机证据（命令 + 输出摘要），不接受「单测通过」作为唯一证据。
- 模型切换提示只读清单，不改配置以外的任何状态；提示后等待用户决定。
