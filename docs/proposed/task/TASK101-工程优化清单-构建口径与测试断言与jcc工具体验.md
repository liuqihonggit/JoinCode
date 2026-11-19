# TASK101 工程优化清单 — 构建口径、测试断言体系与 jcc 工具体验

> 来源：TASK099 B7 实战（PR #700 两次 CI 红 + 本地验收流程）中暴露的工具与工程摩擦。
> 状态：implemented（A/B/C/D 四批已落地并逐批验收；A2 按用户指令回退为定期 CI；C2 暴露的 2 个真实缺失 check 已同步）
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
| A2 CI UI 域编译 job | `.github/workflows/ci-build.yml`（`build / Build UI`，windows-latest） | ✅（已按用户指令回退 PR 化） | ✅ 曾随 A 批合入并在分支保护登记；**2026-11-20 用户指令：PR 必跑的 UI 构建成本过高，已移除 `build-ui` job，UI 编译验证移到 `ci-gui-daily.yml`（每 2 天，windows-latest，含 native_tui 编译 + native_gui 测试）**；分支保护已同步移除 `build / Build UI` |
| B1 JsonAssert 帮助器 | 测试项目模板（Desktop 场景先行） | ✅ | ✅ `test/unit/testing.common/asserts/JsonAssert.cs` 语义断言（Parse/Get 路径/字符串/布尔/整数/数组包含）；自测 `JsonAssertTests` 8/8；Desktop 场景 3 文件 9 测试迁移后 30/30 绿（`--filter FullyQualifiedName~DesktopScene`） |
| B2 排版断言分析器 | 测试项目 | ✅ | ✅ `JsonLayoutAssertionRule`（JCC9311，仅测试项目，Info 级）；分析器测试 5/5（正例 2 + 反例 3）；AotSafety.Tests 全量 267/267；真机 `dotnet build guard.config.tests -p:ErrorLog` SARIF 命中存量 9 处排版断言（level=note 不破坏 TreatWarningsAsErrors）；jcc.exe `--selftest-tui` HOST SELFTEST OK |
| B3 RetryableFact 模板 + CI Flaky 分层 | AsyncLock.Tests、CI workflows | ✅ | ✅ `Testing.Common/retry/FlakyRetry.cs`（RunUntilAsync/RunAsync，耗尽聚合报错，取消直抛）；`FlakyRetryTests` 5/5；AskWithRetry 两例迁移后 12/12；`Category=Flaky` 15/15、默认过滤 541（556−15）；`ci-unit-tests.yml` 加 `Category!=Flaky`；新增 `ci-flaky-nightly.yml`（UTC 06:30 + workflow_dispatch，YAML 校验通过，首次夜跑待合并后观察）；jcc.exe `--selftest-tui` HOST SELFTEST OK |
| C1-C6 jcc 工具改造 | `jcc gh` 各子命令、`mcp_list/mcp_schema` | ✅ | ✅ C1 单测（URL 首行 ×2）；C2 exe 实测 `gh branch audit-protection`: 22 匹配 / 0 误报 stale / 检出 2 个真实缺失 check；C3 exe 实测 `run wait --timeout 25 --interval 2`: 每轮进度行 + 超时 exit 3；C4 exe 实测 0 结果带过滤提示与可用 event 值；C5 exe 实测 mcp_list meta.note + mcp_schema gh_* 引导；C6 exe 实测 `gh --version` exit 0。C1 未真实建 PR（可由随后用户的 PR 自然覆盖） |
| D1 ModuleInitializer 中央开关与登记门禁 | `Directory.Build.props`、门禁脚本 | ✅ | ✅ 根 NoWarn 全局接管 CA2255 + `CompilerVisibleProperty JccAllowModuleInitializer`；`ProjectContext.AllowModuleInitializer`；JCC9312（非测试未登记报 Warning=错，测试放行）；Infrastructure 登记 `JccAllowModuleInitializer=true`；18 份测试 AssemblyInfo 的 CA2255 pragma 全部移除。验收：分析器 270/270；真机负对照（`.xxx/jcc9312_probe` 未登记 → error JCC9312，登记后 0）；guard.config.tests / Infrastructure 0 错误；jcc.exe `--selftest-tui` HOST SELFTEST OK |

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
  - **已回退（2026-11-20 用户指令）**：PR 必跑 UI 构建成本过高；job 删除，UI 验证改由 `ci-gui-daily.yml`（每 2 天）覆盖（新增 native_tui 编译），分支保护同步移除 `build / Build UI`。

<!-- 🤖 Auto Decision: 2026-11-20 TASK101-A -->
<!-- 决策: A1 在 PowerShell 脚本内合并默认目标，而不是把 UI 域并入 JoinCode.slnx -->
<!-- 原因: slnx 是 CI Pack 流程（JoinCode.Pack.slnx）与本地测试矩阵的输入，改动影响面大；脚本层聚合零风险且可保留 -Domains 按域模式 -->
<!-- 替代方案: 并入 slnx（影响 CI 缓存键与 pack 流程）；单独脚本 Build-AllUI.ps1（多一个入口，易漏用）——均否决 -->
<!-- 验证: 默认命令 4 target 全绿（a1_full_default.log）；YAML 校验通过 -->

### B 批（2026-11-20 完成）

- B1 `Testing.Common/asserts/JsonAssert.cs`：零依赖语义 JSON 断言（`Parse` 带片段报错；`Get` 支持 `a.b[0].c` 点路径 + 索引；`AssertString/AssertBool/AssertInt`；`AssertArrayContains/AssertAnyStringContains`）。`Testing.Common/GlobalUsings.cs` 增加 `System.Text.Json`。
  - 迁移：`DesktopSceneDetectClickTests`（5）、`DesktopSceneZoomToolHandlersTests`（2）、`DesktopSceneLookToolHandlersTests`（2）全部改为路径/值断言，移除 `SuppressMessage`。
  - 验收：`Hands.Tests --filter DesktopScene` 30/30；`JsonAssertTests` 8/8。
- B2 `JsonLayoutAssertionRule`（JCC9311，AnalyzerId=`JsonSerializerAot`）：仅当 `ProjectContext.IsTest` 注册；命中 FluentAssertions `StringAssertions.Contain` / xunit `Assert.Contains` 字符串字面量含 `"key": value`（冒号后空格）排版特征，建议改用 JsonAssert。
  - 验收：`JsonLayoutAssertionRuleTests` 5/5；AotSafety.Tests 全量 267/267；真机构建 `guard.config.tests` SARIF 命中既有 9 处（`ConfigFileEncodingTests` 1、`ConfigKeyPathsTests` 8），Info 不阻塞编译；jcc.exe `--selftest-tui` HOST SELFTEST OK。
  - 存量 9 处暂不强制迁移（规则 Info 级，渐进清理）。

<!-- 🤖 Auto Decision: 2026-11-20 TASK101-B -->
<!-- 决策: JCC9311 定为 Info 级 + 仅测试项目注册；测试用单规则包装器（RuleRegistry.Get）而非整组 JsonSerializerAot 规则 -->
<!-- 原因: 存量 9 处排版断言若直接 Warning 会被 TreatWarningsAsErrors 全部打红，阻塞无关改动；只包装 JCC9311 避免 JCC1017 等规则在测试源码里干扰期望诊断 -->
<!-- 替代方案: Warning 级并立即迁移 9 处（扩大本批范围、风险后移）；xunit Assert 与 FluentAssertions 分两条规则（同一根因拆散校验口径）——均否决 -->
<!-- 验证: 267/267 单测 + SARIF 真机命中 9 处 + exe selftest 通过 -->

- B3 `Testing.Common/retry/FlakyRetry.cs`：`RunUntilAsync(Func<Task<bool>>, failureMessage, maxAttempts)` + `RunAsync(Func<Task>)`；瞬态异常视为失败继续重试，`OperationCanceledException` 直抛，耗尽后抛 `InvalidOperationException` 聚合每次尝试原因（含 InnerException）。
  - 迁移：`AskWithRetryTest` 两例手写 16 轮循环改为 `FlakyRetry.RunUntilAsync(..., maxAttempts: 16)`，断言口径（前缀 + CallCount）不变；`MailboxBase` 一例已在 B7 用语义等待（WaitAsync 10s）根治，不需要重试模板。
  - CI：`ci-unit-tests.yml` 测试过滤改为 `Category!=Integration&Category!=Benchmark&Category!=Flaky`；新增 `ci-flaky-nightly.yml`（schedule UTC 06:30 + workflow_dispatch，复用 ci-build 产物 + setup-test-env，`--filter Category=Flaky`，TRX 保留 14 天）。
  - 验收：`FlakyRetryTests` 5/5；`AskWithRetry` 12/12；`Category=Flaky` 15/15；默认过滤 541 = 总数 556 − Flaky 15；YAML 校验通过；jcc.exe `--selftest-tui` HOST SELFTEST OK。

<!-- 🤖 Auto Decision: 2026-11-20 TASK101-B3 -->
<!-- 决策: 落地 FlakyRetry 静态模板而非 RetryableFactAttribute；Flaky 标记仍用 [Trait("Category","Flaky")] 显式声明 -->
<!-- 原因: xunit v2 的 FactAttribute 子类无法注入 Trait（发现器只认 TraitAttribute），想靠特性自动完成"标记+重试"必须自研 test framework（重且易碎）；把重试循环收进一个静态方法和保留 Trait 是最小可靠组合 -->
<!-- 替代方案: 自研 XunitTestCase discoverer（维护成本远超收益）；只在文档里约定"手写 for 循环"（就是现在要消除的重复）——均否决 -->
<!-- 验证: 5+12+15 测试全绿；默认过滤数与 Flaky 数精确互补 -->

### C 批（2026-11-20 完成）

- C1 `GhPrCreateAsync`：PR URL（或 `PR #n` 兜底）固定首行，auto-merge 分支同样先给位置再给状态；分支保护审计降级为末尾警告并标注"CI 未跑完时可能误报"。
  - 验收：`PrCreate_AutoMergeTrue_PrUrlIsFirstLine`、`PrCreate_WithoutHtmlUrl_FallsBackToPrNumberFirstLine` 两单测；真实建 PR 动作留待用户 PR 流程验证。
- C2 `BranchProtectionAuditor`：对比口径对齐 sync-protection — 用 `CiMatrixParser.ExtractAllCheckNames(yml, jobId)` 生成 check 全名（matrix 展开 + 独立 job 原样保留），required 侧按 `{jobId} / ` 前缀过滤本 workflow；删除旧的 `ExtractMatrixJobNames`/`ExtractMatrixNameFromCheck`（" - " 截断正是误报来源）。
  - 验收：exe 实测 `gh branch audit-protection` 22 匹配、0 误报 stale；同时暴露 2 个**真实**缺失 required check（`Honesty checklist matches doc`、`Lock primitives - baseline only decreases`，需管理员同步，见下）。
- C3 `GhDispatcher.InvokeAsync` 新增可选 `ToolProgressCallback` 参数注入（GetParams 跳过该参数，不再出现 `--on-progress` 幻影选项）；CLI 把进度写 stderr；`--timeout/--interval/--fail-fast` 别名映射；等待类工具超时 CLI 退出码 3。
  - 验收：exe 实测 in_progress run：5 行进度、timeout 25s 生效、exit 3。
- C4 `GhRunListAsync`：`total_count==0` 且带过滤时追加"生效过滤 + 可用 event 值 + 去过滤基线命令"提示，终结静默空。
- C5 `mcp_list` JSON `meta.note` + 文本提示标注 gh_* 工具由 jcc-gh.exe 提供（172 个，不在 MCP 注册表）；`mcp_schema gh_*` 从"未找到工具"死路改为直接引导 `jcc gh <分组> <action> --help`。
- C6 `GhSubCommand` 识别 `--version` 输出 `jcc gh (jcc-gh) <版本>` + 用法提示，exit 0。
  - 验收：gh.tests 793/793，Host.Tests CliOutputEnvelope 33/33，exe 全项实测（见四列验收表）。
- 分支保护实同步（用户授权 `sync-protection`）：+`build / Build UI`、+`unit-tests / Honesty checklist matches doc`、+`unit-tests / Lock primitives - baseline only decreases`；首跑误删 `detect` 暴露 parser 缺口 — ci.yml 无 `name:` 的本地 job 不被解析，已修（`ExtractAllJobNames` 无 name 回退 job_id；`ExtractAllCheckNames` 支持裸前缀 + 排除 `uses:` caller job），补回 `detect` 后终态 38 个 check、审计 0 差异。gh.tests 794/794。

<!-- 🤖 Auto Decision: 2026-11-20 TASK101-C -->
<!-- 决策: C2 直接删除旧的 matrix 解析路径而不是在审计里做兼容映射；C5 采用"注册表边界说明"而非把 gh_* 合并进 MCP 注册表 -->
<!-- 原因: 两套 check 名口径（裸值 vs 全名）正是误报根因，保留旧解析只会复发；合并注册表需主进程跨 exe 拉取 172 工具，启动开销与双源漂移都比一句说明更贵 -->
<!-- 替代方案: 审计侧继续按裸 matrix 名比对并特判独立 job（把误报知识写进比对器）；mcp_list 实调 jcc-gh 填充 --list-tools（跨进程 + 缓存一致性）——均否决 -->
<!-- 验证: audit 实测 22 匹配 0 误报；mcp_list/schema 实测带 note/引导；793 单测全绿 -->

### D 批（2026-11-20 完成）

- D1 治理三层：① 根 `Directory.Build.props` 全局 `<NoWarn>...;CA2255</NoWarn>`（附治理说明）+ `CompilerVisibleProperty JccAllowModuleInitializer`；② `ProjectContext.AllowModuleInitializer`（读取 `build_property.JccAllowModuleInitializer`）；③ 新规则 `ModuleInitializerGovernanceRule`（JCC9312，Warning）：非测试程序集未登记 → 报错，测试程序集默认放行，已登记放行。
  - 登记：`Infrastructure.csproj` 由项目级 `NoWarn CA2255` 改为 `<JccAllowModuleInitializer>true</JccAllowModuleInitializer>`（注释即登记理由）。
  - 清理：18 份测试 `AssemblyInfo.cs` 的 `#pragma warning disable/restore CA2255` 全部移除（中央 NoWarn 接管）。
  - 验收：`ModuleInitializerGovernanceRuleTests` 3/3（未登记报/测试放行/登记放行）、AotSafety.Tests 270/270；真机负对照 `.xxx/jcc9312_probe`：未登记 → `error JCC9312`，加开关 → 0 命中；guard.config.tests 与 Infrastructure 真机编译 0 错误；jcc.exe `--selftest-tui` HOST SELFTEST OK。
  - 全仓编译（A1 脚本 4 target）顺带暴露 native_gui 存量红：`DshInteractionProtocol.cs` 字段默认值硬编码 `"positive"` 撞 JCC10007（PR #494 遗留），已改用 `DshFeedbackRating.Positive.ToValue()`；全仓重跑 exit 0。

<!-- 🤖 Auto Decision: 2026-11-20 TASK101-D1 -->
<!-- 决策: 用"根级 NoWarn + JCC9312 自定义规则"接管 CA2255，而不是继续项目级/admin 手工 NoWarn -->
<!-- 原因: CA2255 无法区分"测试引导惯例"与"库作者误用"；中央豁免 + 自定义规则可把判定口径集中（测试放行/非测试登记），并把登记理由固化为 csproj 可见开关，替代 18 份文件各自的 pragma 说明 -->
<!-- 替代方案: 保留 CA2255 原生告警并给每项目 NoWarn（就是当前的散落状态）；脚本 grep 门禁（只能事后检查，无法在编译期定位到具体方法）——均否决 -->
<!-- 验证: 负对照真机报错/登记后静默；270 分析器测试全绿 -->

<!-- 🤖 Auto Decision: 2026-11-20 TASK101-A2 回退（用户指令） -->
<!-- 决策: 删除 ci-build.yml 的 build-ui job；UI 域编译验证移到 ci-gui-daily.yml（每 2 天）并在分支保护移除 build / Build UI -->
<!-- 原因: windows-latest 构建每个 PR 都跑，CI 成本过高；用户明确要求 UI 采用定期 CI 验证 -->
<!-- 代价: 跨域契约漂移不能再在 PR 当次暴露，最迟 2 天在定期 job 出现 — 用户接受 -->
<!-- 替代方案: 只在 docs 改动的 PR 跳过 UI job（已有 detect 门控但仍按 PR 计费）；缩小 UI job 只编译不测试（省一半但违背用户"定期"要求）——均否决 -->
