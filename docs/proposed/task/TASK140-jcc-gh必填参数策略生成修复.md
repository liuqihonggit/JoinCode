# TASK140 — jcc gh 必填参数策略生成修复

> 日期：2026-11-27
> 来源：PR #754 收尾时，`jcc gh` 先因宿主目录缺 `jcc-gh.exe` 失败；按官方部署恢复后，`jcc gh pr checks 754 --refresh` 又因 `pr_number` 未登记在策略中失败。

## 根因

1. `GhSubCommand` 只从 `AppContext.BaseDirectory` 查找 `jcc-gh.exe`；`gh` 是独立卫星项目，单独构建 `JoinCode.csproj` 不会产出或暂存该文件。该行为已由 ADR 0132 与 `Deploy-JccDev.ps1` 约定，本次通过全量构建卫星产物和官方部署恢复入口。
2. `resources/tools/policies/github.yaml` 由 `Generate-CliToolPolicies.py` 从静态 `help.txt` 生成；静态帮助由 `scripts/migrate_mcp_tools_to_cli_nodes.py` 扫描 `[McpToolParameter]` 生成。
3. 迁移扫描器的正则只接受字符串描述特性，不接受 `[McpToolParameter(WellKnownParam.PrNumber)]`。于是 `pr_number`、`issue_number`、`run_id` 等必填参数没有进入帮助文本，也没有进入策略白名单。
4. `CliToolPolicyEvaluator` 对未登记参数 fail-closed，因此动态帮助声明必填参数与硬策略拒绝同时出现。

## 执行清单

- [x] 恢复真实 `jcc gh --version` 入口，确认缺文件根因。
- [x] 定位静态帮助生成器漏扫 `WellKnownParam` 的根因。
- [x] 补生成器红灯回归。
- [x] 让迁移扫描器从 `WellKnownParam.cs` 读取唯一参数元数据。
- [x] 重生成 github 静态帮助、策略与信任目录。
- [x] 编译并部署卫星产物。
- [x] 用真实 `jcc gh pr checks` / `jcc gh run view` 验收。
- [x] 全量相关测试。
- [ ] 提交、推送并创建 auto-merge PR。

## 修复边界

- 不引入系统 `gh` 回退，不放宽策略校验。
- 不手写重复参数表；`WellKnownParam.cs` 继续作为唯一数据源。
- 本任务先修复生成链与 github 策略；其他组已有策略的全量同步不在本次范围内。

## 复审修正

- `WellKnownParam` 只提供描述、必填性和默认值；参数名与类型始终取方法签名，避免元数据覆盖真实 schema。
- 显式 `DefaultValue` 优先于公共默认值；`Limit` 公共描述改为不绑定具体数值，run/workflow 分别声明 20/50。
- 公共选项记录展开时按参数名去重，显式必填参数优先，消除 `repo` 必填/可选冲突。
- 方法参数正则接受 C# 转义标识符并归一化 `@base`/`@ref` 等名称，补回 PR 创建与 workflow 参数。

## 验证记录

- Python 生成器回归：8/8；旧实现下 `WellKnownParam.PrNumber` 与 `Limit` 均漏扫，红灯已复现。
- `Gh.Tests`：801/801，包含随 CI 运行的内嵌策略参数契约。
- `CliNodes.Tests`：155/155；同时修复两处 Windows CRLF 下用 `\n` 替换导致未构造畸形 YAML 的测试假绿。
- `Verify-SlashCommandPolicies.py`：99 个命令、101 个参数对账通过。
- `Verify-ToolNodes.ps1 -SkipBuild`：13 组、635 工具，声明与现实一致，策略/帮助/委托链通过。
- 主宿主与卫星 Debug 编译：0 警告、0 错误；共享策略资源使用 `--no-incremental` 重建。
- 完整 `JoinCode.slnx` Debug 编译：0 警告、0 错误。
- 策略差异：新增 1,015 项参数登记、移除 0 项；旧策略无人工 deny/ask 条目，未被覆盖。
- 生成链幂等：连续重跑帮助/策略生成后 SHA256 完全一致。
- 真实入口：`jcc gh pr checks 754 --refresh` 返回 38 通过、0 失败；`jcc gh run list --branch main --limit 3`、`jcc gh run view`、`tool call github gh_pr_checks` 均通过；`jcc gh pr create --dry_run` 验证 title/head/base 策略通过且无写入。
- `Deploy-JccDev.ps1` 已复制并校验主项目/卫星产物，但脚本最后仍被无关的 `file/read` `Auth.Missing` 检查拦住；本任务验收直接走用户真实 `jcc gh` 入口，未放宽该授权。

## 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| `WellKnownParam` 元数据扫描 | `migrate_mcp_tools_to_cli_nodes.py` | ✅ | ✅ Python 8/8，旧实现红灯 |
| 静态帮助参数补全 | `modules/tools/cli_nodes/groups/github/help.txt` | ✅ | ✅ 173 工具重生成且幂等 |
| 策略白名单与信任哈希 | `resources/tools/policies/github.yaml`、`policy_trust.yaml` | ✅ | ✅ 真实策略放行与 155 项契约测试 |
| 真实 `jcc gh` 调用 | `jcc-gh.exe` | ✅ | ✅ `pr checks`、`run list/view`、委托调用均实跑 |

<!-- Auto Decision: 2026-11-27 -->
<!-- 决策: 从 WellKnownParam.cs 解析唯一元数据，修复静态帮助生成器，再重生成 github 策略；不采用手补 YAML 或放宽授权。 -->
<!-- 原因: 手补只能治当前命令，生成链继续产生不完整帮助与假绿；唯一数据源可幂等消除同类遗漏。 -->
<!-- 替代方案: 仅给 github.yaml 增加 pr_number；能临时放行但保留根因。 -->
<!-- 验证: Python 8/8、Gh.Tests 801/801、CliNodes.Tests 155/155、斜杠策略门禁通过；生成链幂等，真实 jcc gh 三条链路通过。 -->
