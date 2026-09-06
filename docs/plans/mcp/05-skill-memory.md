# MCP 工具测试计划 05: 技能与记忆 Skill + Memory

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 05/12 |
| 涵盖分类 | Skill (14) + Memory (13) |
| 工具总数 | 27 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译,记忆目录可读写 |

## 工具清单

### Category: Skill (14 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `skill_simplify` | 简化技能 | ✅ |
| 2 | `skill_verify` | 验证技能 | ✅ |
| 3 | `skill_debug` | 调试技能 | ✅ 修复 |
| 4 | `skill_batch` | 批量技能 | ✅ |
| 5 | `skill_stuck` | 卡住处理 | ✅ |
| 6 | `tool_create` | 创建工具 | ✅ |
| 7 | `tool_list_templates` | 列出模板 | ✅ |
| 8 | `tool_show_template` | 显示模板 | ✅ |
| 9 | `skill_search` | 搜索技能 | ✅ |
| 10 | `skill_recommend` | 推荐技能 | ✅ |
| 11 | `discover_skills` | 发现技能 | ✅ |
| 12 | `skill` | 技能 | ✅ 修复 |
| 13 | `skill_execute` | 执行技能 | ✅ |
| 14 | `skill_list` | 列出技能 | ✅ |

### Category: Memory (13 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `memory_daily_log_append` | 追加日志 | ✅ |
| 2 | `memory_daily_log_get` | 获取日志 | ✅ |
| 3 | `memory_search_history` | 搜索历史 | ✅ |
| 4 | `memory_team_sync` | 团队同步 | ✅ |
| 5 | `memory_team_status` | 团队状态 | ✅ |
| 6 | `memory_scan` | 扫描记忆 | ✅ |
| 7 | `memory_age` | 记忆年龄 | ✅ |
| 8 | `memory_cleanup` | 清理记忆 | ✅ |
| 9 | `memory_health` | 记忆健康 | ✅ |
| 10 | `memory_add_team_path` | 添加团队路径 | ✅ |
| 11 | `memory_list_team_paths` | 列出团队路径 | ✅ |
| 12 | `memory_remove_team_path` | 移除团队路径 | ✅ |
| 13 | `memory_scan_team` | 扫描团队记忆 | ✅ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$skillTools = @(
    "skill_simplify", "skill_verify", "skill_debug", "skill_batch", "skill_stuck",
    "tool_create", "tool_list_templates", "tool_show_template",
    "skill_search", "skill_recommend", "discover_skills", "skill", "skill_execute", "skill_list"
)
$memoryTools = @(
    "memory_daily_log_append", "memory_daily_log_get", "memory_search_history",
    "memory_team_sync", "memory_team_status", "memory_scan", "memory_age",
    "memory_cleanup", "memory_health", "memory_add_team_path",
    "memory_list_team_paths", "memory_remove_team_path", "memory_scan_team"
)
foreach ($t in $skillTools + $memoryTools) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t 2>&1 | Select-Object -First 5
}
```

### 只读工具冒烟测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
& $jcc --trust --bypass mcp_call skill_list
& $jcc --trust --bypass mcp_call discover_skills
& $jcc --trust --bypass mcp_call tool_list_templates
& $jcc --trust --bypass mcp_call memory_health
& $jcc --trust --bypass mcp_call memory_list_team_paths
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出
- [ ] 无崩溃/超时/死锁
- [ ] `memory_cleanup` 可重复执行(幂等)
- [ ] `memory_add_team_path` / `memory_remove_team_path` 互为逆操作
- [ ] 技能执行失败时给出明确错误提示

## 风险提示

- `memory_cleanup` 可能删除旧记忆,注意备份
- `memory_team_sync` 涉及网络操作,可能超时
- `tool_create` 创建新工具,注意命名冲突
- `skill_execute` 执行技能可能有副作用

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| `Skill` | 工具名大写 `Skill`，违反小写下划线命名规范 | `SkillToolName.cs` 第8行 `[EnumValue("Skill")]` | 改为 `[EnumValue("skill")]` |
| `skill_debug` | 文件路径当目录用导致 ArgumentException | `BundledSkillToolHandlers.cs` 第225行 `GetDebugSuggestionsAsync(path,...)` 把文件路径当目录传给 `_fs.GetFiles(path,...)` | 文件路径时用 `Path.GetDirectoryName(path)` 获取所在目录 |

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。

## 最终结果

- **测试时间**: 2026-09-06
- **工具总数**: 27 (Skill 14 + Memory 13)
- **通过**: 27
- **坏点**: 2 (已修复)
- **坏点详情**:
  1. `Skill` → `skill`: 工具名大写违反命名规范（`SkillToolName.cs`）
  2. `skill_debug`: 文件路径当目录用导致 ArgumentException（`BundledSkillToolHandlers.cs`）
- **commit**: `b5d4d01` - fix: 统一Skill工具名为小写+修复skill_debug文件路径当目录用导致异常
