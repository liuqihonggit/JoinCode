# 0075. gh CLI 排错避坑指南

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

`gh` CLI 在 PowerShell 环境下排错时存在大量引号、超时、管道死锁等陷阱。本文档汇总团队血泪踩坑记录，排错时必须按此指南操作，禁止重复踩坑。

> **⚠️ ADR 0073 后大部分 gh_* 已走 REST API（无引号/超时问题）**：gh_api/gh_pr_*/gh_issue_*/gh_repo_*/gh_release_(list/view/create/download/delete)/gh_run_(list/rerun/cancel) 均直调 GitHub REST API。仅 **gh_run_view**（缓存+并行下载逻辑复杂）和 **gh_release_upload**（需二进制上传）仍用 gh 子命令，以下坑仅对这两个命令适用。

## 详细内容

### 坑1：`gh api` + jq 在 PowerShell 中引号被吃掉

```powershell
# ❌ 绝对禁止：jq 把 "failure" 解释为除法，报 function not defined: failure/0
gh api .../jobs --jq '.jobs[] | select(.conclusion=="failure") | .id'

# ✅ 正确写法：用反引号转义双引号
gh api .../jobs --jq ".jobs[] | select(.conclusion==`"failure`") | {name:.name, id:.id}"

# ✅ 最可靠：写入 JSON 文件再用 PowerShell ConvertFrom-Json 解析，彻底绕开 jq 引号地狱
gh api repos/{owner}/{repo}/actions/runs/{run-id}/jobs > .xxx/jobs.json
Get-Content .xxx/jobs.json | ConvertFrom-Json | Select-Object -ExpandProperty jobs | Where-Object { $_.conclusion -eq "failure" } | Select-Object name, id
```

**根因**：PowerShell 把双引号当字符串边界吃掉了，jq 收到的是裸单词 `failure`，被解释为除法。别试图用单引号包双引号——PowerShell 单引号不转义，jq 又不认。反引号转义或干脆走 JSON 文件。

### 坑2：`gh run view --log-failed` 直接超时炸掉

- 失败日志动辄几万行，`--log-failed` 一把梭直接超 60s 超时
- **⛔ 禁止**：`gh run view <run-id> --log-failed` 不加过滤
- **✅ 正确做法**：先拿到失败 job ID，再 `--job <job-id> --log` 精准拉日志，配合 `Select-String` 过滤

```powershell
# 第一步：拿失败 job ID（走 JSON 文件，别用 jq）
gh api repos/{owner}/{repo}/actions/runs/{run-id}/jobs > .xxx/jobs.json
Get-Content .xxx/jobs.json | ConvertFrom-Json | Select-Object -ExpandProperty jobs | Where-Object { $_.conclusion -eq "failure" } | Select-Object name, id

# 第二步：精准拉单个 job 日志，过滤关键行
gh run view <run-id> --job <job-id> --log 2>&1 | Select-String "Failed|FAIL|Test Run Failed|error" | Select-Object -First 20
```

### 坑3：`gh api` 取 job logs 被 Sandbox 网络拦截

```powershell
# ❌ 报错：Sandbox Network Error: hit restricted [20.205.243.168:443]
gh api repos/{owner}/{repo}/actions/jobs/<job-id>/logs
```

- `gh api` 走的 API 端点可能被 Sandbox 网络策略拦截
- **✅ 解法**：改用 `gh run view --job <job-id> --log`，走不同的 API 路径，不会被拦

### 坑4：`gh pr checks` 输出格式

```
<check-name>\t<status>\t<duration>\t<url>
```

| status 值 | 含义 |
|-----------|------|
| `pass` | CI 通过 |
| `fail` | CI 失败 |
| `pending` | 正在运行 |
| `skipping` | 前置 job 失败导致跳过 |

**注意**：`skipping` 不是失败！别看到一堆 `skipping` 就以为全挂了，那是依赖链跳过。

### 坑5：`gh run view --job --log` + `Select-String` 管道仍超时（120s+）

- 坑2 的"正确做法"(`--job <job-id> --log 2>&1 | Select-String`)在日志量大时**仍会超时**
- 原因：`gh run view --log` 一次性输出全部日志到 stdout，PowerShell 管道消费端 `Select-String` 逐行处理，当日志几万行时 120s 超时
- **⛔ 禁止**：`gh run view <run-id> --job <job-id> --log 2>&1 | Select-String "..." | Select-Object -First 20`（超时炸掉）
- **✅ 正确做法**：重定向写文件再查（绕开 PowerShell 管道死锁）

```powershell
# 第一步：重定向写文件（不用管道，不会超时）
gh run view <run-id> --job <job-id> --log > .xxx/job_log.txt 2>&1

# 第二步：从文件过滤关键行（Select-String 读文件不卡）
Select-String -Path .xxx/job_log.txt -Pattern "Failed|FAIL|错误|失败|Exception|Assert" | Select-Object -First 30

# 第三步：看测试失败上下文（匹配行前后 5 行）
Select-String -Path .xxx/job_log.txt -Pattern "失败|Failed" -Context 5,5 | Select-Object -First 10
```

- **⚠️ 如果重定向也超时**：说明 `gh run view --log` 本身就卡（日志太大或网络慢），改用 `gh api` 分页拉取或直接在 GitHub Actions 页面查看日志
- **✅ 实战经验（2026-09-04）**：`gh run view --log` 和 `gh run view --log-failed --job=<id>` 在日志量大时**双双超时**（30s/60s 都不够）。最快替代方案：**直接本地复现失败测试** — `gh run view <run-id> --job <job-id>`（不带 --log）看摘要知道哪个步骤失败 → 本地 `dotnet test <csproj> -c Release --filter "Category!=#Integration"` 跑全量测试看哪个测试 FAIL → 看错误消息和堆栈定位根因。比拉 CI 日志快 10 倍以上

### CI 排错完整流程（按此顺序，不许跳步）

```powershell
# 1. 查看哪些 check 失败
gh pr checks <pr-number>

# 2. 拿到 run-id（从 check URL 里提取，或 gh pr view）
gh pr view <pr-number> --json statusCheckRollup

# 3. 获取失败 job ID（走 JSON 文件，别用 jq）
gh api repos/{owner}/{repo}/actions/runs/<run-id>/jobs > .xxx/jobs.json
Get-Content .xxx/jobs.json | ConvertFrom-Json | Select-Object -ExpandProperty jobs | Where-Object { $_.conclusion -eq "failure" } | Select-Object name, id

# 4. 拉失败 job 日志，过滤关键信息（重定向写文件，不用管道，避免超时）
gh run view <run-id> --job <job-id> --log > .xxx/job_log.txt 2>&1
Select-String -Path .xxx/job_log.txt -Pattern "Failed|FAIL|Test Run Failed" | Select-Object -First 30

# 5. 本地复现失败测试
dotnet test <csproj> -c Release --filter "<test-name>" --nologo /p:SkipLocalPack=true
```

## 替代方案

无。此指南为团队实战经验汇总，必须遵守。
