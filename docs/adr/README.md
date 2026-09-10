# ADR — 架构决策记录

> **ADR (Architecture Decision Records)** 记录项目中重要的架构决策：**为什么**选 A 而不选 B，以及后果是什么。

## 为什么需要 ADR

项目已有 `docs/design/`（怎么实现）、`docs/plans/`（什么时候做）、`docs/tasks/`（做什么），但缺少**决策文档**（为什么这样选）。决策散落在 `AGENTS.md`、各 design 文档、代码注释中，难以回溯。

ADR 用固定格式收编这些决策，形成不可变的历史记录。

## 目录规范

- 文件名：`NNNN-kebab-case-title.md`（NNNN=四位序号，从 0001 开始）
- 语言：简体中文
- 不可变：决策一经 accepted 不再修改内容，只改状态（superseded/deprecated）

## 模板

```markdown
# NNNN. 标题

- 状态：proposed | accepted | superseded by NNNN | deprecated
- 日期：YYYY-MM-DD
- 决策者：

## 背景

（为什么需要这个决策，当时面临什么问题）
## 决策

（最终选了什么）
## 替代方案

（考虑过但没选的方案，及放弃原因）
## 后果

- 正面：
- 负面：
- 中性：
```

## 状态标注规范

> AI 通常只读文档前 8K token，superseded/部分取代信息必须在标题后醒目标注，确保不被遗漏。

| 状态 | 标注要求 | 格式 |
|------|---------|------|
| accepted | 无需额外标注 | — |
| proposed | 无需额外标注 | — |
| superseded by NNNN | **必须**在标题后添加 blockquote 标注块 | `> ⚠️ **已被 [NNNN](NNNN-xxx.md) 取代** — 原因简述` |
| 部分被取代 | **必须**在标题后添加 blockquote 标注块 | `> ⚠️ **部分内容已被 [NNNN](NNNN-xxx.md) 取代** — 哪些被取代，哪些仍有效` |

## 模板字段变体

> 工程**指南/规范/禁令**类 ADR（如排错指南、测试规则、平台禁令）记录的是"怎么做"而非"选 A 放弃 B"，可省略 `## 决策` 和 `## 后果` 节，用 `## 详细内容`/`## 规范`/`## 禁令` 等节代替。传统架构决策 ADR 必须包含完整模板字段。

| ADR 类型 | 决策 | 替代方案 | 后果 | 可用替代节名 |
|---------|------|---------|------|-------------|
| 架构决策 | 必需 | 必需 | 必需 | — |
| 工程指南/规范/禁令 | 可省 | 可省 | 可省 | `## 详细内容`/`## 规范`/`## 禁令` |
| 有好处/坏处 | 必需 | 必需 | 可用变体 | `## 好处` + `## 坏处` 代替 `## 后果` |
| superseded | 必需 | 可省 | 可省 | 已被取代,替代方案/后果见取代者 |

## 与其他文档的关系

| 文档 | 职责 | 示例 |
|------|------|------|
| `docs/adr/` | **为什么**这样决策 | 为什么用 slnx 隔离而非单 sln |
| `docs/design/` | **怎么**实现 | 七层 slnx 的具体依赖链和编译顺序 |
| `docs/plans/` | **什么时候**做 | 重构执行计划和里程碑 |
| `docs/tasks/` | **做什么** | 具体任务清单 |

ADR 引用 design/plans，但不重复其内容。

## 粒度策略

本项目采用**架构级 + 组件策略级 + 工程级**三层，函数级决策留在代码注释或 design 文档中。

## 不适用范围（禁止写成 ADR）

ADR 是**统筹架构决策**的文档（"为什么选 A 放弃 B"）。以下内容**不属于架构决策**，禁止写成 ADR：

- **Bug 修复报告** — bug 修复是"修对了什么"，不是架构取舍。根因+修复+验证记录在 commit message + 测试里
- **功能开发日志** — 新功能实现过程不是决策。用 `docs/tasks/` 或 commit message
- **代码审查记录** — review 发现不是决策。用 PR comment
- **排错/调试过程** — 调试过程不是决策。用 commit message 或 `docs/design/`

**判断标准**：如果"替代方案"部分是空的或凑数的（没有真正"考虑过但放弃"的方案），那它大概率不是 ADR，而是 bug 报告或功能日志。

**正确做法**：bug 修复用 commit message（含根因+修复+验证），TDD 用测试复现，不需要 ADR。只有跨模块的"为什么选 A 放弃 B"才写 ADR。

## 统计

- 总数：**97** | accepted：**90** | superseded：**5** | proposed：**2**

## 完整索引（按编号）

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0001](0001-seven-layer-slnx-isolation.md) | 七层 slnx 隔离架构 | accepted | 2026-08-29 |
| [0002](0002-nativeaot-no-microsoft-ai-packages.md) | NativeAOT + 禁用微软 AI 包 | accepted | 2026-08-29 |
| [0003](0003-rebase-over-merge.md) | rebase 而非 merge | accepted | 2026-08-29 |
| [0004](0004-config-over-code-modalities.md) | 配置大于代码 — 模态能力显式注册 | accepted | 2026-08-29 |
| [0005](0005-file-driven-ui.md) | 文件驱动界面 | accepted | 2026-08-29 |
| [0006](0006-tdd-double-layer.md) | 双层 TDD | accepted | 2026-08-29 |
| [0007](0007-progressive-development.md) | 渐进式开发方法 | accepted | 2026-08-29 |
| [0008](0008-archive-to-xxx-not-delete.md) | .xxx 归档而非删除 | accepted | 2026-08-29 |
| [0009](0009-mcp-streamable-http.md) | MCP Streamable HTTP 2025-11-25 | accepted | 2026-08-29 |
| [0010](0010-global-usings.md) | GlobalUsings 统一管理 | accepted | 2026-08-29 |
| [0011](0011-data-container-aot-gc.md) | 数据容器 AOT+GC 选型 | accepted | 2026-08-29 |
| [0013](0013-hypergraph-vs-dag-separation.md) | 超图与 DAG 分工 | accepted | 2026-08-29 |
| [0014](0014-mcp-tool-coverage-principle.md) | MCP 工具覆盖原则 | accepted | 2026-08-29 |
| [0015](0015-config-hotreload-dual-variable.md) | 配置热重载双变量切换 | accepted | 2026-08-29 |
| [0016](0016-pass-interface-not-property.md) | 参数传接口不传属性 | accepted | 2026-08-29 |
| [0017](0017-inductive-refactor-no-abandon.md) | 归纳性重构不放弃 | accepted | 2026-08-29 |
| [0018](0018-loop-detector-state-machine.md) | 循环检测器状态机风格 | superseded by 0038 | 2026-08-29 |
| [0019](0019-enum-enumvalue-source-generator.md) | 枚举 + EnumValue + 源码生成器 | accepted | 2026-08-29 |
| [0020](0020-encapsulation-requirements.md) | 封装要求 | accepted | 2026-08-29 |
| [0021](0021-e2e-script-mode-inferred.md) | E2E 脚本 Mode 计算属性 | accepted | 2026-08-29 |
| [0022](0022-csharp-ast-cli-over-regex.md) | C# AST CLI 优先于正则 | accepted | 2026-08-29 |
| [0023](0023-subtraction-over-addition.md) | 减法思维优先 | accepted | 2026-08-29 |
| [0024](0024-no-symptomatic-fix-chain.md) | 治标不治本禁令 | accepted | 2026-08-29 |
| [0026](0026-pr-two-stage-pipeline.md) | PR 两段式流水线验证 | accepted | 2026-08-29 |
| [0027](0027-treat-warnings-as-errors.md) | TreatWarningsAsErrors 零警告容忍 | accepted | 2026-08-29 |
| [0028](0028-invariant-globalization.md) | InvariantGlobalization 渐进式双语策略 | accepted | 2026-08-29 |
| [0029](0029-analyzer-rules-jcc5002-jcc9006.md) | 分析器铁律 JCC5002/JCC9006 | accepted | 2026-08-29 |
| [0030](0030-e2e-real-service-strategy.md) | E2E 真实服务策略 | accepted | 2026-08-29 |
| [0031](0031-http-connection-pool-dns.md) | HTTP 连接池 DNS 刷新 | accepted | 2026-08-29 |
| [0032](0032-computeruse-win32-pinvoke.md) | ComputerUse P0 纯 Win32 P/Invoke | accepted | 2026-08-29 |
| [0033](0033-transport-fallback-chain-priority.md) | 传输层 Fallback 链优先级 | accepted | 2026-08-29 |
| [0034](0034-command-interception-layered.md) | 命令拦截分层 Guard+Interceptor+Dispatcher | superseded by 0039 | 2026-08-29 |
| [0035](0035-tool-progressive-exposure.md) | 工具渐进式暴露 | accepted | 2026-08-29 |
| [0036](0036-defense-in-depth-l1-l10.md) | 纵深防御 L1-L10 | accepted | 2026-08-29 |
| [0037](0037-redirect-soft-guidance.md) | Redirect 软引导而非硬转交 | accepted | 2026-08-29 |
| [0038](0038-state-machine-flags-guard.md) | 状态机 + 守卫 + [Flags] 位标志 | accepted | 2026-08-29 |
| [0039](0039-command-interception-state-machine.md) | 命令拦截全状态机 + 守卫 + [Flags] | accepted | 2026-08-29 |
| [0040](0040-enterprise-fsm-framework.md) | 企业级状态机框架 — 转换表 + 守卫 + 共享上下文 | accepted | 2026-08-29 |
| [0041](0041-fsm-source-generator.md) | Fsm 源码生成器 + 特性 + 事件订阅 | accepted | 2026-08-29 |
| [0042](0042-json-relaxed-serializer-unification.md) | JSON 序列化统一收口 — RelaxedJsonSerializer 单一入口 | accepted | 2026-08-30 |
| [0043](0043-unified-cleanup-functions.md) | 收口函数统一 — 命名/参数/异常/幂等性 | accepted | 2026-08-29 |
| [0044](0044-error-code-convention.md) | 错误码统一规范 — [PREFIX+数字] 格式 | accepted | 2026-08-29 |
| [0045](0045-configureawait-false.md) | ConfigureAwait(false) 强制规范 | accepted | 2026-08-29 |
| [0046](0046-register-di-pattern.md) | [Register] 特性 DI 自动注册模式 | accepted | 2026-08-29 |
| [0047](0047-unified-danger-level-classification.md) | 统一危险指令分级系统 | accepted | 2026-08-30 |
| [0048](0048-subagent-concurrency-unified-config.md) | 子代理并发控制统一配置入口 | accepted | 2026-09-02 |
| [0050](0050-spawn-stage-concurrency-limit.md) | spawn 阶段 SemaphoreSlim 限流 | accepted | 2026-09-02 |
| [0051](0051-fork-concurrency-limit.md) | Fork 并发上限 | accepted | 2026-09-02 |
| [0052](0052-asynclock-unified-mutex-file-access.md) | AsyncLock 统一互斥锁 + 文件读写可剥离架构 | accepted | 2026-09-02 |
| [0053](0053-context-compaction-layered-mechanism.md) | 上下文压缩分层机制 | accepted | 2026-09-02 |
| [0054](0054-llm-output-loop-detection-intervention.md) | LLM 输出循环检测与分级干预机制 | accepted | 2026-09-02 |
| [0056](0056-cache-break-detection-enhancement.md) | 缓存破坏检测维度补齐 — 双阈值 + TTL 区分 + 多 agent 隔离 | accepted | 2026-09-02 |
| [0057](0057-ts-p0-gap-alignment-lsp-analytics.md) | TS 原版 P0 缺口补齐 — LSP 集成 + Analytics 分析 | accepted | 2026-09-02 |
| [0058](0058-ts-p1-gap-alignment-proactive-vim-permission-skills.md) | TS 原版 P1 缺口补齐 — Proactive + Vim + Permission LLM + Skills | accepted | 2026-09-02 |
| [0059](0059-asynclock-reentrancy-detection.md) | AsyncLock 同步重入检测 — LockReentrancyException 提早暴露死锁 | superseded by 0060 | 2026-09-03 |
| [0060](0060-asynclock-sync-trylock-fireandforget-deadlock.md) | AsyncLock 同步 TryLock + StreamingToolExecutor 死锁排查 | accepted | 2026-09-04 |
| [0061](0061-shell-timeout-keyword-auto-capture.md) | 脚本超时关键字自动捕获机制 | accepted | 2026-09-04 |
| [0062](0062-path-existence-precheck-and-garbled-detection.md) | 路径存在性前置检查与乱码检测 | accepted | 2026-09-04 |
| [0063](0063-unified-external-endpoints.md) | 统一对外暴露地址 | accepted | 2026-09-05 |
| [0064](0064-pluggable-update-source-auto-update.md) | 可插拔更新源与自动更新 | accepted | 2026-09-05 |
| [0065](0065-jcc-mcp-subcommand.md) | jcc mcp CLI 子命令 — bash 直调内部 MCP 工具 | superseded by 0069 | 2026-09-05 |
| [0066](0066-prefix-exclamation-command.md) | 前置感叹号命令（! 触发 AI / !! 不触发 AI） | accepted | 2026-09-05 |
| [0067](0067-ci-log-structured-drill-down.md) | CI 日志结构化逐级展开（Section 级 drill down） | accepted | 2026-09-06 |
| [0068](0068-unified-persistence-pipeline-actor.md) | 统一持久化管道（Actor 模型） | accepted | 2026-09-06 |
| [0069](0069-cli-args-full-refactor.md) | 启动参数完全重构 — 扁平元动词 + 统一解析框架 + 斜杠命令直调 | accepted | 2026-09-06 |
| [0070](0070-rg-engine-mmap-plinq.md) | jcc rg 内置 ripgrep 兼容搜索 — RgEngine 独立实现（mmap + PLINQ + 零 GC） | accepted | 2026-09-06 |
| [0071](0071-editfileasync-per-file-asynclock.md) | IFileSystem.EditFileAsync 原子编辑接口 — per-file AsyncLock 串行化 | accepted | 2026-09-07 |
| [0072](0072-mmap-plinq-span-proliferation.md) | mmap + PLINQ + 零 GC Span 技术推广 — 从 RgEngine 到全项目文件遍历 | accepted | 2026-09-07 |
| [0073](0073-gh-rest-api-direct-call.md) | gh_* MCP 工具重写为 GitHub REST API 直调（摆脱系统 gh 依赖） | accepted | 2026-09-07 |
| [0074](0074-actor-supervisor-tree.md) | Actor 监督树 — Router/Gateway/Supervisor/PersistentMailbox 四层扩展 | accepted | 2026-09-08 |
| [0075](0075-gh-cli-troubleshooting-guide.md) | gh CLI 排错避坑指南 | accepted | 2026-09-08 |
| [0076](0076-dotnet-test-build-output-rules.md) | .NET 测试和构建输出禁令与 CLI 运行时测试 | accepted | 2026-09-08 |
| [0077](0077-mockserver-jcc-joint-testing.md) | MockServer + jcc 联合测试 | accepted | 2026-09-08 |
| [0078](0078-merge-e2e-synonym-rules.md) | 合并与 E2E 同义词规则 | accepted | 2026-09-08 |
| [0079](0079-anti-pattern-examples.md) | 反例清单（踩过的坑，禁止再犯） | accepted | 2026-09-08 |
| [0080](0080-manual-exe-testing-guide.md) | 手动测试 exe 功能与推荐配置 | accepted | 2026-09-08 |
| [0081](0081-seven-layer-build-strategy.md) | 七层解决方案架构与编译策略 | accepted | 2026-09-08 |
| [0082](0082-gui-async-test-avalonia.md) | GUI 异步 UI 测试与启动 exe 测试 | accepted | 2026-09-08 |
| [0083](0083-e2e-script-mode-spec.md) | E2E 测试脚本模式规范 | accepted | 2026-09-08 |
| [0084](0084-platform-windows-env-rules.md) | 平台专属操作禁令与 Windows 命令行环境 | accepted | 2026-09-08 |
| [0085](0085-data-container-selection-spec.md) | 数据容器选型规范 | accepted | 2026-09-08 |
| [0086](0086-core-tech-selection-lock-design.md) | 核心技术选型与锁设计 | accepted | 2026-09-08 |
| [0087](0087-batch-replace-csharp-source-rules.md) | 批量替换 C# 源码禁令与导向 | accepted | 2026-09-08 |
| [0088](0088-test-execution-rules.md) | 测试执行规则 | accepted | 2026-09-08 |
| [0089](0089-jcc-builtin-tools-only-no-system-gh-rg.md) | jcc 自带工具统一入口 — 禁止系统/宿主环境的 gh / rg | accepted | 2026-09-08 |
| [0090](0090-jcc-gh-cli-subcommand.md) | `jcc gh` CLI 子命令 — 扁平元动词 + schema 驱动参数绑定 | accepted | 2026-09-08 |
| [0091](0091-actor-duplex-inplace-upgrade.md) | Actor 全双工改造 — 直接改 ActorBase（无后向兼容） | accepted | 2026-09-08 |
| [0092](0092-worktree-path-inconsistency-fix.md) | Worktree 路径一致性 — 中间件幂等 + Guard 路径锁定 | accepted | 2026-09-08 |
| [0093](0093-resource-management-exception-style.md) | 资源管理与异常控制风格规范 | accepted | 2026-09-09 |
| [0094](0094-github-verbose-output.md) | GitHub 工具精简输出 + verbose 完整模式 | proposed | 2026-09-09 |
| [0095](0095-unified-path-normalizer.md) | 统一路径归一化工具 PathNormalizer | accepted | 2026-09-09 |
| [0096](0096-shell-path-error-auto-retry.md) | Shell 路径处理策略 — 去掉执行前自动转换 + 执行后失败重试 | accepted | 2026-09-09 |
| [0097](0097-workflow-checkpoint-resume.md) | Workflow 级断点续跑持久化策略 | accepted | 2026-09-09 |
| [0098](0098-plugin-system-fusion-actor-effectscope.md) | 插件系统融合 — Actor+EffectScope+动态拓扑+弱事件+ALC | accepted | 2026-09-10 |

## 主题索引（按议题）

> 同一 ADR 可同时归属多个议题时，仅列在主归属组；交叉引用见 AGENTS.md。

### 架构基础

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0001](0001-seven-layer-slnx-isolation.md) | 七层 slnx 隔离架构 | accepted | 2026-08-29 |
| [0002](0002-nativeaot-no-microsoft-ai-packages.md) | NativeAOT + 禁用微软 AI 包 | accepted | 2026-08-29 |
| [0003](0003-rebase-over-merge.md) | rebase 而非 merge | accepted | 2026-08-29 |
| [0004](0004-config-over-code-modalities.md) | 配置大于代码 — 模态能力显式注册 | accepted | 2026-08-29 |
| [0005](0005-file-driven-ui.md) | 文件驱动界面 | accepted | 2026-08-29 |
| [0006](0006-tdd-double-layer.md) | 双层 TDD | accepted | 2026-08-29 |
| [0007](0007-progressive-development.md) | 渐进式开发方法 | accepted | 2026-08-29 |
| [0008](0008-archive-to-xxx-not-delete.md) | .xxx 归档而非删除 | accepted | 2026-08-29 |
| [0009](0009-mcp-streamable-http.md) | MCP Streamable HTTP 2025-11-25 | accepted | 2026-08-29 |
| [0010](0010-global-usings.md) | GlobalUsings 统一管理 | accepted | 2026-08-29 |
| [0011](0011-data-container-aot-gc.md) | 数据容器 AOT+GC 选型 | accepted | 2026-08-29 |
| [0081](0081-seven-layer-build-strategy.md) | 七层解决方案架构与编译策略 | accepted | 2026-09-08 |
| [0098](0098-plugin-system-fusion-actor-effectscope.md) | 插件系统融合 — Actor+EffectScope+动态拓扑+弱事件+ALC | accepted | 2026-09-10 |

### 组件策略与方法论

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0013](0013-hypergraph-vs-dag-separation.md) | 超图与 DAG 分工 | accepted | 2026-08-29 |
| [0014](0014-mcp-tool-coverage-principle.md) | MCP 工具覆盖原则 | accepted | 2026-08-29 |
| [0015](0015-config-hotreload-dual-variable.md) | 配置热重载双变量切换 | accepted | 2026-08-29 |
| [0016](0016-pass-interface-not-property.md) | 参数传接口不传属性 | accepted | 2026-08-29 |
| [0017](0017-inductive-refactor-no-abandon.md) | 归纳性重构不放弃 | accepted | 2026-08-29 |
| [0018](0018-loop-detector-state-machine.md) | 循环检测器状态机风格 | superseded by 0038 | 2026-08-29 |
| [0019](0019-enum-enumvalue-source-generator.md) | 枚举 + EnumValue + 源码生成器 | accepted | 2026-08-29 |
| [0020](0020-encapsulation-requirements.md) | 封装要求 | accepted | 2026-08-29 |
| [0021](0021-e2e-script-mode-inferred.md) | E2E 脚本 Mode 计算属性 | accepted | 2026-08-29 |
| [0022](0022-csharp-ast-cli-over-regex.md) | C# AST CLI 优先于正则 | accepted | 2026-08-29 |
| [0023](0023-subtraction-over-addition.md) | 减法思维优先 | accepted | 2026-08-29 |
| [0024](0024-no-symptomatic-fix-chain.md) | 治标不治本禁令 | accepted | 2026-08-29 |
| [0079](0079-anti-pattern-examples.md) | 反例清单（踩过的坑，禁止再犯） | accepted | 2026-09-08 |
| [0086](0086-core-tech-selection-lock-design.md) | 核心技术选型与锁设计 | accepted | 2026-09-08 |
| [0087](0087-batch-replace-csharp-source-rules.md) | 批量替换 C# 源码禁令与导向 | accepted | 2026-09-08 |

### 工程实践 / CI / 测试

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0026](0026-pr-two-stage-pipeline.md) | PR 两段式流水线验证 | accepted | 2026-08-29 |
| [0027](0027-treat-warnings-as-errors.md) | TreatWarningsAsErrors 零警告容忍 | accepted | 2026-08-29 |
| [0028](0028-invariant-globalization.md) | InvariantGlobalization 渐进式双语策略 | accepted | 2026-08-29 |
| [0029](0029-analyzer-rules-jcc5002-jcc9006.md) | 分析器铁律 JCC5002/JCC9006 | accepted | 2026-08-29 |
| [0030](0030-e2e-real-service-strategy.md) | E2E 真实服务策略 | accepted | 2026-08-29 |
| [0031](0031-http-connection-pool-dns.md) | HTTP 连接池 DNS 刷新 | accepted | 2026-08-29 |
| [0076](0076-dotnet-test-build-output-rules.md) | .NET 测试和构建输出禁令与 CLI 运行时测试 | accepted | 2026-09-08 |
| [0077](0077-mockserver-jcc-joint-testing.md) | MockServer + jcc 联合测试 | accepted | 2026-09-08 |
| [0078](0078-merge-e2e-synonym-rules.md) | 合并与 E2E 同义词规则 | accepted | 2026-09-08 |
| [0080](0080-manual-exe-testing-guide.md) | 手动测试 exe 功能与推荐配置 | accepted | 2026-09-08 |
| [0082](0082-gui-async-test-avalonia.md) | GUI 异步 UI 测试与启动 exe 测试 | accepted | 2026-09-08 |
| [0083](0083-e2e-script-mode-spec.md) | E2E 测试脚本模式规范 | accepted | 2026-09-08 |
| [0088](0088-test-execution-rules.md) | 测试执行规则 | accepted | 2026-09-08 |

### 平台与传输层

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0032](0032-computeruse-win32-pinvoke.md) | ComputerUse P0 纯 Win32 P/Invoke | accepted | 2026-08-29 |
| [0033](0033-transport-fallback-chain-priority.md) | 传输层 Fallback 链优先级 | accepted | 2026-08-29 |
| [0034](0034-command-interception-layered.md) | 命令拦截分层 Guard+Interceptor+Dispatcher | superseded by 0039 | 2026-08-29 |
| [0035](0035-tool-progressive-exposure.md) | 工具渐进式暴露 | accepted | 2026-08-29 |
| [0036](0036-defense-in-depth-l1-l10.md) | 纵深防御 L1-L10 | accepted | 2026-08-29 |
| [0037](0037-redirect-soft-guidance.md) | Redirect 软引导而非硬转交 | accepted | 2026-08-29 |
| [0084](0084-platform-windows-env-rules.md) | 平台专属操作禁令与 Windows 命令行环境 | accepted | 2026-09-08 |

### 状态机 / 错误码 / 序列化

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0038](0038-state-machine-flags-guard.md) | 状态机 + 守卫 + [Flags] 位标志 | accepted | 2026-08-29 |
| [0039](0039-command-interception-state-machine.md) | 命令拦截全状态机 + 守卫 + [Flags] | accepted | 2026-08-29 |
| [0040](0040-enterprise-fsm-framework.md) | 企业级状态机框架 — 转换表 + 守卫 + 共享上下文 | accepted | 2026-08-29 |
| [0041](0041-fsm-source-generator.md) | Fsm 源码生成器 + 特性 + 事件订阅 | accepted | 2026-08-29 |
| [0042](0042-json-relaxed-serializer-unification.md) | JSON 序列化统一收口 — RelaxedJsonSerializer 单一入口 | accepted | 2026-08-30 |
| [0043](0043-unified-cleanup-functions.md) | 收口函数统一 — 命名/参数/异常/幂等性 | accepted | 2026-08-29 |
| [0044](0044-error-code-convention.md) | 错误码统一规范 — [PREFIX+数字] 格式 | accepted | 2026-08-29 |
| [0045](0045-configureawait-false.md) | ConfigureAwait(false) 强制规范 | accepted | 2026-08-29 |
| [0046](0046-register-di-pattern.md) | [Register] 特性 DI 自动注册模式 | accepted | 2026-08-29 |

### 安全与并发治理

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0047](0047-unified-danger-level-classification.md) | 统一危险指令分级系统 | accepted | 2026-08-30 |
| [0048](0048-subagent-concurrency-unified-config.md) | 子代理并发控制统一配置入口 | accepted | 2026-09-02 |
| [0050](0050-spawn-stage-concurrency-limit.md) | spawn 阶段 SemaphoreSlim 限流 | accepted | 2026-09-02 |
| [0051](0051-fork-concurrency-limit.md) | Fork 并发上限 | accepted | 2026-09-02 |
| [0052](0052-asynclock-unified-mutex-file-access.md) | AsyncLock 统一互斥锁 + 文件读写可剥离架构 | accepted | 2026-09-02 |
| [0059](0059-asynclock-reentrancy-detection.md) | AsyncLock 同步重入检测 — LockReentrancyException 提早暴露死锁 | superseded by 0060 | 2026-09-03 |
| [0060](0060-asynclock-sync-trylock-fireandforget-deadlock.md) | AsyncLock 同步 TryLock + StreamingToolExecutor 死锁排查 | accepted | 2026-09-04 |

### 上下文与系统提示词

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0053](0053-context-compaction-layered-mechanism.md) | 上下文压缩分层机制 | accepted | 2026-09-02 |
| [0054](0054-llm-output-loop-detection-intervention.md) | LLM 输出循环检测与分级干预机制 | accepted | 2026-09-02 |
| [0056](0056-cache-break-detection-enhancement.md) | 缓存破坏检测维度补齐 — 双阈值 + TTL 区分 + 多 agent 隔离 | accepted | 2026-09-02 |

### TS 缺口补齐

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0057](0057-ts-p0-gap-alignment-lsp-analytics.md) | TS 原版 P0 缺口补齐 — LSP 集成 + Analytics 分析 | accepted | 2026-09-02 |
| [0058](0058-ts-p1-gap-alignment-proactive-vim-permission-skills.md) | TS 原版 P1 缺口补齐 — Proactive + Vim + Permission LLM + Skills | accepted | 2026-09-02 |

### 脚本与路径校验

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0061](0061-shell-timeout-keyword-auto-capture.md) | 脚本超时关键字自动捕获机制 | accepted | 2026-09-04 |
| [0062](0062-path-existence-precheck-and-garbled-detection.md) | 路径存在性前置检查与乱码检测 | accepted | 2026-09-04 |
| [0095](0095-unified-path-normalizer.md) | 统一路径归一化工具 PathNormalizer | accepted | 2026-09-09 |
| [0096](0096-shell-path-error-auto-retry.md) | Shell 路径处理策略 — 去掉执行前自动转换 + 执行后失败重试 | accepted | 2026-09-09 |

### 地址与自动更新

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0063](0063-unified-external-endpoints.md) | 统一对外暴露地址 | accepted | 2026-09-05 |
| [0064](0064-pluggable-update-source-auto-update.md) | 可插拔更新源与自动更新 | accepted | 2026-09-05 |

### CLI / jcc 启动参数

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0065](0065-jcc-mcp-subcommand.md) | jcc mcp CLI 子命令 — bash 直调内部 MCP 工具 | superseded by 0069 | 2026-09-05 |
| [0066](0066-prefix-exclamation-command.md) | 前置感叹号命令（! 触发 AI / !! 不触发 AI） | accepted | 2026-09-05 |
| [0069](0069-cli-args-full-refactor.md) | 启动参数完全重构 — 扁平元动词 + 统一解析框架 + 斜杠命令直调 | accepted | 2026-09-06 |
| [0070](0070-rg-engine-mmap-plinq.md) | jcc rg 内置 ripgrep 兼容搜索 — RgEngine 独立实现（mmap + PLINQ + 零 GC） | accepted | 2026-09-06 |
| [0089](0089-jcc-builtin-tools-only-no-system-gh-rg.md) | jcc 自带工具统一入口 — 禁止系统/宿主环境的 gh / rg | accepted | 2026-09-08 |
| [0090](0090-jcc-gh-cli-subcommand.md) | `jcc gh` CLI 子命令 — 扁平元动词 + schema 驱动参数绑定 | accepted | 2026-09-08 |

### 持久化 / CI 日志 / Actor

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0067](0067-ci-log-structured-drill-down.md) | CI 日志结构化逐级展开（Section 级 drill down） | accepted | 2026-09-06 |
| [0068](0068-unified-persistence-pipeline-actor.md) | 统一持久化管道（Actor 模型） | accepted | 2026-09-06 |
| [0074](0074-actor-supervisor-tree.md) | Actor 监督树 — Router/Gateway/Supervisor/PersistentMailbox 四层扩展 | accepted | 2026-09-08 |

### 文件 I/O 与 Span 优化

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0071](0071-editfileasync-per-file-asynclock.md) | IFileSystem.EditFileAsync 原子编辑接口 — per-file AsyncLock 串行化 | accepted | 2026-09-07 |
| [0072](0072-mmap-plinq-span-proliferation.md) | mmap + PLINQ + 零 GC Span 技术推广 — 从 RgEngine 到全项目文件遍历 | accepted | 2026-09-07 |

### GitHub 工具链

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0073](0073-gh-rest-api-direct-call.md) | gh_* MCP 工具重写为 GitHub REST API 直调（摆脱系统 gh 依赖） | accepted | 2026-09-07 |
| [0075](0075-gh-cli-troubleshooting-guide.md) | gh CLI 排错避坑指南 | accepted | 2026-09-08 |
| [0094](0094-github-verbose-output.md) | GitHub 工具精简输出 + verbose 完整模式 | proposed | 2026-09-09 |

### 数据与核心选型

| 编号 | 标题 | 状态 | 日期 |
|------|------|------|------|
| [0085](0085-data-container-selection-spec.md) | 数据容器选型规范 | accepted | 2026-09-08 |

## 取代链（历史追溯）

| 旧 ADR | 状态 | 取代者 |
|--------|------|--------|
| [0018](0018-loop-detector-state-machine.md) | 循环检测器状态机风格 | [0038](0038-state-machine-flags-guard.md) |
| [0034](0034-command-interception-layered.md) | 命令拦截分层 Guard+Interceptor+Dispatcher | [0039](0039-command-interception-state-machine.md) |
| [0059](0059-asynclock-reentrancy-detection.md) | AsyncLock 同步重入检测 — LockReentrancyException 提早暴露死锁 | [0060](0060-asynclock-sync-trylock-fireandforget-deadlock.md) |
| [0065](0065-jcc-mcp-subcommand.md) | jcc mcp CLI 子命令 — bash 直调内部 MCP 工具 | [0069](0069-cli-args-full-refactor.md) |

## 反例与工程约束（高频查阅）

| 编号 | 主题 | 一句话约束 |
|------|------|-----------|
| [0008](0008-archive-to-xxx-not-delete.md) | 删除 vs 归档 | 禁止删除文件，移至 `.xxx/{name}.{时间戳}.del` |
| [0023](0023-subtraction-over-addition.md) | 减法思维 | 新增前先问能否删除/收敛已有接口 |
| [0024](0024-no-symptomatic-fix-chain.md) | 治标不治本 | 禁止打补丁链，必须改根因 |
| [0026](0026-pr-two-stage-pipeline.md) | PR 验证 | 创建 PR 必须开 auto-merge，CI 通过自动合并后 main 再跑 CI 二次验证 |
| [0027](0027-treat-warnings-as-errors.md) | 零警告 | TreatWarningsAsErrors 全开，分析器铁律 JCC5002/JCC9006 |
| [0028](0028-invariant-globalization.md) | 区域文化 | InvariantGlobalization 渐进式，先英文再中文 |
| [0030](0030-e2e-real-service-strategy.md) | E2E 真实 | E2E 优先用真实服务 + MockServer+jcc 联合 |
| [0043](0043-unified-cleanup-functions.md) | 收口函数 | 命名/参数/异常/幂等性四统一 |
| [0044](0044-error-code-convention.md) | 错误码 | [PREFIX+数字] 格式，向参数错误用 Rust 风格报错 |
| [0061](0061-shell-timeout-keyword-auto-capture.md) | 脚本超时 | wait/sleep 等关键字被自动加超时 |
| [0062](0062-path-existence-precheck-and-garbled-detection.md) | 路径校验 | 外部传入路径必须前置存在性 + 乱码检测 |
| [0076](0076-dotnet-test-build-output-rules.md) | 构建/测试输出 | 禁止散落 .csproj 同级输出，统一到 `artifacts/bin|obj/` |
| [0079](0079-anti-pattern-examples.md) | 反例清单 | 踩过的坑禁止再犯 |
| [0083](0083-e2e-script-mode-spec.md) | E2E 脚本 | E2E 脚本模式 Mode 必须由输入推导 |
| [0084](0084-platform-windows-env-rules.md) | 平台操作禁令 | 禁用会卡交互的命令、PowerShell 严禁 HEREDOC |
| [0087](0087-batch-replace-csharp-source-rules.md) | 批量替换 | 必须先在单文件验证 → 才能推广到全部位置 |
| [0088](0088-test-execution-rules.md) | 测试执行 | 子智能体禁止全量测试，编译+冒烟后由主智能体执行 |
| [0089](0089-jcc-builtin-tools-only-no-system-gh-rg.md) | jcc 工具统一入口 | ⛔ 禁止系统/宿主 gh/rg，统一用 `jcc rg` / `jcc mcp_call gh_*` / `jcc gh` |
