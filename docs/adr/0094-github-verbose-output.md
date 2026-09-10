# 0094. GitHub 工具精简输出 + verbose 完整模式

- 状态：proposed
- 日期：2026-09-09

## 背景

GitHub 工具（`gh_pr_view`、`gh_issue_view`、`gh_repo_view` 等）直接返回 GitHub REST API 的完整 JSON 响应。一个 `gh_pr_view` 调用返回几千字符的 JSON，包含 `user.avatar_url`、`repo.full_name`、`_links` 等大量冗余字段，占用 AI 上下文、降低效率。

`gh_api` 已有 `max_lines` 截断（默认 500 行），但其他工具无任何精简逻辑。

## 决策

**默认精简 + `verbose` 缓存完整模式**：

1. **默认输出**：每个 GitHub 工具调 API → 缓存完整 JSON → 返回精简文本
2. **`verbose=true`**：查缓存 → 命中返回缓存完整 JSON（不调 API）→ 未命中调 API → 缓存 → 返回完整
3. **精简格式**：键值对文本（非 JSON），人类可读、AI 友好
4. **缓存位置**：`~/.jcc/gh_cache/{tool_name}_{hash(args)}.json`，每次默认调用更新缓存

### 数据流

```
默认调用:
  AI → gh_pr_view(pr=212) → 调 GitHub API → 缓存完整 JSON → 返回精简文本(300字符)

AI 想看完整:
  AI → gh_pr_view(pr=212, verbose=true) → 查缓存 → 命中 → 返回完整 JSON(3000字符, 0 API 调用)

AI 想刷新数据:
  AI → gh_pr_view(pr=212) → 调 GitHub API → 更新缓存 → 返回新精简文本
```

### 精简字段设计

| 工具 | 精简字段 |
|------|---------|
| `gh_pr_view` | number, state, title, draft, mergeable, mergeable_state, author, head→base, additions/deletions/changed_files, url |
| `gh_pr_list` | 每行: number, state, title, author (表格格式) |
| `gh_issue_view` | number, state, title, author, labels, created_at, url |
| `gh_issue_list` | 每行: number, state, title, author (表格格式) |
| `gh_repo_view` | name, full_name, private, default_branch, stars, forks, url |

### 缓存设计

- **缓存 key**：`{tool_name}_{SHA256(argsJson)[..8]}.json`（如 `gh_pr_view_a1b2c3d4.json`）
- **缓存目录**：`~/.jcc/gh_cache/`（与现有 `gh_cache` 目录统一）
- **缓存内容**：完整 GitHub API JSON 响应
- **缓存策略**：默认调用每次更新缓存（保证数据新鲜），`verbose=true` 只读缓存不调 API
- **缓存失效**：不设 TTL，由默认调用自然刷新；AI 想强制刷新就调一次默认（无 verbose）

### 实现方案

1. `GitHubToolHandlers.cs` 新增 `SummarizePr`/`SummarizeIssue`/`SummarizeRepo` 精简方法
2. `GitHubToolHandlers.cs` 新增 `TryGetGhCache`/`SaveGhCache`/`BuildCacheKey` 缓存辅助方法
3. 各 Handler 方法新增 `verbose` 参数（默认 false），统一走 `FetchOrCacheAsync` 辅助方法

## 替代方案（已否决）

1. **自动截断+按需展开**：超过阈值截断 + `--fields` 按需展开。否决原因：`--fields` 需要设计字段选择语法，复杂度高，AI 难以记忆
2. **jq 表达式过滤**：`--jq '.number,.state'`。否决原因：需要嵌入 jq 引擎或自实现 JSONPath，增加依赖和复杂度
3. **verbose=true 重新调 API**：否决原因：浪费 API 调用和网络，用户明确要求缓存节约

## 影响

- 改动文件：`GitHubToolHandlers.Pr.cs`、`GitHubToolHandlers.Issue.cs`、`GitHubToolHandlers.cs` + 测试
- verbose 参数可选（默认 false），无缓存时 fallback 到 API 调用
- AI 上下文节省：典型 PR view 从 ~3000 字符降至 ~300 字符
- API 调用节省：`verbose=true` 命中缓存时 0 API 调用
