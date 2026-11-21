# 架构门禁（architecture gate）

只读扫描全仓 `*.csproj` 的 `ProjectReference`，把结果映射到 16 个 modules 域 + `native_tui` / `native_gui`，并与 `baseline.json` 对比。脚本不构建、不改 csproj；只有 `-Mode Baseline` 会写基线文件。

配套决策与规则目录：

- `docs/proposed/adr/0161-architecture-contract-analyzer-and-gate.md`
- `docs/proposed/design/DSG052-architecture-contract-and-gate-plan.md`

## 用法

```powershell
# 只读报告（默认）
pwsh non_deliverables_tools/architecture/ArchitectureGate.ps1 -Mode Report

# 与基线对比；增长即 exit 1
pwsh non_deliverables_tools/architecture/ArchitectureGate.ps1 -Mode Validate

# 维护者清账后重冻结（不允许手工改 JSON）
pwsh non_deliverables_tools/architecture/ArchitectureGate.ps1 -Mode Baseline
```

常用参数：

| 参数 | 说明 |
|---|---|
| `-RepoRoot` | 仓库根；默认从脚本位置向上两级 |
| `-IncludeTests` | 保留测试项目参与统计（默认排除生产图） |
| `-Domains a,b` | 只统计指定来源域（本地增量） |
| `-OutputPath` | 报告写到文件；默认 stdout |

## 当前覆盖规则（P0 子集）

| 规则 | 判定 | 当前基线 |
|---|---|---:|
| `JCC12001` | 18 个域缺 `Directory.Build.props` 或 `JccDomain` 不匹配 | 18 |
| `JCC12002` | 生产 code 跨域边（待 P3 接 allowlist） | 474 |
| `JCC12003` | 跨域目标不是 `apiProjects` 冻结项 | 0 |
| `JCC12004` | 生产 → 测试引用 | 1 |
| `JCC12005` | 生产域图环 | 15 |
| `JCC12009` | 目录名非 `^[a-z0-9_]+$` | 176 |
| `JCC12010` | 域内 csproj 未进 `module.slnx` | 26 |
| `JCC12011` | 16 个 modules 域缺 `README.md` / `CHANGELOG.md` / `docs/` | 0 |
| `JCC12012` | Hub 入度超预算（当前仅报告） | 0 |

`JCC12006/12007/12008/12013` 属编译期分析器，不在此脚本内；`JCC12002` 的 allowlist 判定在 P3 接入。`JCC12011` 当前只查文档存在性，README 项目表行数校验收在 P4。

## 基线纪律

- `baseline.json` 由脚本生成，禁止手改数字。
- `-Mode Validate` 只允许计数下降；上升即失败。
- 新增例外必须写 `allow` 条目（`reason` + `adr` + `expires`），过期按 Error 处理。
- 分析器规则不进 baseline；用 `.editorconfig` severity 分阶段收紧。

## 口径

- 边分三类：`code` / `analyzer` / `test`；`analyzer` 边不参与架构判定。
- 生产图排除 `test/`、`*.tests`、`*.test` 项目；`JCC12004` 单独看生产→测试。
- 项目域按路径推导，不依赖单次 compilation；这正是分析器看不到全局图的部分。
- 基线路径统一 `/`、`repoRoot` 固定 `.`，可在 CI 复用。
