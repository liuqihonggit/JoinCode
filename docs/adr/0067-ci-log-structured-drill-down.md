# 0067. CI 日志结构化逐级展开（Section 级 drill down）

- 状态：accepted
- 日期：2026-09-06
- 决策者：AI + 用户确认

## 背景

当前 `gh_run_view` 的 `expand=step:Name` 返回该步骤的**所有日志行**（按 `skip_lines` 分页续读）。问题：

1. **AI 逐页扫描效率低**：CI 步骤 "Test - Brain (Context)" 有 936 行日志，AI 需要多次调用 `skip_lines=0→200→400→600→800` 才能找到 `##[error]` 标记的错误行
2. **平铺结构无层次**：所有日志行平铺返回，AI 无法直接跳到 error 段，必须线性扫描
3. **上下文浪费**：每页 200 行日志注入 AI 上下文，多轮调用后上下文被大量无关日志占据

用户要求：**结构化表达、逐级展开、逐级填充、在级内分页**，让 AI 渐进式查看，直接跳到错误段而非逐页扫描。

## 决策

采用 **Section 级展开**方案 — 按 GitHub Actions 日志标记（`##[group]`/`##[error]`/`##[warning]`/`##[command]`）将步骤内日志分段，逐级返回摘要而非全量日志。

### 1. 四级展开层次

```
Level 0: Run 概要（状态、结论、jobs 列表）           ← 已有（log=false）
Level 1: Step 列表（步骤名 + 行数）                   ← 已有（expand=steps）
Level 2: Section 摘要（标记类型 + 计数）              ← 新增（expand=step:Name）
Level 3: Section 内容（日志行，级内 skip_lines 分页）  ← 新增（expand=step:Name/section:error）
```

### 2. Section 分类

按 GitHub Actions 日志标记分为 5 类 section：

| Section 类型 | 匹配标记 | 语义 | 优先级 |
|-------------|---------|------|--------|
| `error` | `##[error]` | 错误行 | 0（最高，排障首要查看） |
| `warning` | `##[warning]` | 警告行 | 1 |
| `command` | `##[command]` | 命令行 | 2 |
| `group` | `##[group]` | 分组开始 | 3 |
| `normal` | 无标记 | 普通日志行 | 4（最低） |

### 3. expand 参数语法扩展

| 调用 | 返回 | 示例 |
|------|------|------|
| `expand=steps` | 步骤列表（Level 1） | `936 行 Test - Brain (Context)` |
| `expand=step:Name` | **Section 摘要**（Level 2，改） | `error:1, warning:0, command:2, group:5, normal:928` |
| `expand=step:Name/section:error` | **error section 内容**（Level 3，新增） | `##[error]Process completed with exit code 1.` |
| `expand=step:Name/section:group` | group section 内容 | `##[group]Run dotnet test...` |

**section 类型支持简写**：`section:error` = `section:Error` = `section:ERROR`（大小写不敏感）。

### 4. AI 渐进式查看路径

```
1. gh_run_list → 发现 conclusion=failure
2. gh_run_view run_id=xxx expand=steps → 11 个步骤
3. gh_run_view run_id=xxx expand=step:Test - Brain (Context) → error:1, warning:0, group:5, normal:928
4. gh_run_view run_id=xxx expand=step:Test - Brain (Context)/section:error → ##[error]Process completed with exit code 1.
5. 如需上下文 → expand=step:Test - Brain (Context)/section:group → 查看命令详情
```

**对比当前方案**：
- 当前：步骤 3 返回 936 行，AI 需要 `skip_lines=0→200→400→...` 逐页扫描找 error
- 新方案：步骤 3 返回 5 个 section 计数，步骤 4 直接返回 1 行 error，**2 次调用定位错误**

### 5. 数据结构

**位置**：`services/Mcp/src/GitHub/RunLogCache.cs` 扩展

```csharp
internal sealed class RunLogCache
{
    public required string RunId { get; init; }
    public string? JobId { get; init; }
    
    // 现有：步骤名 → 日志行列表
    public Dictionary<string, List<string>> Steps { get; init; } = new(...);
    
    // 新增：步骤名 → Section 列表（按标记分类）
    public Dictionary<string, List<LogSection>> StepSections { get; init; } = new(...);
    
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

internal sealed class LogSection
{
    public required string Type { get; init; }      // error/warning/command/group/normal
    public required List<string> Lines { get; init; } // 该 section 的日志行
    public int StartLine { get; init; }              // 在步骤内的起始行号（用于 skip_lines 续读）
}
```

### 6. Section 解析逻辑

在 `GetOrFetchCacheAsync` 流式拉取时，按行解析标记，将日志行分配到对应 section：

```csharp
foreach (var line in lines)
{
    var sectionType = ParseSectionType(line);  // 检查 ##[error] / ##[warning] / ##[command] / ##[group] / 无标记
    // 添加到对应 section
}
```

**`##[group]` 特殊处理**：`##[group]Name` 开始一个 group，后续行属于该 group 直到 `##[endgroup]`。group section 的 `Lines` 包含 group 内所有行。

### 7. Level 2 返回格式（Section 摘要）

```
Run 33970784713 步骤:Test - Brain (Context) sections:
  error:    1 行  ##[error]Process completed with exit code 1.
  warning:  0 行
  command:  2 行  dotnet test, dotnet build
  group:    5 行  Run dotnet test, Set up env, ...
  normal: 928 行  Passed xxx, Passed yyy, ...

💡 下一步:
- expand=step:Name/section:error → 查看 error 段(排障首要)
- expand=step:Name/section:group → 查看 group 段(命令上下文)
- expand=step:Name/section:normal → 查看普通日志(测试结果)
```

### 8. Level 3 返回格式（Section 内容）

Section 内容用 `skip_lines` 分页（与当前行级分页一致）：

```
Run 33970784713 步骤:Test - Brain (Context) section:error (1 行):
##[error]Process completed with exit code 1.
```

如果 section 内容多（如 `normal` 有 928 行），截断提示包含 `skip_lines` 续读。

### 9. 向后兼容

- `expand=step:Name` **行为变更**：从返回所有日志行改为返回 section 摘要
- **迁移策略**：如果用户不传 `/section:xxx`，返回 section 摘要 + 提示"用 /section:error 查看 error 段"
- 旧用法 `expand=step:Name + skip_lines=N` 不再直接返回日志行，而是提示用户用 `/section:normal` + `skip_lines` 查看普通日志段

### 10. 按 job 并行下载（首次下载提速）

**问题**：`gh run view --log` 串行下载整个 run 日志（内部调 `GET /repos/{owner}/{repo}/actions/runs/{run_id}/logs` 获取 zip 归档），25 个 job 的 run 耗时 ~43s。文件级缓存已将后续调用降至 3s，但首次调用仍需 43s。

**方案**：用 `GET /repos/{owner}/{repo}/actions/jobs/{job_id}/logs` 按 job 并行下载：

1. 先调 `GET /repos/{owner}/{repo}/actions/runs/{run_id}/jobs` 获取所有 job_id 列表（< 1s）
2. 并行调 `GET /repos/{owner}/{repo}/actions/jobs/{job_id}/logs` 下载每个 job 的日志（302 重定向到纯文本）
3. 合并所有 job 日志，用 job 名作为 step 名，构建 RunLogSummary + section 内容

**实测数据**：
- 全量下载（`gh run view --log`）：43s
- 单个 job 下载：8.8s
- 并行下载 25 个 job 理论上：~10-15s（最大 job 的下载时间），提速 3-4 倍

**并发度限制**：用 `SemaphoreSlim(8)` 控制并发，避免 GitHub API 二级限速。

**格式适配**：`gh api .../jobs/{job_id}/logs` 返回 `Timestamp\tLogLine`（2 列），`gh run view --log` 返回 `JobName\tStepName\tTimestamp\tLogLine`（4 列）。6. 用 job 名作为 step 名，每行添加 `JobName\t` 前缀。

**与文件级缓存集成**：并行下载后存入 MemoryCache + `.jcc/gh_cache/` 文件，后续调用仍走缓存。

## 替代方案

- **Test Result 级展开**：解析 trx 格式，`expand=step:Name/failed` 列出失败测试名。最精确但只适用 dotnet test 步骤，不通用。未采用（保留为未来增强，对 dotnet test 步骤自动检测 trx）。
- **混合方案**：通用步骤用 Section 级，dotnet test 步骤自动检测 trx 并用 Test Result 级。最精确但最复杂，当前先实现通用方案。未采用（保留为未来增强）。
- **保持当前按行分页**：简单但 AI 需要多轮调用逐页扫描才能找到错误。未采用（用户明确要求结构化逐级展开）。
- **按时间窗口分段**：按时间戳将日志分为 5s/10s/30s 窗口。不适合排障（错误行和上下文可能跨窗口）。未采用。

## 后果

- 正面：AI 排障效率大幅提升 — 2 次调用定位错误（steps → section:error），而非逐页扫描 936 行。Section 摘要返回计数，不撑爆上下文。通用性强，适用所有 CI 步骤（不限于 dotnet test）。
- 负面：`expand=step:Name` 行为变更（从返回日志行改为返回 section 摘要），需要更新单元测试。Section 解析增加复杂度（`##[group]`/`##[endgroup]` 配对、标记识别）。`RunLogCache` 数据结构扩展，内存占用略增。
- 中性：`skip_lines` 分页从步骤级下移到 section 级，语义更精确（在 error section 内分页 vs 在所有日志行内分页）。

## 反向引用

- AGENTS.md「规则2 MCP工具覆盖原则」— `expand` 参数扩展属于 gh_run_view 工具增强
- AGENTS.md「开心路径」— Level 2 先返回 section 摘要，AI 按需 drill down 到 Level 3，不一次性返回全量
- AGENTS.md「替换便利」— Section 解析逻辑封装为 `ParseSectionType`，便于未来替换为 trx 解析
- AGENTS.md「数据容器选型」— `StepSections` 用 `Dictionary<string, List<LogSection>>`，O(1) 查找

## 调查来源

- GitHub Actions 日志标记格式：`##[error]` / `##[warning]` / `##[command]` / `##[group]` / `##[endgroup]`
  - https://docs.github.com/en/actions/using-workflows/workflow-commands-for-github-actions#setting-an-error-message
- 当前 `gh_run_view` 实现：`services/Mcp/src/GitHub/GitHubToolHandlers.Run.cs`
- ToolSearch `map[]` 逐层展开设计（复刻参考）：`expand=steps` → `expand=step:Name` → `expand=step:Name/section:Type`
