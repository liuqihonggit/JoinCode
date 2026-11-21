# TASK104 — jcc 启动自动拉起 Hub（ADR 0155 收尾）

**状态**: 已完成（2026-11-21）

## 背景

ADR 0155 规划“启动 jcc.exe 后自动拉起 jcchub.exe，一切默认”，代码中已有 `EnsureHubServerAsync`，但存在两处落地缺口：

1. **jcchub.exe 不在任何默认构建产物里**：`Hub.csproj` 只挂在 `modules/services/module.slnx`，默认全仓编译（JoinCode.slnx）与 jcc 输出目录均无彼；`EnsureHubServerAsync` 只在 `AppContext.BaseDirectory` 找 `jcchub.exe`，永远找不到 → 自动拉起是死代码，随后配置加载报“Hub 不可达”。
2. **E2E 直连 MockServer 的语义缺失**：一旦 Hub 能自动拉起，所有本地/CI MockServer E2E 会被 Hub 抢占；原 `JCC_ALLOW_DIRECT_FALLBACK=1` 只表示“Hub 不可达时回退”，不表示“显式 JCC_ENDPOINT 时跳过 Hub”。

## 实现

| 项 | 位置 | 说明 |
|----|------|------|
| Hub 纳入主解决方案 | `build/sln/JoinCode.slnx`（services 节点） | 默认全仓编译产出 `artifacts/bin/Hub/{Config}/{TFM}/jcchub.exe` |
| 开发布局发现 | `modules/apps/cli/core/commands/core/Program.cs`（`FindHubExecutable`） | 先找 jcc 同目录（发布布局），再探测 `artifacts/bin/Hub/{当前配置,Debug,Release}/{TFM}/jcchub.exe` |
| 启动工作目录 | 同上（`ProcessStartInfo.WorkingDirectory`） | 指向 jcchub.exe 所在目录，保证依赖/嵌入资源解析稳定 |
| 直连覆盖语义 | `EngineApplicationBuilder.ShouldUseDirectEndpoint` | `JCC_ENDPOINT` 显式设置且 `JCC_ALLOW_DIRECT_FALLBACK=1` 时跳过 Hub 探测、自动拉起与 Agent 注册，保证 E2E 直连 MockServer 确定命中 |
| 错误指引 | `EngineApplicationBuilder.LoadConfigAsync` | 不再要求用户手动确认 Hub；改为说明“启动 jcc 会自动拉起，失败再手动运行”，保留“后台始终可读”的修复语义 |

## 需求落地四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `FindHubExecutable` 双布局查找 + 自动 `Process.Start` | `Program.EnsureHubServerAsync`（所有非子命令启动路径） | ✅ | ✅ 真实 exe：干净状态（无 jcchub 进程）运行 `jcc -p "hub autostart acceptance"`，日志出现 `[HUB] AgentHubServer 已启动 — http://localhost:3457`、`[HUB] Agent 已注册`，`/health` 返回 `{"status":"ok","agents":0,"providers":7}` |
| `Hub.csproj` 纳入 `JoinCode.slnx` | 默认全仓编译 | ✅ | ✅ `Build-AllDomains.ps1` 通过；jcchub.exe 产出并可被 jcc 发现 |
| `ShouldUseDirectEndpoint` 直连覆盖 | `LoadConfigAsync` / `EnsureHubServerAsync` / `RegisterHubAgentAsync` | ✅ | ✅ 真实 exe：`JCC_ENDPOINT=http://localhost:18110 JCC_ALLOW_DIRECT_FALLBACK=1` 时日志输出 `[HUB] 显式 JCC_ENDPOINT + 直接回退 — 跳过 Hub 探测`，请求全部命中 MockServer（后台通知注入验收 EXIT=0） |

## 验证记录

| 验证 | 方式 | 结果 |
|------|------|------|
| 自动拉起 | 先确认 3457 无监听、无 jcchub 进程 → `jcc -p` | Hub 启动成功；`/health` 正常；jcc 走 Hub 协议加载 7 个供应商 |
| 直连隔离 | 同机存在运行中 Hub 时设置显式 `JCC_ENDPOINT` | jcc 跳过 Hub，MockServer 捕获全部 3 次请求 |
| 编译 | `dotnet build modules/apps/cli/JoinCode.csproj -c Debug` + `Build-AllDomains.ps1` | 0 警告 0 错误；全仓通过 |
| 回归 | `Hands.Shell.Tests` 528/528、`Agents.Tests` 626/626、`Brain.Context.Tests` 943/943 | 全部通过 |
| 风格门禁 | `Verify-StyleGate.py` | 0 findings |

## 说明

- Hub 仍固定端口 3457、无环境变量开关；`JCC_ALLOW_DIRECT_FALLBACK=1 + JCC_ENDPOINT` 仅作为测试/CI 的显式直连语义，不影响默认用户行为。
- 若发布包未携带 `jcchub.exe`，错误信息已明确提示自动拉起失败与手动运行方式。
