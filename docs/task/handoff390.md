# jcc gh 工具缺陷报告（交接文档）

> 创建时间：2026-10-08
> 场景：用 jcc gh 排查 PR #390 CI 失败时遇到的工具缺陷
> 关乎：ADR 0132（jcc gh 与系统 gh CLI 耦合测试关系）

## 缺陷 1：MergeJsonArrays 丢弃对象结构分页（最严重）

**症状**：`gh run view --expand jobs` 在 40+ matrix job 时只显示 30 个，且"X 个失败"计数不准（显示 0 个失败但实际有 1 个在第 2 页）。`--expand failed` 匹配 0 行（失败 job 在第 2 页时 `failedJobIds` 为空）。

**根因**：`lib/infrastructure/io/process/GitHubApiClient.cs:696-710`

```csharp
private static string MergeJsonArrays(IReadOnlyList<string> bodies) {
    foreach (var body in bodies) {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind == JsonValueKind.Array) {
            // 顶层数组: 合并 ✅
        } else {
            return bodies[0];  // ← 对象结构直接返回第一页，丢弃第 2/3 页 ❌
        }
    }
}
```

GitHub Actions API 的 `/actions/runs/{id}/jobs` 返回 `{"jobs":[...], "total_count":45}` **对象**（非顶层数组），`MergeJsonArrays` 走 else 分支只返回第一页 30 个 job。同一 bug 影响 `expand=jobs`、`expand=failed`、`gh pr checks`（check-runs 端点同结构）。

**修复方向**：识别 `{"jobs":[]}`/`{"check_runs":[]}`/`{"items":[]}`/`{"workflow_runs":[]}` 等常见包裹键，提取内部数组合并。

---

## 缺陷 2：boolean 参数不宽容 `--log true`

**症状**：`gh run view 123 --log true` 报"多余的位置参数: true"，必须写 `--log`（不带值）或 `--log=true`。

**根因**：`app/cli/core/commands/core/GhCommandResolver.cs:270-273`

```csharp
if (param.IsBoolean) {
    result[key] = "true";   // ← 直接设 true
    continue;               // ← 不消费下一个 token，"true" 变成多余位置参数
}
```

AI 习惯写 `--log true`（显式传值），但 jcc boolean 参数设计是无值 flag，遇到 `--log` 直接设 true 并跳过，下一个 token `true` 被当作多余位置参数报错。

**修复方向**：boolean 参数遇到下一 token 是 `true`/`false` 时消费它，而非当位置参数报错。

---

## 缺陷 3：run number / run id 不兼容

**症状**：`gh run view 752`（run number）返回 Not Found，必须用 `gh run view 37663049294`（run id）。

**根因**：`gh run view` 的 `run_id` 参数只接受 GitHub 内部数字 id（十几位），不接受人类可读的 run number（几百）。`gh run list` 输出同时显示 ID 和 NUM 两列，AI 容易拿 NUM 调 view 失败。

**修复方向**：若 `run_id` 值 < 1000000（明显是 run number），自动调 `/actions/runs` 查询转换；或参数同时兼容两种。

---

## 缺陷 4：`gh api` 无 --jq，AI 被迫 python 二次解析

**症状**：`gh api "repos/.../jobs"` 返回 `{"ok":true,"data":"{\"jobs\":[...]}"}` 双层包装（CLI 输出信封 + JSON 字符串），AI 需要python `json.loads` 两次才能提取字段。

**根因**：
- `kit/mcp/git_hub/GitHubToolHandlers.Api.cs:6` 故意禁用 `--jq`（注释"避坑1: 禁用 --jq"）
- `app/cli/core/output/CliOutputContract.cs:28` 的 `CliOutputEnvelope` 在 JSON 模式下统一包 `{ok:true, data:...}` 信封

**修复方向**：`gh api` 加 `--jq` 参数支持 JSON 字段提取（如 `.jobs[] | select(.conclusion=="failure") | .name`），或加 `--json_fields` 支持嵌套路径。

---

## 缺陷 5：`--expand failed` 0 行匹配时提示不精准

**症状**：`--expand failed` 返回"未匹配到任何日志行"，不提示可能是分页 bug 导致失败 job 丢失。

**根因**：`kit/mcp/git_hub/GitHubRunLogFilterRunner.cs:177-178` 只输出"未匹配到任何日志行"，不区分"真的没有失败"和"失败 job 被分页丢弃"。

**修复方向**：0 行匹配时提示可能原因（①filter 不匹配 ②分页 bug ③建议用 `--expand steps` 手动查看）。

---

## 缺陷 6：`gh pr checks` 超时 120s

**症状**：`jcc gh pr checks 390` 超时 120s 无响应，AI 不得不回退系统 `gh pr checks 390`。

**根因**：`kit/mcp/git_hub/GitHubToolHandlers.Pr.cs:269-308` 串行调 2-3 个 API（有依赖关系，串行必要），但 check-runs 端点不传 `per_page` 参数，默认 30 条，大量 check 时可能慢或丢数据。watch 模式固定间隔轮询 120 次效率更低。

**修复方向**：check-runs 端点加 `per_page=100`；watch 模式改指数退避。

---

## 缺陷汇总

| # | 缺陷 | 根因文件:行号 | 严重度 | 一个修复解决多个症状 |
|---|------|-------------|--------|-------------------|
| 1 | MergeJsonArrays 丢页 | `GitHubApiClient.cs:696-710` | **高** | ✅ expand=jobs + expand=failed + pr checks |
| 2 | --log true 报错 | `GhCommandResolver.cs:270-273` | 中 | — |
| 3 | run number/id 不兼容 | `gh run view` handler | 中 | — |
| 4 | gh api 无 --jq | `GitHubToolHandlers.Api.cs:6` | 中 | — |
| 5 | 0 行匹配提示差 | `GitHubRunLogFilterRunner.cs:177` | 低 | — |
| 6 | pr checks 超时 | `GitHubToolHandlers.Pr.cs:269` | 中 | — |

**修缺陷 1 一处可同时解决 expand=jobs 截断 + expand=failed 0 行 + pr checks check-runs 丢数据三个症状，ROI 最高。**
