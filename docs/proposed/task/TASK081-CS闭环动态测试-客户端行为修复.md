# TASK081 — C/S 闭环动态测试 — 客户端行为修复

## 任务概述

建立"自己和自己沟通"的 C/S 闭环动态测试模式，实际启动 jcc.exe（客户端）+ MockServer（服务端），动态输入内容（不得用固定 mock），针对返回结果做修改。既要操作服务器，也要操作客户端，遇到问题两边代码都修复。

## 核心目的

修复客户端一切行为：
1. 被服务器调用工具、参数、信号收发、卡死、无响应、错误响应等各种 bug
2. 不舒服的地方归纳成 bug 而不是特性，拿不准的让用户决定，不隐瞒问题
3. 不统一的格式要统一（强迫症思维），避免用户使用时面对不同格式出现精神诧异
4. 通过多轮比较，遇到三次的不舒服就修复（抓大放小是危险的，bug 往往在放小位置）
5. 不敢确定传参值范围时用防御性编程，宁可异常日志记录也不隐藏问题，下一轮测试必须跑到这个范围
6. 返回值验收：不把空值当特性，当 bug 对待（`[]` 空数组让人困惑：是没有还是程序错误）
7. 一个角色驱动测试：客户端驱动服务端，客户端 mock 服务端数据，实现一个角色的收和发
8. 接收到异常被抛出是重点（功能没正确处理异常）
9. 服务器要 web 化展示：查看客户端发来的数据、发出去什么、工具调用 json、是否命中缓存等
10. 记录 exe 启动时间、参数时间（优化时间是目标）

## 基础设施

### 已有设施（复用）

| 设施 | 路径 | 用途 |
|------|------|------|
| jcc.exe 入口 | `modules/apps/cli/core/commands/core/Program.cs:7` | 客户端 Main |
| jcc csproj | `modules/apps/cli/JoinCode.csproj` | 客户端项目 |
| OpenAI MockServer 入口 | `test/mock/open_ai.mock_server/Program.cs:6` | 服务端 Main |
| MockServer 内核 | `test/mock/mock_server.core/KestrelMockServer.cs` | Kestrel 通用宿主 |
| MockServer Dashboard | `GET /__control__/dashboard` | web 化展示 UI |
| MockServer 交互模式 | `--interactive` 启动 + `POST /__control__/inject` 注入 | 动态注入响应（非固定脚本） |
| MockServer 控制面 | `GET /__control__/state`、`/requests`、`POST /__control__/clear` | 查看收发数据 |
| 现有 E2E 脚本 | `test/mock/scripts/run_e2e_test.ps1` | 固定脚本模式（本任务要改为动态） |
| 部署脚本 | `non_deliverables_tools/scripts/Deploy-JccDev.ps1` | 全量编译+部署 |

### 通信方式

**HTTP 模式**（本任务采用，便于 web 可视化）：
```
jcc.exe  ──HTTP──>  MockServer (Kestrel, localhost:9901)
                    ├── /v1/chat/completions (OpenAI Chat)
                    ├── /responses (OpenAI Responses API)
                    ├── /__control__/dashboard (web 可视化)
                    └── /__control__/inject (动态注入响应)
```

jcc 侧环境变量：
- `JCC_ENDPOINT=http://localhost:9901`
- `OPENAI_API_KEY=sk-test-1234567890`
- `JCC_VENDOR=openai`
- `JCC_MODEL_ID=gpt-4o`

jcc 侧启动：
- 非交互：`jcc --trust -p "输入" --await 30`
- 交互多轮：`jcc --trust --force-interactive --await 30`

## 工作流

```
┌─ 编译 jcc.exe + MockServer (Debug)
├─ 启动 MockServer (--interactive, web Dashboard)
├─ 启动 jcc.exe (动态输入)
├─ 观察 MockServer Dashboard (收发数据可视化)
├─ 发现 bug / 不舒服
├─ 修复 (客户端 or 服务端 or 两边)
├─ 编译验证
├─ 回归测试
├─ git 提交
└─ 记录到本文档"修复记录"区
```

## 任务清单

### 阶段 0：基建（动态闭环测试脚手架）

- [ ] 0.1 编译 jcc.exe + OpenAI.MockServer.exe（Debug）
- [ ] 0.2 编写动态闭环测试脚本（Python，避免 PowerShell 编码问题）
  - 启动 MockServer（--interactive 模式）
  - 启动 jcc.exe（--force-interactive 多轮）
  - 动态注入响应（POST /__control__/inject）
  - 捕获 jcc.exe stdout
  - 捕获 MockServer /__control__/requests（收发数据）
  - 记录启动时间、参数时间
- [ ] 0.3 验证脚手架可用（基础对话 round-trip）

### 阶段 1：基础对话链路验证

- [ ] 1.1 纯文本对话（无工具调用）
- [ ] 1.2 thinking/reasoning_content 分离（记忆：已修复，回归验证）
- [ ] 1.3 流式 vs 非流式输出格式一致性

### 阶段 2：工具调用链路验证

- [ ] 2.1 空参数工具调用 input 应为 `{}`（记忆：已修复，回归验证）
- [ ] 2.2 工具调用 id 解析（记忆：AllToolCalls 输出空 id 问题）
- [ ] 2.3 工具结果 content 嵌套错误检测（记忆：不能只看 result: 'Done'）
- [ ] 2.4 多工具调用顺序与并行

### 阶段 3：权限与参数校验

- [ ] 3.1 `--trust` 是否设 permission_mode=trusted（记忆：未生效问题）
- [ ] 3.2 `--permission-mode bypass` vs `--dangerously-skip-permissions`（记忆：后者无效）
- [ ] 3.3 参数校验先于权限检查的顺序问题

### 阶段 4：异常与边界

- [ ] 4.1 服务器返回异常时客户端处理
- [ ] 4.2 空数组/空对象返回值验收（不把空值当特性）
- [ ] 4.3 超时、卡死、无响应
- [ ] 4.4 防御性编程：不确定参数范围用异常日志记录

### 阶段 5：格式统一（强迫症）

- [ ] 5.1 NDJSON stream-json 格式对齐（关联 TASK080）
- [ ] 5.2 错误提示格式统一（Rust 风格报错）
- [ ] 5.3 时间戳、id、type 字段格式统一

## 修复记录

> 每次修复记录：时间 | 问题 | 根因 | 修复 | 验证 | commit

### 2026-11-05 R1 基础链路探测

#### 发现的 Bug（未修复）

| # | Bug | 证据 | 分类 |
|---|-----|------|------|
| B1 | jcc 启动 banner 显示端点与实际不一致 | 显示 `https://api.openai.com/v1` 但请求发到 `http://localhost:9901/v1`（MockServer total_requests=1） | 显示不一致 |
| B2 | jcc 启动 banner 显示模型与实际不一致 | 显示 `gpt-5.6-sol` 但请求体 `model=gpt-4o` | 显示不一致 |
| B3 | jcc 启动 banner 显示流式与实际不一致 | 显示 `流式: 否` 但请求体 `stream=true` | 显示不一致 |
| B4 | JCC_ENDPOINT 环境变量不生效 | 设 `JCC_ENDPOINT=http://localhost:9901` 但 jcc 连 `api.openai.com:443`；改 settings.json 才生效 | 环境变量 |
| B5 | MockServer interactive 注入超时 | `Interactive: waiting for inject (30s timeout)... inject timeout, falling back to script`；inject_queue_length=1 注入未消费 | 时序 |

#### B1-B3 根因定位

jcc 启动 banner 输出（stdout）：
```
供应商: openai | 模型: gpt-5.6-sol | 流式: 否 | 预设: openai
  端点: https://api.openai.com/v1 | API Key: 已配置（未验证）
```
实际请求（MockServer 捕获）：
- path: /chat/completions（发到 localhost:9901）
- model: gpt-4o
- stream: true

**根因**：banner 显示的是 settings.json 中 vendor.openai 的原始字段（model=gpt-5.6-sol, endpoint=api.openai.com/v1），而非运行时实际使用的值（被 JCC_MODEL_ID=gpt-4o 覆盖、endpoint 被 settings.json 修改覆盖）。banner 读取的是配置快照，不是最终生效的 WorkflowConfig。

#### B4 根因定位

`EnvOverrideApplier.Apply`（EnvOverrideApplier.cs:69）正确读取 JCC_ENDPOINT 并写入 settings.json vendor profile。但后续 `ApplyProviderDefinitionDefaultsAsync`（SettingsMapper.cs:113）`Endpoint = provider.Endpoint ?? newDefinition.DefaultEndpoint` 可能用 DefaultEndpoint 覆盖。需进一步调试。

#### B5 根因定位

`KestrelMockServer.cs:170-176` interactive 模式 `WaitForInjectionAsync(ctx.RequestAborted, 30s)`。注入时 waiter 已超时移除，响应入队未被消费。需更快注入（收到请求后立即注入）。

### 2026-11-05 R2 闭环链路打通

#### 修复的 Bug

| # | Bug | 根因 | 修复 | 验证 |
|---|-----|------|------|------|
| B4-fix | JCC_ENDPOINT 不生效 | 工作目录 `.jcc/settings.json`（旧格式 `provider:deepseek`）干扰配置合并 | 移动到 `.xxx/` | JCC_ENDPOINT=http://localhost:9901/v1 生效，jcc 连 MockServer |
| B5-fix | interactive 注入超时 | jcc HTTP 超时 < MockServer 30s 等待，waiter 提前完成 | 改用**预注入**策略：jcc 启动前注入，MockServer ConsumeInjection 立即返回 | inject_queue_length=0，响应被消费 |
| B7-fix | inject isStream=false | `MockServerJsonContext` 用 `SnakeCaseLower` 命名策略，camelCase `isStream` 不匹配 `is_stream` | 注入 JSON 改用 snake_case 字段名 | MockServer 日志 `stream=True`，jcc 收到流式响应 |

#### 闭环测试结果（R2）

```
jcc OUT:
  供应商: openai | 模型: gpt-4o | 流式: 否 | 预设: openai
    端点: http://localhost:9901/v1 | API Key: 已配置（未验证）
  [AI助手]
  你好！我是 JoinCode AI 助手，可以帮你完成软件工程任务。

  Total: 348ms

MockServer:
  total_requests=1, inject_queue_length=0
  Response: injected (queue), status=200, stream=True
```

#### 仍存在的 Bug（未修复）

| # | Bug | 证据 | 优先级 |
|---|-----|------|--------|
| B1 | banner 显示端点与实际不一致 | banner 显示 `https://api.openai.com/v1`（DefaultEndpoint），实际用 settings.json 的 `http://localhost:9901/v1` | 中（精神诧异） |
| B2 | banner 显示模型与实际不一致 | banner 显示 `gpt-5.6-sol`（settings.json vendor.model），实际请求 `model=gpt-4o`（JCC_MODEL_ID 覆盖） | 中 |
| B3 | banner 显示流式与实际不一致 | banner 显示 `流式: 否`，实际请求 `stream=true` | 中 |
| B8 | banner 显示"流式: 否"但 jcc 发流式请求 | `[WIRE] 流式请求体字节数=95891` 证明发的是流式请求 | 中 |

### 2026-11-05 R3 多轮闭环测试（5 轮）

#### 测试结果汇总

| 轮次 | 场景 | exit | time | reqs | AI 响应 | 结果 |
|------|------|------|------|------|---------|------|
| R1 | 基础对话（流式） | 0 | 5391ms | 1 | "你好！我是 JoinCode AI 助手..." | ✓ 成功 |
| R2 | 多句回复（流式） | 0 | 5141ms | 1 | "递归是一种函数调用自身的编程技巧..." | ✓ 成功 |
| R3 | 服务器 500 错误 | -1 | 20096ms | 2 | <空> | ✗ 无限重试超时 |
| R4 | 空响应 | 1 | 5073ms | 1 | <空> | ✗ 抛异常退出 |
| R5 | 非流式响应 | -1 | 20116ms | 2 | <空> | ✗ 降级后无响应 |

#### 新发现的 Bug

| # | Bug | 证据 | 分类 | 优先级 |
|---|-----|------|------|--------|
| B9 | 500 错误后无限重试 | `[LLM:RETRY] 失败 (尝试 1/∞)` — ∞ 表示无限重试，20 秒超时 exit=-1 | 重试逻辑 | **高** |
| B10 | 空响应抛异常退出 | `Exception: 模型返回空回复（无文本/思考/工具产出）` exit=1 | 防御性编程 | 中（行为正确但 exit=1 可能过严） |
| B11 | 非流式降级后无响应 | 流式失败→降级非流式（reqs=2），但预注入响应已被消费，第2请求超时 | 测试脚手架 | 低（脚手架问题） |
| B12 | R2 自动注入 OutputStyleExplanatory | `[系统提示: 检测到 '解释' 关键词，已自动注入]` — 用户输入被关键词检测修改 | 功能行为 | 低（可能是特性） |

#### B9 详细证据

```
[WIRE ] 流式请求体字节数=95849 | tools=35 | chat/completions
[LLM:RETRY] LLM.StreamingChatCompletion 失败 (尝试 1/∞), 2027.6962ms 后重试 | HttpRequestException: [LLM.StreamingChatCompletio
```
- jcc 收到 500 错误后重试，重试次数 `1/∞`（无限）
- 20 秒后 --await 超时，exit=-1
- 用户看到无输出、无错误提示，只有超时退出

#### B10 详细证据

```
[WIRE ] 流结束 | chunks=2, content=0, toolCalls=0
[CliSession] Exception: 模型返回空回复（无文本/思考/工具产出）。可能为请求过大超时或供应商响应异常，请重试或切换模型/供应商。
[STEP] ExecuteStep exception: InvalidOperationException: 模型返回空回复...
```
- jcc 正确检测到空回复并抛异常（防御性编程）
- 但 exit=1（错误退出），用户可能期望显示错误信息而非直接退出
- 错误信息合理，但退出码可能需要区分（空回复 vs 真正错误）

#### 时间记录

| 轮次 | jcc 启动到退出 | LLM 响应 | 备注 |
|------|---------------|---------|------|
| R1 | 5391ms | 359ms | 模块加载约 5s + 响应 359ms |
| R2 | 5141ms | ~300ms | 同上 |
| R3 | 20096ms | N/A | 超时（无限重试） |
| R4 | 5073ms | ~100ms | 快速失败 |
| R5 | 20116ms | N/A | 超时（降级后无响应） |

### 2026-11-05 B9 修复 — 500 错误无限重试

#### 根因

1. `ServiceRegistration.cs:210` `IsTransientCapacityError` 只检查 `ApiException`，但 `ResilientHttpExecutor.CheckResponseStatusAsync` 抛 `HttpRequestException`（非 ApiException），导致 429/529 无法被识别为容量错误
2. `ResilientHttpExecutor.cs:152` `maxLabel = budget.HasValue ? "∞" : ...` — 预算模式下总显示 "∞"，即使有 `TransientErrorPredicate` 限制非容量错误重试次数

#### 修复

| 文件 | 行号 | 修改 |
|------|------|------|
| `modules/llm/core/DependencyInjection/ServiceRegistration.cs` | 213-215 | `IsTransientCapacityError` 增加 `HttpRequestException` 检查：`ex is HttpRequestException http && (int?)http.StatusCode is 429 or 529` |
| `modules/abstractions/infrastructure/http/ResilientHttpExecutor.cs` | 152 | `maxLabel` 改为 `budget.HasValue && retry.TransientErrorPredicate is null ? "∞" : retry.MaxRetries.ToString()` — 有 TransientErrorPredicate 时显示 MaxRetries |

#### 验证

```
修复前: [LLM:RETRY] 失败 (尝试 1/∞), 2027ms 后重试 | HttpRequestException: HTTP 500
        20 秒超时 exit=-1，无输出

修复后: [LLM:RETRY] 失败 (尝试 1/3), 1522ms 后重试 | HttpRequestException: HTTP 500
        [LLM:RETRY] 失败 (尝试 2/3), 4913ms 后重试
        [LLM:RETRY] 失败 (尝试 3/3), 7022ms 后重试
        [LLM:RETRY] 最终失败 (尝试 4) | HttpRequestException: HTTP 500
        jcc 输出: 错误: [LLM.StreamingChatCompletion] HTTP 500: ...
        jcc 退出 (T+18942ms)，显示错误信息 + 检查建议
```

### 2026-11-05 B10 修复 — 计时摘要与错误信息粘连

#### 根因

`CliEventConsumer.OnTimingSummary`（`CliEventConsumer.cs:178`）用 `WriteRaw(summary)` 输出 `Total: 188ms` 不换行，后续异常处理 `WriteLine($"错误: {ex.Message}")` 直接接在同一行，产生 `Total: 188ms错误: ...` 粘连。

#### 修复

| 文件 | 行号 | 修改 |
|------|------|------|
| `modules/apps/cli/adapters/CliEventConsumer.cs` | 178 | `WriteRaw(summary)` → `WriteLine(summary)`，保证计时摘要独立成行 |

#### 验证

```
修复前: Total: 188ms错误: 模型返回空回复...
修复后: Total: 187ms
        错误: 模型返回空回复...
```

commit `619de6a2b`

### 2026-11-05 B13 修复 — HTTP 错误信息显示裸 JSON body

#### 根因

1. `ResilientHttpExecutor.CheckResponseStatusAsync`（`ResilientHttpExecutor.cs:256`）抛 `[{operationName}] HTTP {statusCode}: {body.Trim()}`，把整个 JSON body 拼进异常消息
2. `HttpResponseExtensions.EnsureSuccessWithBodyAsync`（`HttpResponseExtensions.cs:30`）同样把 body preview 拼进异常消息
3. 两处都不提取 OpenAI 错误格式 `{"error":{"message":"..."}}` 的 `error.message` 字段

#### 修复

| 文件 | 行号 | 修改 |
|------|------|------|
| `modules/abstractions/infrastructure/http/ResilientHttpExecutor.cs` | 256-279 | 新增 `ExtractErrorMessage` 方法，`JsonDocument.Parse` 解析 `error.message`，失败回退原始 body 前 200 字符；空 catch 改为带日志 |
| `modules/llm/core/Adapters/LLM/QueryServices/core/HttpResponseExtensions.cs` | 30-48 | 同样新增 `ExtractErrorMessage` 方法，复用相同逻辑 |

#### 验证

```
修复前: 错误: [LLM.StreamingChatCompletion] HTTP 404: {"error": {"message": "Not Found", "type": "server_error", "code": "internal_error"}}
修复后: 错误: [LLM.StreamingChatCompletion] HTTP 404: Not Found
```

commit `0af9b595d`

### 2026-11-05 B14 修复 — 429 限流不处理 Retry-After 头

#### 根因

1. `ResilientHttpExecutor.CheckResponseStatusAsync` 抛 `HttpRequestException` 时不携带 Retry-After 头信息
2. `ServiceRegistration.cs` 的 `RetryAfterExtractor` 从未被赋值实际提取函数（一直为 null）
3. MockServer `InjectedResponse` 无 `Headers` 字段，无法注入自定义响应头

#### 修复

| 文件 | 行号 | 修改 |
|------|------|------|
| `modules/abstractions/infrastructure/http/ResilientHttpExecutor.cs` | 255-282 | 新增 `ExtractRetryAfterMs` 方法，`CheckResponseStatusAsync` 提取 Retry-After 头存入 `HttpRequestException.Data["RetryAfterMs"]` |
| `modules/llm/core/DependencyInjection/ServiceRegistration.cs` | 155,218-225 | `RetryAfterExtractor = ExtractRetryAfterFromException`，从异常 Data 字典读取 Retry-After |
| `test/mock/mock_server.core/MockServerController.cs` | 33 | `InjectedResponse` 新增 `Headers` 字段（`Dictionary<string, string> = []`） |
| `test/mock/mock_server.core/KestrelMockServer.cs` | 806-810 | `HandleInjectedResponseAsync` 设置注入的自定义响应头 |

#### 验证

```
修复前: [LLM:RETRY] 失败 (尝试 1/3), 2321ms 后重试 | HTTP 429（指数退避，忽略 Retry-After 头）
修复后: [LLM:RETRY] 失败 (尝试 1/3), 1000ms 后重试 | HTTP 429（使用 Retry-After: 1 头指定的 1 秒）
```

commit `8174113b5`

### 2026-11-05 扩展场景测试结果

| 场景 | exit | 耗时 | 结果 | 备注 |
|------|------|------|------|------|
| R6 长文本 (30×重复) | 0 | 5944ms | ✓ | 分块渲染正确 |
| R7 Unicode (emoji+换行+缩进+引号) | 0 | 5324ms | ✓ | 特殊字符全部正确 |
| R8 404 错误 | 10 | 4953ms | ✓ B13 已修复 | 错误信息友好 |
| R9 429 限流 | -1 | 25161ms | ⚠️ | 重试 3 次后超时（无 Retry-After 头） |
| R10 思考链 (reasoning_content) | 0 | 5667ms | ✓ | reasoning + content 都正确显示 |
| T1 工具调用 (Read) | -1 | 25128ms | ✓ 链路通 | 工具执行成功，第二轮超时（未预注入后续响应） |

## 不舒服记录

> 三次同类不舒服即修复。记录：轮次 | 现象 | 分类 | 是否已修复

（待填充）

## 时间记录

> exe 启动时间、参数时间、round-trip 延迟

（待填充）

## 决策记录

<!-- 🤖 Auto Decision: 2026-11-05 -->
<!-- 决策: 采用 HTTP 通信 + MockServer --interactive 模式动态注入响应 -->
<!-- 原因: 用户要求"不得用固定 mock 形式"，--interactive 模式请求挂起等待 responder 注入，实现动态交互 -->
<!-- 替代方案: 命名管道通信（PipeMockServerFixture），但不利 web 可视化 -->
<!-- 验证: 待编译后验证 -->
