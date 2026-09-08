# 0077. MockServer + jcc 联合测试

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

E2E 测试需要真实启动 MockServer exe 和 jcc exe 进行联合测试，验证完整链路。阻塞进程禁止直接运行，必须后台启动，否则会卡住 sandbox。

## 详细内容

⚠️ **阻塞进程禁止直接运行**，必须后台启动，否则会卡住 sandbox。

**MockServer 参数表**

| 参数 | 格式 | 默认值 | 说明 |
|------|------|--------|------|
| `--port` | `--port <数字>` | 配置文件中的 `port` 字段（0=自动分配） | 监听端口 |
| `--config` | `--config <路径>` | `mockserver.json` | 预设脚本配置文件 |

**MockServer 端点**

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/` | 健康检查，返回 `{"status":"ok"}` |
| GET | `/shutdown` | 优雅关闭，返回 `{"status":"shutting_down"}` |
| POST | `/v1/chat/completions` | OpenAI 兼容的 chat 接口（stream=true/false） |
| POST | `{**path}` | 通配 POST，匹配任意路径 |

### 1. 启动 MockServer

```powershell
# ✅ 方式A：Start-Process（推荐，最简单）
Start-Process -FilePath "D:\project\{当前分支名}\artifacts\bin\OpenAI.MockServer\Release\net10.0\JoinCode.OpenAI.MockServer.exe" -ArgumentList "--port","9901"

# ✅ 方式B：ProcessStartInfo（需要捕获输出时用）
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "D:\project\{当前分支名}\artifacts\bin\OpenAI.MockServer\Release\net10.0\JoinCode.OpenAI.MockServer.exe"
$psi.Arguments = "--port 9901"
$psi.UseShellExecute = $false
[System.Diagnostics.Process]::Start($psi)

# 验证启动成功：
Invoke-RestMethod -Uri "http://localhost:9901/" -Method Get
# 期望返回：@{status=ok}

# 手动 POST 测试（模拟 jcc 发送请求）：
$body = '{"model":"gpt-4o","messages":[{"role":"user","content":"hello"}],"stream":true}'
Invoke-RestMethod -Uri "http://localhost:9901/v1/chat/completions" -Method Post -Body $body -ContentType "application/json"

# 关闭：
Invoke-RestMethod -Uri "http://localhost:9901/shutdown" -Method Get
```

**踩坑记录**

| 问题 | 原因 | 解决 |
|------|------|------|
| 端口始终绑定到配置文件默认值 | `Start-Process -ArgumentList "--port=9901"` 把等号格式当单参数传入 | 用逗号分隔：`-ArgumentList "--port","9901"` |
| MockServer 启动后立即崩溃 | `File.AppendAllText` 在 Kestrel 多线程中并发写同一文件导致 IOException | 禁止在 KestrelMockServer 中用 File.AppendAllText，只用 Console.WriteLine |
| `RedirectStandardOutput` + `ReadToEnd()` 死锁 | PowerShell 管道消费端阻塞，dotnet 进程 stdout 写入阻塞 | 用 `BeginOutputReadLine()` 异步读取，或不用重定向 |
| Console.WriteLine 在后台进程中不可见 | `UseShellExecute=false` 时输出到父进程控制台，不写文件 | 前台调试用 `& $exe --port 9901`；后台运行靠 dump 文件诊断 |
| jcc 环境变量不生效（JCC_ENDPOINT等） | `ApplyEnvOverrides` 只在 `dotEnv != null` 时调用，无 `.env/api.json` 时环境变量被跳过 | 已修复：`ApplyEnvOverrides` 移出 `if (dotEnv is not null)` 块，无论 dotEnv 是否存在都调用 |
| MockServer 流式最终 chunk 未发送 | `WriteAsync(lastChunk)` 后缺少 `FlushAsync`，`data: [DONE]` 缓冲在服务端 | 在 `BuildStreamFinalChunk` 写入后加 `await ctx.Response.Body.FlushAsync()` |
| `mcp_call --trust key=value --json` 丢参数 | `FlatSubCommandRouter.GetPositional` 启发式假设 `--xxx` 都带值，布尔标志 `--trust` 误吞 `key=value` | 已修复：`CliArgConstants.BooleanFlags` 白名单（源码生成器从 `[CliOption]` 特性自动提取）+ `IsKeyValuePair()` 双保险，布尔标志不吞值、key=value 永不被吞 |

### 2. 启动 jcc 连接 MockServer

```powershell
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "D:\project\{当前分支名}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$psi.Arguments = "--trust --await 20 -p `"echo hello`""
$psi.EnvironmentVariables["JCC_ENDPOINT"] = "http://localhost:9901"
$psi.EnvironmentVariables["OPENAI_API_KEY"] = "sk-test-1234567890"
$psi.EnvironmentVariables["JCC_VENDOR"] = "openai"
$psi.EnvironmentVariables["JCC_MODEL_ID"] = "gpt-4o"
$psi.UseShellExecute = $false
$psi.WorkingDirectory = "D:\project\{当前分支名}"
[System.Diagnostics.Process]::Start($psi)
# --await 20: 20秒超时自动关闭（超时返回1234，正常完成不受影响）
# --debuglog: 启用诊断输出（[WIRE] [STEP] [READY] 等）
```

**jcc 环境变量参数表**

| 环境变量 | 示例值 | 说明 |
|----------|--------|------|
| `JCC_ENDPOINT` | `http://localhost:9901` | API 端点（⚠️ 不要带 `/v1`，jcc 内部会自动拼接 `chat/completions`） |
| `OPENAI_API_KEY` | `sk-test-1234567890` | API 密钥（MockServer 不校验，任意值即可） |
| `JCC_VENDOR` | `openai` | LLM 供应商（openai/anthropic/deepseek/sensenova/agnes） |
| `JCC_MODEL_ID` | `gpt-4o` | 模型 ID（MockServer 不校验，任意值即可） |
| `JCC_PROTOCOL` | `responses` | LLM 协议覆盖（`openai-compatible`/`anthropic`/`responses`），不设置则用供应商配置的默认协议 |
| `JCC_SUBAGENT_MODEL` | `gpt-4o-mini` | 子代理模型全局覆盖（优先级最高，高于 SpawnOptions.Model 和 Agent 定义文件） |
| `JCC_PERMISSION_MODE` | `bypass` | 权限模式（plan/auto/ask/bypass），等价于 `--permission-mode` 参数 |
| `JCC_DEBUGLOG` | `1` | 启用调试日志（等效 `--debuglog` 参数） |

### 3. 诊断：查看 MockServer 请求记录

```powershell
# dump 目录包含每个请求的完整记录
Get-ChildItem "D:\project\{当前分支名}\tests\MockServers\MockServer.Core\dumps\OpenAI" -File | Sort-Object LastWriteTime -Descending | Select-Object -First 5 Name,LastWriteTime
```

## 替代方案

无。MockServer + jcc 联合测试是验证完整链路的唯一方式，单元测试无法替代。
