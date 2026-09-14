# MCP 工具测试计划 11: Shell / PowerShell / MCP资源 / 定时 / 代码分析 / Web / 语音 / 配置

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 11/12 |
| 涵盖分类 | Shell (6) + PowerShell (5) + McpResource (5) + Cron (4) + CodeAnalysis (4) + Web (4) + Voice (4) + Config (3) |
| 工具总数 | 35 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译 |

## 工具清单

### Category: Shell (6 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `bash` | Bash 执行 | ⬜ |
| 2 | `shell_background_get` | 获取后台输出 | ⬜ |
| 3 | `shell_background_list` | 列出后台进程 | ⬜ |
| 4 | `shell_background_output` | 后台输出 | ⬜ |
| 5 | `shell_background_cancel` | 取消后台 | ⬜ |
| 6 | `shell_background_kill_all` | 终止所有后台 | ⬜ |

### Category: PowerShell (5 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `powershell` | PowerShell 执行 | ⬜ |
| 2 | `powershell_script` | 执行脚本 | ⬜ |
| 3 | `powershell_version` | 版本信息 | ⬜ |
| 4 | `powershell_execution_policy` | 执行策略 | ⬜ |
| 5 | `powershell_set_execution_policy` | 设置执行策略 | ⬜ |

### Category: McpResource (5 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `mcp_remote_list_resources` | 列出远程资源 | ⬜ |
| 2 | `mcp_remote_read_resource` | 读取远程资源 | ⬜ |
| 3 | `mcp_remote_list_prompts` | 列出远程提示 | ⬜ |
| 4 | `mcp_get_prompt` | 获取提示 | ⬜ |
| 5 | `mcp_list_clients` | 列出客户端 | ⬜ |

### Category: Cron (4 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `cron_create` | 创建定时任务 | ⬜ |
| 2 | `cron_list` | 列出定时任务 | ⬜ |
| 3 | `cron_delete` | 删除定时任务 | ⬜ |
| 4 | `cron_validate` | 验证 cron 表达式 | ⬜ |

### Category: CodeAnalysis (4 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `analyze_csharp_code` | 分析 C# 代码 | ⬜ |
| 2 | `find_bugs` | 查找 Bug | ⬜ |
| 3 | `optimize_code` | 优化代码 | ⬜ |
| 4 | `security_audit` | 安全审计 | ⬜ |

### Category: Web (4 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `download_file` | 下载文件 | ⬜ |
| 2 | `web_fetch` | 抓取网页 | ⬜ |
| 3 | `web_to_markdown` | 转 Markdown | ⬜ |
| 4 | `web_search` | 网页搜索 | ⬜ |

### Category: Voice (4 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `voice_start_recording` | 开始录音 | ⬜ |
| 2 | `voice_stop_recording` | 停止录音 | ⬜ |
| 3 | `voice_transcribe` | 语音转文字 | ⬜ |
| 4 | `voice_status` | 语音状态 | ⬜ |

### Category: Config (3 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `config_get` | 获取配置 | ⬜ |
| 2 | `config_set` | 设置配置 | ⬜ |
| 3 | `config_list` | 列出配置 | ⬜ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$shellTools = @("bash", "shell_background_get", "shell_background_list", "shell_background_output", "shell_background_cancel", "shell_background_kill_all")
$psTools = @("powershell", "powershell_script", "powershell_version", "powershell_execution_policy", "powershell_set_execution_policy")
$mcpResTools = @("mcp_remote_list_resources", "mcp_remote_read_resource", "mcp_remote_list_prompts", "mcp_get_prompt", "mcp_list_clients")
$cronTools = @("cron_create", "cron_list", "cron_delete", "cron_validate")
$codeAnalysisTools = @("analyze_csharp_code", "find_bugs", "optimize_code", "security_audit")
$webTools = @("download_file", "web_fetch", "web_to_markdown", "web_search")
$voiceTools = @("voice_start_recording", "voice_stop_recording", "voice_transcribe", "voice_status")
$configTools = @("config_get", "config_set", "config_list")
$all = $shellTools + $psTools + $mcpResTools + $cronTools + $codeAnalysisTools + $webTools + $voiceTools + $configTools
foreach ($t in $all) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t 2>&1 | Select-Object -First 5
}
```

### 只读工具冒烟测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
& $jcc --trust --bypass mcp_call powershell_version
& $jcc --trust --bypass mcp_call powershell_execution_policy
& $jcc --trust --bypass mcp_call cron_list
& $jcc --trust --bypass mcp_call config_list
& $jcc --trust --bypass mcp_call voice_status
& $jcc --trust --bypass mcp_call mcp_list_clients
& $jcc --trust --bypass mcp_call shell_background_list
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出
- [ ] 无崩溃/超时/死锁
- [ ] `cron_create` / `cron_delete` 互为逆操作
- [ ] `cron_validate` 验证非法表达式时给出明确错误
- [ ] `voice_start_recording` / `voice_stop_recording` 互为逆操作
- [ ] `config_set` + `config_get` 往返一致
- [ ] Web 工具在无网络时优雅降级

## 风险提示

- `bash` / `powershell` 执行任意命令,注意安全
- `shell_background_kill_all` 终止所有后台进程,注意副作用
- `powershell_set_execution_policy` 修改系统设置,注意恢复
- `download_file` 可能下载大文件,注意磁盘空间
- `web_search` / `web_fetch` 涉及网络,可能超时
- `voice_start_recording` 访问麦克风,注意隐私

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| | | | |

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。
