# TASK141 — jcc gh run wait 单次请求超时崩溃

> 日期：2026-11-27
> 来源：PR #755 CI 守候期间，`jcc gh run wait 45914716008` 在数次正常轮询后退出 127；CI 最终全绿，崩溃来自工具自身。

## 根因证据

- `.xxx/logs/task140_pr755_ci_wait.txt`：`GitHubApiClient.SendAsync` 的 `HttpClient` 在 100 秒超时后抛出 `TaskCanceledException`。
- `modules/tools/github_client/GitHubApiClient.cs`：把 `OperationCanceledException` 全部当成调用方取消重抛，未区分 `HttpClient.Timeout`。
- `modules/tools/gh/run_meta/GitHubRunPoller.cs`：`PollUntilAsync` 直接等待 `pollOnce(ct)`，单次请求超时没有重试；总超时只在结果返回后检查，不能中断在途请求；瞬态失败还会把上一轮成功状态覆盖成空。
- `modules/tools/gh/handlers/GitHubToolHandlers.Run.cs`：run_id 前置解析发生在轮询器之前，使用调用方原始令牌，不受 `timeout_seconds` 约束；且 StatusCode=0 会被误报为“run 不存在”。
- `modules/tools/github_client/GitHubApiClient.cs`：`ResponseHeadersRead` 后读取 body 期间断连/截断不在原 try 内，仍可能穿出进程。
- 异常最终穿过 `GhRunWaitCoreInnerAsync`，导致卫星进程非零退出。

## 修复边界

- 客户端边界区分取消与超时：调用方取消继续抛出；请求或响应体读取阶段的传输失败返回 `StatusCode=0` 的网络失败响应。
- 轮询器把无状态码传输失败视为可重试事件，保留最近一次成功状态并按既有退避继续；HTTP 4xx/5xx 仍按原语义终止。
- 总等待期限使用 `CancellationTokenSource` 单调计时，通过链接令牌中断在途请求；不再依赖墙钟 deadline。
- run wait 的 run_id 前置解析也纳入同一个总期限，剩余时间传给轮询器；前置网络失败透出真实网络错误，不再误报不存在。
- 首个请求从未成功时，正常返回 Timeout 摘要；后期瞬态失败时保留上次状态并在摘要附最后错误。
- 不放宽策略、不引入系统 gh 回退、不改变公开工具参数。

## 执行清单

- [x] 保存真实崩溃堆栈并定位根因。
- [x] 补单次超时、网络失败、首次无数据超时的单元红灯。
- [x] 补在途总超时、run_id 前置解析超时、在途调用方取消边界回归。
- [x] 修复客户端取消/超时边界与轮询引擎并编译。
- [x] 跑 Gh.Tests 与 GitHubClient.Tests 全量回归。
- [x] 重建主宿主并部署卫星产物。
- [x] 用真实 jcc gh 验收已完成 run 与瞬态网络故障重试。
- [x] 更新验收表与验证记录。
- [ ] 提交、推送并创建 squash auto-merge PR，守候 CI。

## 验证记录

- 单元红灯（修复前）：
  - 单次请求超时直接抛 `TaskCanceledException`。
  - 网络失败被当成终态 Error。
  - 首次请求无数据超时时无法返回 Timeout。
  - 总期限不能中断在途请求，run_id 前置解析不受总期限约束。
  - HttpClient 超时在客户端边界被误判为调用方取消。
- 复审补强（修复后新增回归并用旧逻辑推理确认缺口）：瞬态失败覆盖上次状态、响应体读取断连穿出进程、前置网络失败误报 run 不存在。
- `GitHubRunWaitTests`：31/31。
- `Gh.Tests` 全量：812/812。
- `GitHubClient.Tests` 全量：54/54。
- 主宿主 Debug 编译：0 警告、0 错误。
- 官方部署脚本已复制主项目/gh/rg/13 组工具节点；仍被无关的 `file/read` `Auth.Missing` 探针拦住，本任务未放宽该授权。
- 真实 `jcc gh run wait 45919447398 --timeout_seconds 10`：返回 `Run ... 已完成: success`，轮询 1 次。
- 真实瞬态故障（`.xxx/logs/task141_real_stub.txt`）：本地 HTTP 桩第 1 次返回 in_progress，第 2-5 次连接重置以超过 HttpClient 内建重试，第 6 次返回 completed；真实 `jcc gh run wait` 第 2 次轮询恢复成功，退出 0，进度输出单行包含网络失败原因与引导。
- 额外回归：瞬态失败后超时仍保留 `in_progress` 状态与 body，并附最后网络错误；401 保持单次终态；响应体 `IOException` 走重试；run_id 解析 StatusCode=0 显示真实网络错误。

## 验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| HttpClient 请求/响应体超时映射网络失败 | `GitHubApiClient.SendAsync`、`ReadBodyOrFailureAsync` | ✅ | ✅ 单元 54/54，真实桩连接失败可诊断 |
| 传输失败重试与总期限中断 | `GitHubRunPoller.PollUntilAsync` | ✅ | ✅ 单元 31/31，真实桩第 2 次轮询恢复 |
| 瞬态失败保留上次状态与最后错误 | run wait Timeout 摘要 | ✅ | ✅ 状态保留回归 + 真实失败进度 |
| run_id 前置解析纳入总期限与真实错误透出 | `GhRunWaitAsync`、`ResolveRunIdAsync` | ✅ | ✅ 前置超时/网络失败测试 + 真实进程 |
| 无数据超时安全返回 | run / PR / fail-fast 三个包装结果 | ✅ | ✅ 首次无数据 Timeout 回归 |
| 真实 jcc gh 调用 | `jcc-gh.exe` | ✅ | ✅ 已完成 run 与瞬态网络故障均实跑 |

<!-- Auto Decision: 2026-11-27 -->
<!-- 决策: 在 GitHubApiClient 边界区分调用方取消与传输失败（含 body 读取），轮询器用 linked CTS 单调总期限并只重试 StatusCode=0；run_id 前置解析共享期限且透出真实网络错误；瞬态失败不覆盖上次成功状态。 -->
<!-- 原因: 只在命令层捕获异常会保留“超时被误判为取消”的根因，且前置请求/响应体读取仍可崩溃或误诊；分层修复让 gh 等待链路的取消、重试、诊断和状态语义一致。 -->
<!-- 替代方案: 仅给 run wait 包 try-catch；或改用系统 gh 回退；或缩短 HttpClient.Timeout。前两者违背根因/用户约束，第三者会误伤大响应下载。 -->
<!-- 验证: 核心 6 类红灯复现后转绿，复审补充 3 类回归；Gh.Tests 812/812、GitHubClient.Tests 54/54；真实 jcc gh 已完成 run 成功，连接重置桩在第 2 次轮询恢复并退出 0。 -->
