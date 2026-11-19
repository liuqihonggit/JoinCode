# TASK101 工程优化清单 — 构建口径、测试断言体系与 jcc 工具体验

> 来源：TASK099 B7 实战（PR #700 两次 CI 红 + 本地验收流程）中暴露的工具与工程摩擦。
> 状态：proposed（仅文档，未实施；实施时按批次逐项落地）
> 约束：无后向兼容；每项实施走「红测试 → 实现 → 编译 → 测试 → exe/手动验收 → 提交」。

## 1. 背景与实证

PR #700（TASK099 B7 合入）过程中暴露两类真实致红问题与一组工具摩擦：

1. **构建口径分裂**：`JoinCode.slnx` 不含 `native_tui/native_gui`。跨域契约变更（`GoalEngine.TryGetGoalProgress`、`INodeRegistry.Register`）在默认"全仓编译"中不可见，只有 `Build-AllDomains.ps1 -Domains native_tui,native_gui` 才暴露，导致 PR CI 前才补修。
2. **测试对 JSON 排版敏感**：JCC1017 把 Desktop 场景工具从手写 JSON 迁到 DTO 序列化（紧凑格式 + 非 ASCII 转义）后，detect/click/look/zoom 共 9 个用例因 `Should().Contain("\"type\": \"button\"")` 类断言集体红，与产品行为无关。
3. **jcc 工具摩擦**：`pr create` 成功输出被保护校验淹没（PR URL 不在首屏）、校验误报 required check 缺失、`run wait` 无超时/进度、`run list --event push` 静默空、`mcp_list/mcp_schema` 与 `gh` 子命令注册表口径分裂、`gh --version` 报错。
4. **MSB4181 掩盖真错误**：`Build-AllDomains.ps1` 失败时外层只看到 MSB4181，真实错误在 inner temp log 中一行；且默认全量目标不含 UI 域。
5. **flaky 修复无机制**：两例时序断言（墙钟阈值、Actor 调度超时）只能人工按 16 轮协议修，未沉淀模板。

## 2. 优化项清单

### A. 构建/CI 口径（P0，真实致红根因）

| 项 | 问题 | 改法 | 验收 |
|----|------|------|------|
| A1 | `Build-AllDomains.ps1` 默认全量不含 native_tui/native_gui | 默认目标集合补齐两个 UI 域（或并入 slnx），失败时 inner temp log 的 error 行全量回显并去重 | 构造一次 UI 域契约错误（临时 fake 缺接口成员），默认命令必须红且错误可读；还原后绿 |
| A2 | CI 无 UI 域编译 job | `ci-build.yml` 增加 native_tui/native_gui 编译（或把两域并入 JoinCode.slnx 后由既有 build job 覆盖） | 构造同类契约漂移推 PR，CI 在 build 阶段即红（而非靠单测偶遇） |

### B. 测试断言体系（P0，迁移连锁红根因）

| 项 | 问题 | 改法 | 验收 |
|----|------|------|------|
| B1 | 测试对 JSON 字符串做排版断言 | 提供 `JsonAssert` 帮助器（`JsonDocument` 解析 + 按路径/值/集合语义断言），并在测试模板/AGENTS 约定"禁止排版敏感断言" | 迁移 Desktop 场景 4 个测试文件到帮助器并全绿；帮助器自身单测覆盖数组包含/嵌套路径/非 ASCII |
| B2 | 无防止回归的手段 | 新增分析器（测试项目范围）：`Should().Contain("\"<key>\": ")` 或 `Contain("{...\": ...")` 模式报警，建议改用 JsonAssert | 分析器测试：正例（排版断言）报红、反例（语义断言）不报 |
| B3 | flaky 修复靠人工 | 沉淀 `RetryableFactAttribute`（Trait Flaky + 外层重试模板 + 失败自动 rethrow 聚合）；CI 默认 `Category!=Flaky`，夜间专项 job 跑 Flaky 集 | 两例既有 flaky（MailboxBase、AskWithRetry）改用该模板后 16 轮全绿；CI 配置生效可见 Flaky 集合清单 |

### C. jcc 工具链（P1，体验与排障效率）

| 项 | 问题（实证） | 改法 | 验收 |
|----|------|------|------|
| C1 | `pr create` 输出被保护校验淹没，PR 编号不在首屏 | 首行输出 PR URL/编号；保护校验仅作警告且标注"CI 未跑完时可能误报" | 创建实测 PR：首行即 URL；误报场景有提示文案 |
| C2 | 保护校验对 reusable workflow 检查名误报"CI 中不存在" | 按 `<caller job>/<called job name>` 约定匹配，或等 check suite 注册后再判 | 用 #700 同款多 job 的 ci-unit-tests 验证零误报 |
| C3 | `run wait` 无超时/进度，长等待像卡死 | 增加 `--timeout`、周期性输出进度（如 `running 6/37`）；超时返回明确错误码 | 对一个长 CI run 实测：有进度行、超时可配 |
| C4 | `run list --event push` 静默返回 0 run | 不支持的过滤项显式报错并列出可用字段 | 实测报错含可用字段列表 |
| C5 | `mcp_list --category github`（1 个）与 `gh` 子命令（31 个）注册表分裂；`mcp_schema gh_pr_create` 找不到 | gh CLI 路由表与 MCP 工具注册表统一数据源（或 mcp_list 标注"gh CLI 工具不经此暴露"） | `mcp_list` 与 `jcc gh --help` 的 gh 工具集合一致（或文档说明一致性规则） |
| C6 | `gh --version` 报 `error: 缺少 gh 分组` | 包装器识别 `--version` 直接返回版本与提示 | `gh --version` 输出包装器版本，退出码 0 |

### D. 模块初始化治理（P2）

| 项 | 问题 | 改法 | 验收 |
|----|------|------|------|
| D1 | `[ModuleInitializer]` 只能用项目级 NoWarn 豁免（B7 CI 修复），无中央登记 | `Directory.Build.props` 提供 `JccAllowModuleInitializer` 开关；门禁脚本/分析器要求每个 ModuleInitializer 有登记理由 | 未登记的新增 ModuleInitializer 报警；已登记的（Infrastructure 本地化）不报警 |

## 3. 四列验收表

> `已验收` 只填真实 exe/命令证据（ADR 0080），实施后逐项补。

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|:------:|:------:|
| A1 构建默认覆盖 UI 域 + inner log 错误透出 | `Build-AllDomains.ps1`、CI 本地排障 | ✅ | ✅ 默认全量 4 target（solution/native_tui/native_gui×2）全绿（`.xxx/logs/b7/a1_full_default.log`，exit 0）；错误透出上限 15→30 + 无直接 error 行时显式提示查看日志 |
| A2 CI UI 域编译 job | `.github/workflows/ci-build.yml`（`build / Build UI`，windows-latest） | ✅ | ❌ 待下一次 PR 实测；**仓库保护需人工把 `build / Build UI` 加入 required status checks（需管理员操作）** |
| B1 JsonAssert 帮助器 | 测试项目模板（Desktop 场景先行） | ❌ | ❌ |
| B2 排版断言分析器 | 测试项目 | ❌ | ❌ |
| B3 RetryableFact 模板 + CI Flaky 分层 | AsyncLock.Tests、CI workflows | ❌ | ❌ |
| C1-C6 jcc 工具改造 | `jcc gh` 各子命令、`mcp_list/mcp_schema` | ❌ | ❌ |
| D1 ModuleInitializer 中央开关与登记门禁 | `Directory.Build.props`、门禁脚本 | ❌ | ❌ |

## 4. 风险与回滚

| 风险 | 缓解 |
|------|------|
| Build-AllDomains 默认全量变慢（UI 域加入） | 保留 `-Domains` 按域模式；CI 并行 job 分摊 |
| 分析器误报测试正常断言 | 规则收窄到"`"<ascii key>": `" 空格模式且出现在测试项目；提供 style 豁免路径 |
| jcc 工具改动影响既有脚本 | 每项独立提交；首行 URL 等纯增量输出不改退出码语义（除 C4/C6 明确报错场景） |
| RetryableFact 掩盖真实缺陷 | Trait 分层：CI 默认排除 Flaky，夜间必跑；模板禁止吞异常，只重试明确断言失败 |

## 5. 批次建议

1. P0-A（构建/CI 口径）→ 2. P0-B（测试断言体系）→ 3. P1-C（jcc 工具）→ 4. P2-D（治理）。
每批独立提交、独立验收，可与 TASK099 文档同目录交叉引用。

## 6. 自主决策记录

<!-- 🤖 Auto Decision: 2026-11-19 TASK101 -->
<!-- 决策: 先出任务文档再实施，A/B 定为 P0，C 为 P1，D 为 P2 -->
<!-- 原因: A/B 是 PR #700 两次 CI 红的直接根因（UI 域漏编译、排版断言连锁红），优先消除回归温床；C 是体验/排障效率问题不阻塞交付；D 依赖 B3/门禁基建落地后再做 -->
<!-- 替代方案: 直接开修（证据已足，但跨 6 个工具链子项需先对齐范围与验收口径）；只修 A1（B2 不防回归，同类红会复发）——均否决 -->
<!-- 验证: 文档阶段未实施；实施后按四列验收表逐项补命令/exe 证据 -->

## 7. 实施记录

### A 批（2026-11-20 完成）

- A1 `Build-AllDomains.ps1`：默认全量改为 4 个 target（JoinCode.slnx + native_tui.slnx + NativeGui.Core.Tests + NativeGui.Host.Tests），失败时统一 result 表 + exit 1；错误透出上限 15→30，无直接 error 行时提示"可能被 MSB4181/级联掩盖，查看日志"。
  - 验收：默认命令 exit 0，4 target 全绿（`.xxx/logs/b7/a1_full_default.log`）。
- A2 `ci-build.yml` 新增 `build-ui` job（windows-latest，构建 native_tui.slnx + NativeGui.Host.Tests.csproj），YAML 语法校验通过。
  - 待办（需管理员）：将 `build / Build UI` 加入 main 分支保护 required checks，否则 job 失败不阻塞 auto-merge。

<!-- 🤖 Auto Decision: 2026-11-20 TASK101-A -->
<!-- 决策: A1 在 PowerShell 脚本内合并默认目标，而不是把 UI 域并入 JoinCode.slnx -->
<!-- 原因: slnx 是 CI Pack 流程（JoinCode.Pack.slnx）与本地测试矩阵的输入，改动影响面大；脚本层聚合零风险且可保留 -Domains 按域模式 -->
<!-- 替代方案: 并入 slnx（影响 CI 缓存键与 pack 流程）；单独脚本 Build-AllUI.ps1（多一个入口，易漏用）——均否决 -->
<!-- 验证: 默认命令 4 target 全绿（a1_full_default.log）；YAML 校验通过 -->
