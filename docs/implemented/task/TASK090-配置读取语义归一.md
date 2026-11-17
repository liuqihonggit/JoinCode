# TASK090 — 配置读取语义归一 / 启动日志去噪

> 状态：**已完成**
> 起点：TASK089 把「这个键住哪个文件」收敛成一张表之后，仍有三处「同一件事两种答案」，
> 外加启动时一屏噪音（45 条假故障 + 栈帧 + 逐供应商的「未配置 API Key」）。
> 用户 2026-11-18 三条指示：(1) `model` 别再给他三个选项，帮他定一个；(2) 启动不该出现栈帧；
> (3) 缺失类告警必须「auth.json 里真有该供应商的凭据」才允许报。

---

## 一、`config_get model` 收敛成一个答案（用户说「你设计好」）

### 三个候选与取舍

| 方案 | 读什么 | 问题 |
|---|---|---|
| A 读 `current.model`（覆盖值） | 覆盖值 | **运行时根本不读这个字段** —— 写进去也白写，是「第二个真相源」 |
| B 读 `vendor.{profile}.model`（生效值） | 运行时真正用的 | 若 `config_set` 仍写 `current.model`，就成了「写一个、读另一个」 |
| C 两个都返回 | —— | 违反「一个键一个答案」，模型每次还得自己判断该信哪个 |

**取 B，并把写也改到同一个字段。** 依据不是偏好，而是代码事实：

```csharp
// EngineApplicationBuilder.cs:275 — 运行时只认这一个
var configuredModel = settings?.GetActiveProfile()?.Model;   // → vendor.{current.profile}.model
```

所以「生效模型」就只有这一个来源。`config_set model X` 写 `vendor.{当前供应商}.model` 之后，
**读到的一定是 X**，下次启动也用 X —— 写读对称，不需要「覆盖值」这个概念。
`current.model` 这个字段在 C# 侧从未被消费，本轮也没有引入它。

### 顺带修掉挡在路上的两道墙

1. **静态白名单把真实模型全判死**：`SupportedSettings["model"].GetOptions` 返回写死的
   `sonnet / opus / haiku / best`（从 TS 基线抄来的四个占位），于是
   `config_set model deepseek-v3` 直接 `Invalid value`，等于 `config_set model` 不可用。
   删掉 `GetOptions`：候选模型属于「已配置供应商的 models 列表」，应该问 `model_search`，
   不该在设置表里硬编码一份。
2. **写失败静默报成功**：`ConfigurationService.SetAsync` 无论磁盘写成功都 `return true`。
   现在写失败返回 false，`/config set` 与 `config_set` 都会明确报错（见第三节能看到的真机输出）。

---

## 二、嵌套键按路径读写 —— `SettingPathMap`

`ConfigSetting.Path` 这个字段早就存在（`permissions.defaultMode → ["permissions","defaultMode"]`），
但**零消费方**：`ConfigLoader` 用的是源码生成的 `CurrentSettings.GetSettingByKey`，只认扁平键名。
结果是 `config_set permissions.defaultMode plan` 写进扁平键 `current.permissions.defaultMode` 之外的
位置、运行时 `PermissionChecker` 读 `settings.Current.Permissions.DefaultMode` 仍是旧值 —— 改了不生效。

新增契约层 `SettingPathMap`（与 `SettingSourceMap` 同目录、同思路）：**键 → 文件内 JSON 路径**，
`{profile}` 为当期供应商占位。

```csharp
["model"]                   = ["vendor", "{profile}", "model"],   // 运行时真正读的字段
["permissions.defaultMode"] = ["current", "permissions", "defaultMode"],
```

- `ConfigLoader` 读侧：路径键走 `JsonNode` 定点取值；写侧 `ApplyNestedSetting` 定点改那一个字段，
  中间层不存在就创建，值为 null 就删字段。
- **只用 `JsonNode`，不做强类型往返** —— 往返会把 `autoFetchModels` 等本类不认识的字段丢掉。
- `ConfigSetting.Path` 字段删除，`SupportedSettings.GetPath` 改为委托 `SettingPathMap`：
  同一件事只能有一处声明（与 TASK089 删 `ConfigSetting.Source` 同一条纪律）。
- `{profile}` 解析不出来（`settings.json` 里没有 `current.profile`）时**抛错而不是糊过去**：
  错误消息直接说「先选供应商再写该字段」，工具诊断也会带上目标位置。

---

## 三、启动日志：没有栈帧，也没有按供应商刷屏的告警

### 1. 栈帧（用户第 2 条）

三处会把 `at JoinCode.App.Builder.x()` 送到用户眼前，全部收紧：

| 位置 | 原来 | 现在 |
|---|---|---|
| `ErrorConsole.Fatal`（控制台） | 有异常就打印完整堆栈 | 只打「类型: 消息」，堆栈仅在 `JCC_DEBUGLOG=1` |
| `ConsoleActorLoggerProvider`（日志行） | 任何带异常的日志都追加堆栈 | `ex.Message` 一行；堆栈仅调试日志 |
| `WriteErrorLogAsync`（`jcc_error.log`） | 全量堆栈写盘 | 类型 + 消息 + **一行 `origin:`**（首个带文件信息的栈帧） |

`jcc_error.log` 会回显给用户（「详细日志: ~\.jcc\runtime\jcc_error.log」），写进去的每一行
用户都会当成「程序崩了」。现在默认是这样：

```
[2026-11-18 07:03:01] System.InvalidOperationException: Hub 服务不可达，...
  origin: JoinCode.App.Builder.EngineApplicationBuilder.LoadConfigAsync(...) (EngineApplicationBuilder.cs:line 155)
  （需要完整堆栈：设置 JCC_DEBUGLOG=1 后复现，或查看 ~/.jcc/runtime/crash-dumps）
```

完整堆栈另有归处：崩溃快照目录（`crash-dumps`）。

### 2. 45 条假故障：「依赖服务未注册」其实是退出竞态

启动时刷出 45 行 `✗ FileToolHandlers: 依赖服务未注册`。给生成器临时加上异常消息后真相是：

```
System.ObjectDisposedException: Cannot access a disposed object.
  Object name: 'IServiceProvider'.
   at ...GeneratedToolHandlerRegistration_...RegisterFileToolHandlersToolsAsync(...)
```

插件加载是**后台跑**的（`McpPluginBootstrapHandle.Run`，`McpInitPlugin` 会调
`InitializeAsync`），`-p` 单次调用很快就退出、宿主 Dispose 与 16 个并行注册批次赛跑，
赛输的批次拿到 `ObjectDisposedException`；而 `ObjectDisposedException : InvalidOperationException`，
被同一个 `catch` 统一报成「依赖服务未注册」。用户看到的是 45 个不存在的配置问题。

生成器两处改动：

1. 注册前先看 `cancellationToken.IsCancellationRequested`，宿主已在关闭就不再解析 DI；
2. `catch` 里先判 `__ex is ObjectDisposedException || ct.IsCancellationRequested` → **Debug**「宿主已开始关闭，跳过注册」，
   真缺依赖（`InvalidOperationException`）才保留 Warning。

### 3. 缺失类告警按 auth.json 判定（用户第 3 条）

`settings.json` 出厂带五六个供应商预设，用户通常只启用其中一个。原来每个没配 Key 的都发一条
`[ModelListFetcher] 跳过 sensenova：未配置 API Key`，一屏五条，把真正的错误埋掉。

口径改成：**只有 auth.json（或 `apiKeyEnvVar` 环境变量）里真有该供应商的凭据，才允许报它下游的缺失**；
没有就按「用户没选它」静默跳过（Debug）。401/403 属于「配了但用不了」，保留 Warning 并写明原因。
`AgentHubServer` 的同类循环用同一口径。

同一轮把另外三条「每次启动都喊、但都不是故障」的降到 Information / Debug：

- `未找到 PowerShell(pwsh)，回退到 powershell.exe`（大多数机器都没装 pwsh，回退可用）
- `未找到 Python，回退到 python3`
- `[HUB] jcchub.exe 未找到` / `[HUB] Agent 注册失败`（Hub 不可达由上层在真需要时报错并给指引）

---

## 四、真机验收

### 1. 启动输出从 126 行降到 10 行

```
$ jcc -p "hi"        # 无 Hub、无 API Key 的裸机

  ✖ API 错误  未配置 API Key，LLM 调用将失败
  配置方法: ...
warn: Core.Configuration.ModelFetch.ModelListFetcher[0]
      [ModelListFetcher] deepseek 拒绝访问（401）：凭据无效或已过期，本次不拉取模型列表
```

消失的：45 条「依赖服务未注册」、5 条「跳过 X：未配置 API Key」、pwsh/python 回退各 1 条、2 条 `[HUB]`、
以及 `jcc_error.log` 里的 7 行堆栈。

### 2. model 读写对称（`JCC_APP_DATA_FOLDER` 隔离）

```
$ cat settings.json
{"current":{"profile":"p1"}, "vendor":{"p1":{"model":"m1"}}}

$ jcc tool call system config_get --setting model
{"ok":true,"data":"model = m1",...}          ← 读的是 vendor.p1.model

$ jcc tool call system config_set --setting model --value m2
{"ok":true,"data":"Set model to m2",...}

$ jcc tool call system config_get --setting model
{"ok":true,"data":"model = m2",...}          ← 同字段读回，运行时下次启动用 m2

$ jcc slash_call /config '{"action":"get","key":"model"}'
model = m2                                    ← 斜杠命令与工具同一答案
```

### 3. 嵌套键（原先写不进运行时读的位置）

```
$ jcc tool call system config_set --setting permissions.defaultMode --value plan
{"ok":true,"data":"Set permissions.defaultMode to plan",...}
$ jcc tool call system config_get --setting permissions.defaultMode
{"ok":true,"data":"permissions.defaultMode = plan",...}

$ cat settings.json
{ "current": { "profile":"p1", "permissions": { "defaultMode": "plan" } },
  "vendor":  { "p1": { "model": "m2" } },
  "autoFetchModels": false }                  ← 未知字段原样保留
```

`PermissionChecker` 读的就是 `settings.Current.Permissions.DefaultMode`，所以这次真的生效。

### 4. 没有 `current.profile` 时不再假成功

```
$ jcc tool call system config_set --setting model --value m9
{"ok":true,"data":{"content":[...{"type":"execution_failed",
  "message":"Failed to set model
             [诊断] 设置项: model
             [诊断] 尝试写入的值: \"m9\"
             [诊断] 目标位置: settings.json → vendor.{profile}.model（{profile} 取自 current.profile）",
  "suggestions":["检查配置文件权限","先选择供应商（/config set profile <名称> 或 /init）再写该字段"]}],"isError":true}}
$ cat settings.json   # 文件未被改动
```

### 5. 回归

| 项 | 结果 |
|---|---|
| `Hands.ToolHandlers.Tests`（Config） | 5 通过 / 0 失败 |
| `Guard.Config.Tests` | 1304~1305 通过 / 0 失败（见下方已知抖动） |
| `Composition.Tests`（Config） | 见提交说明 |
| `Hub.csproj` | 编译通过 |

**已知抖动**：`AnthropicCompatibleProviderDefinitionTests` 有三个用例直接改进程环境变量
`DEEPSEEK_API_KEY`，全量跑时与并行类互相串扰，偶尔红一个；单独 `--filter FullyQualifiedName~ResolveApiKeyFromEnv`
跑 6/6 通过。与本轮改动无关（未触碰 provider definition），但值得单开一轮把它们改成集合串行或注入环境。

---

## 五、顺带发现、本轮不修

1. **`ConfigToolPrompt` 零调用方** —— 生成「可用设置项 + 模型选项」提示词的那段代码没有消费者。
   原先 `model` 的四个占位选项只影响校验，不影响提示词。要不要接回、接到哪，属于提示词设计，另开。
2. **`SupportedSettings`（15 键）与 `ConfigKey` 枚举（17 键）仍是两套清单** —— 未合并。
3. **`/theme` 写盘失败仍只 warn 不报错**（`ThemeCommand.SetThemeAsync`）—— 键是扁平的、不涉及路径解析，
   IO 失败极罕见，本轮未动，避免和「model 语义」混在一个提交里。
