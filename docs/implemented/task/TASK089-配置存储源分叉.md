# TASK089 — 配置存储源分叉：/theme 与 config 工具读的不是同一份文件

> 状态：**已完成**（2026-11-18，方案 B「统一到 global.json，对齐基准」；真机三路径交叉验证通过）
> 起点：TASK086~088 连续三轮修复的都是同一根因 —— **同一语义在两条路径上宽严不一**。
> 本轮在同一根因下找到第 4 例，但这次的「两条路径」分属不同模块，且修复涉及用户数据迁移。

**一句话结论**：「这个键住哪个文件」原本有 4 份判断（斜杠命令、GUI、配置工具、下沉 exe 各一份）。
收敛成契约层 `SettingSourceMap` 一张表、并在 `ConfigurationService` 内部做唯一一次决策后，
三条路径对同一键给同一答案；旧 `settings.json` 里的 global 键由一次性幂等迁移搬进 `global.json`。

---

## 一、问题现象

同一批设置键，在 C# 侧住在**两个不同的文件**里，且每条路径只认自己那个：

| 路径 | 代码 | 读 | 写 |
|---|---|---|---|
| `/theme`（CLI）、`/config`（CLI） | `ThemeCommand` / `ConfigCommand` → `GetAsync(key, ct)` / `SetAsync(key, v, ct)` | `settings.json` | `settings.json` |
| GUI（native_gui） | `PlaceholderChatSession` → `GetAsync(ConfigKeyEnumConstants.Theme, ct)` | `settings.json` | `settings.json` |
| `config_get` / `config_set`（LLM 工具） | `ConfigToolHandlers` → `GetAsync(key, source)` | 由 `SupportedSettings.Source` 决定 | 同左 |

而 `SupportedSettings`（`modules/cognition/hands_core/tool_handlers/dev_tools/handlers/SupportedSettings.cs`）
把 **15 个键中的 10 个**标成 `Source = "global"`：

```
theme / editorMode / debuglog / preferredNotifChannel / autoCompactEnabled
fileCheckpointingEnabled / showTurnDuration / terminalProgressBarEnabled
todoFeatureEnabled / teammateMode
```

这 10 个键**全部**是 `settings.json` 里 `current` 分支的字段（`SettingsJson.CurrentSettings`）——
即它们的**真实存储位置是 settings.json**，但工具侧被告知在 global.json。

---

## 二、真机证据

### 1. 工具写 global、UI 读 settings（真机跑出来的）

```bash
$ JCC_APP_DATA_FOLDER=/tmp/jcc_cfg_real jcc tool call system config_set --setting theme --value dark
{"ok":true,"data":"Set theme to dark"}

$ cat /tmp/jcc_cfg_real/global.json
{
  "theme": "dark"
}          ← 值落在 global.json

$ grep -A3 '"current"' /tmp/jcc_cfg_real/settings.json
（空）        ← settings.json 的 current.theme 仍是空
```

后果：**LLM 用 `config_set theme` 设的主题，用户在 UI 里看不到**（UI 读 settings.json）。
反过来用户在 `/theme` 选的主题，LLM 用 `config_get theme` 读不到。

### 2. 分叉探针（冷缓存）

新增 `non_deliverables_tools/config_source_probe/`：

```
[PROBE] global.json   = {"theme":"from-global"}
[PROBE] settings.json = {"current":{"theme":"from-settings"}}
[PROBE] GetAsync(key) [无 source，等价 /config]        = from-settings
[PROBE] GetAsync(key, GlobalConfig) [等价 config_get] = from-global
[PROBE] 结论：分叉 —— 两条路径对同一 key 给出不同答案
EXIT=1
```

**坑（必须记住）**：`ConfigurationService` 的内存缓存按 key 存、**不区分存储源**。
同一实例第二次读会命中缓存而根本不碰磁盘 —— **缓存会掩盖分叉**。
探针第一版就因此误判为「一致」，必须用**两个独立实例**（冷缓存）分别测才暴露真相。

---

## 三、根因（对照基准侧）

基准（`D:/project/claude-code-rust/claude-code-rev-main`）侧**三方完全一致**，主题统一在 global：

- `src/tools/ConfigTool/supportedSettings.ts:31` → `theme: { source: 'global', ... }`
- `src/components/design-system/ThemeProvider.tsx:35` → `defaultInitialTheme()` 读 `getGlobalConfig().theme`
- `src/components/design-system/ThemeProvider.tsx:38` → `defaultSaveTheme()` 写 `saveGlobalConfig(...)`
- `src/commands/theme/theme.tsx` → 走 `useTheme()` / `ThemePicker`，最终落到上面那对读写

所以基准的布局是：**`~/.claude.json`（= C# 的 `~/.jcc/global.json`）存主题**。

C# 侧的偏离在于：`SupportedSettings` 照抄了基准的 `source` 归属（global），
但 C# 侧的 `/theme`、GUI、引擎启动全部读写 `settings.json`。
**结果是 `global.json` 在 C# 侧成了「只有 config 工具在用的孤儿存储」** ——
全仓唯一读者就是 `ConfigurationService` 的 source 分流分支（`ConfigLoader.cs:448`）。

这解释了缺陷的形状：不是「工具读错文件」，而是**存储布局在 C# 侧只完成了一半**。

---

## 四、候选方案与选定结果（用户选定 **B**）

### 方案 A：统一到 settings.json（改动小，不动用户现有数据）

改 `SupportedSettings` 的 10 个 `Source = "global"` → `"settings"`。

- `/theme`、GUI、CLI `/config` 完全不动
- `config` 工具改为读写 settings.json → 与 UI 一致
- 代价：**偏离基准的存储布局**（基准主题在 global.json）
- 用户影响：已经误写进 global.json 的值需要一次性搬回（可顺手做，也可不管）
- 副作用：`global.json` 将变成完全无生产读者的空存储 —— 要么归档，要么后续留给别的用途

### 方案 B：统一到 global.json（对齐基准，需迁移）✅ **已采用**

改 `/theme` 命令、GUI、引擎启动，全部改读写 global.json。

- 与基准布局一致
- 代价：改动面大（CLI + native_gui + 引擎三处）
- 用户影响：**已存在于 settings.json 的 current.theme 等 10 个键会失效**，必须配迁移机制
  —— 这正好接上清单里另一条在案待办 **TASK071-A4（设置迁移机制）**，A4 从「基准有、C# 无」变成「C# 自己也需要」

### 方案 C：不做统一，只加「读双源 + 写单一源」的兼容层

工具侧读时先查 global 再回落 settings，写仍写自己那侧。

- 改动最小，不破坏任何现有数据
- 代价：**违反本项目「唯一数据源」主线** —— 同一个键在两个文件里都可能有值，
  「谁是权威」仍是隐式约定，下一轮还会出同样的分叉
- 与前三轮修复的原则冲突（那三轮都是「消除第二套实现」而不是「兼容两套」）

---

## 五、判定依据（AGENTS.md 基准对比四问）

1. **专家隐性实践**：配置存储迁移的标准做法是「先定权威源，再写一次性迁移，最后删旧读写路径」。
   长期兼容双源是反模式 —— 它把「谁是权威」推迟到运行时，代价是每个消费方都要做回落逻辑。
2. **原始素材清单**：基准的 `src/migrations/*`（11 个脚本）+ `src/main.tsx:326` 的
   `CURRENT_MIGRATION_VERSION = 11` 迁移框架 —— 这是基准对「配置键搬家」的完整答案。
3. **具体方法与工具**：对方案 B，迁移按基准同构实现：版本号 + 幂等迁移函数。
4. **独立验证来源**：本任务的真机探针（`config_source_probe`，退出码即结论）
   + `/config` 与 `config_get` 两条路径对同一键的真机比对。

---

## 六、尚未确认的部分（不影响方案选择）

- `model` 键：`SupportedSettings` 标 `settings`，但 `CurrentSettings` 里**没有** `model` 字段
  （它在 `vendor.{provider}.model`）。这个键的读取路径可能另有问题，本轮未深查。
- `SupportedSettings` 与 `ConfigKey` 枚举是**两套并行的键清单**（前者 15 个，后者 17 个），
  本身也是「同一语义两个实现」的候选，但本轮聚焦存储源分叉，未合并。

---

## 七、实现（4 处改动，全部围绕「让决策只剩一处」）

### 1. 契约层 `SettingSourceMap` — 键 → 存储源唯一表

新增 `modules/abstractions/abstractions.primitives.configuration/src/settings/SettingSourceMap.cs`：

```csharp
public static SettingSource SourceOf(string key)   // 表外键一律 UserSettings
public static bool IsDeclared(string key)
public static IReadOnlyDictionary<string, SettingSource> All
```

同时**删掉** `ConfigSetting.Source` 字段 —— 它让每个消费方都能自己再判断一次。
`SupportedSettings` 只保留「支持吗、类型、选项、校验」，存储源改为委托 `SourceOf`。
放在契约层而不是 `hands_core`：工具 exe 受依赖墙只能引 `Abstractions.Primitives.Tools/Configuration`，
规则留在上层就必然再出现第二套（与 TASK087 参数名归一化同一条理由）。

### 2. 决策收进 `ConfigurationService` —— 无 source 重载也按表分流

```csharp
GetAsync(key, ct)    => GetAsync(key, SettingSourceMap.SourceOf(key), ct);
SetAsync(key, v, ct) => SetAsync(key, v, SettingSourceMap.SourceOf(key), null, ct);
RemoveAsync(key, ct) => RemoveAsync(key, SettingSourceMap.SourceOf(key), ct);
```

调用点（`/theme`、`/config`、GUI、引擎）因此不需要也不允许各自判断源 —— **漏改任何一处都自动正确**。
`RemoveAsync` 原先硬编码删 `settings.json`，删 global 键等于空转；现在删的源与写的源一致。
内存缓存键由裸 key 改为 `{source}:{key}` —— 不改的话「先读 A 源再读 B 源」会拿到 A 的值，把分叉掩盖成一致。

### 3. 一次性幂等迁移 — `SettingsSourceMigration`

`modules/guard/guard.configuration/core/loading/core/SettingsSourceMigration.cs`：

- 遍历 `SettingSourceMap.All` 中 `GlobalConfig` 的键，把 `settings.json` `current` 下**有值**的那些搬进 `global.json`，再从 `settings.json` 删掉；
- **先写 global 后删 settings**：中途崩溃只会留下「两边都有」，下一轮走「保留 global、删 settings」分支收敛；
- **幂等靠结构不靠版本号**：搬完就无项可搬，重复执行零写入（探针断言两次读后文件字节不变）；
- **用 `JsonNode` 而不是强类型 `SettingsJson` 往返**：迁移只应动被搬的那几个键，强类型往返会丢掉
  `autoFetchModels` / `update` 等本类不认识的字段；
- 两边都有值时**保留 global 并打 Warning 日志**（global 是今后唯一权威源），不静默丢弃；
- 触发点：`ConfigurationService` 首次读写前跑一次（`lock` + 共享 `Task`，不用 `SemaphoreSlim` ——
  单例持有可释放类型会触发 JCC9301）。放服务内部而不是三个宿主入口，是因为 CLI / GUI / `jcc tool call`
  各自解析同一个单例，挂入口就要写三遍。

### 4. `config_get`/`config_list` 从 exe 退回宿主（本轮新发现的分叉）

真机验证时发现第三条「两条路径」：`jcc tool call system config_get` **根本没走宿主**，
命中 `modules/tools/cli_nodes/groups/system/private/SystemConfigCommands.cs` 里的下沉实现，而它自带：

- 一份**自己的** supported 键清单（漏掉 `editorMode` 等 12 个键，又多出 `thinkingEnabled`/`telemetry`/
  `maxTokens`/`temperature` 4 个 `SupportedSettings` 里没有的键）；
- 一条**自己的**存储规则：`settings.json` 找不到就递归找 `global.json`；
- 一条**自己的**路径规则：`Environment.SpecialFolder.UserProfile` + `.jcc`，完全不认 `JCC_APP_DATA_FOLDER`。

而 `config_set` 是 `mode: host`（走 `ConfigToolHandlers` + `SettingSourceMap`）。
于是「写全在 global、读全在 exe」—— 这正是 `config_set theme` 成功、`config_get theme` 报未设置的原因。

修法：把 `config_get` / `config_list` 的 `mode` 改回 `host`（即从 `SystemCommands.Handlers` 移除这两个
entry，重跑生成器把 node.yaml 的 mode 从 `local` 翻成 `host`）。
判定依据仍是本项目既有标准 —— **「同一决策是否会出现两个实现」：键清单与存储源映射都是宿主侧知识，
零依赖 exe 复制一份只会分叉出第二个真相源，所以不下沉。**

副作用（已核对）：本地工具数 217 → 215，宿主 245 → 247，**总数 635 不变**；`system` 组 local 24 → 22。

---

## 八、真机验收（同一台机、同一个 `JCC_APP_DATA_FOLDER`）

### 1. 斜杠命令 ↔ 工具交叉读写（关键证据）

```bash
# 目录里放旧布局：settings.json → current.theme = dark-daltonized
$ jcc slash_call /config '{"action":"get","key":"theme"}'
theme = dark-daltonized            ← 读得到，且顺手完成迁移
$ cat global.json
{ "theme": "dark-daltonized" }     ← 已搬到 global
$ cat settings.json
{ "current": { "language": "zh" } } ← settings 里已无 theme

$ jcc slash_call /theme '{"theme":"light"}'
主题已设置为：浅色模式
$ jcc tool call system config_get --setting theme
{"ok":true,"data":"theme = light",...}      ← 工具读到斜杠命令写的值

$ jcc tool call system config_set --setting theme --value dark
{"ok":true,"data":"Set theme to dark",...}
$ jcc slash_call /config '{"action":"get","key":"theme"}'
theme = dark                                ← 斜杠命令读回工具写的值
```

### 2. GUI 路径（`non_deliverables_tools/gui_session_probe`，已扩展为带主题读写断言）

```
[PROBE] gui 会话建立成功，服务提供程序: ServiceProvider
[PROBE] GUI 读 theme = dark-ansi            ← 旧布局里的值，迁移后读到
[PROBE] GUI 主题读写与 global.json 一致      ← 写 light-daltonized 落在 global.json
（settings.json 里不再出现 "theme"）
```

### 3. 迁移探针（`non_deliverables_tools/config_source_probe`，退出码即结论）

```
[场景 1] 旧布局：theme 只在 settings.json     … 7 项断言全 OK（含「再跑一次零写入」幂等）
[场景 2] 冲突：global 与 settings 都有 theme  … 2 项 OK（保留 global、清掉 settings）
[场景 3] 布尔值与嵌套键                       … 3 项 OK（布尔仍是布尔，嵌套结构不被动）
[PROBE] 结论：三条路径 + 迁移全部一致   EXIT=0
```

### 4. 回归与门禁

| 项 | 结果 |
|---|---|
| `Guard.Config.Tests` | 1305 通过 / 0 失败 |
| `Composition.Tests`（含 `/config` `/theme` 命令测试） | 1073 通过 / 0 失败 |
| `Hands.ToolHandlers.Tests`（Config 相关） | 5 通过 / 0 失败 |
| `Verify-ToolNodes.ps1`（声明 vs 现实） | 「声明与现实一致」，13 组 / 635 工具 |
| `jcc-tool-system.exe -h` / 直调 `config_get` | 帮助列出 72 个；直调明确报「无本地实现且需宿主」并给修复方式 |

---

## 九、过程中踩到的坑

- **探针必须冷缓存、独立实例**：缓存若按裸 key 存，同一实例第二次读不碰磁盘，会把分叉掩盖成「一致」。
  探针第一版就因此误判；现已把缓存键改成含 source，并在探针里每个场景新建实例。
- **`AppDataConstants.Paths` 在进程启动时由环境变量解析一次**：探针要在同一进程切多个场景，
  必须显式 `AppDataConstants.Paths = AppDataPaths.CreateForTest(folder)`。
- **Git Bash 会改写 `/config` 这类参数**：真机跑 `jcc slash_call /config` 必须 `MSYS_NO_PATHCONV=1`，
  否则命令名被替换成本机 PortableGit 路径。
- **`slash_call` 的 JSON 必须按命令自己的 schema 写**（`{"action":"get","key":"theme"}`），
  写成 `{"args":"get theme"}` 会被当成无参数、退化成 `/config list`。
- **`.cs` 文件内禁止 `using`（JCC1005）、禁止 `new JsonSerializerOptions{}`（JCC1012）、
  禁止绕过 `IFileSystem` 直接 `File.*`（JCC9001）**：新增档案与探针都按这三条改写过一遍。

---

## 十、遗留缺口（已确认存在，本轮不修，避免混进同一提交）

1. ~~**`model` 键读不到**~~ **已修（TASK090）**：`model` 的真身是 `vendor.{profile}.model`，
   现由契约层 `SettingPathMap` 声明路径、`ConfigLoader` 按路径读写，读写落在同一个字段。
   详见 [TASK090](TASK090-配置读取语义归一.md)。
2. ~~**嵌套键读取不支持**~~ **已修（TASK090）**：`ConfigSetting.Path` 是死字段（零消费方），
   已删除并改为 `SettingPathMap`；`ConfigLoader` 读写都按路径走 `JsonNode` 定点操作。
3. ~~**`/config list`（斜杠命令）只显示本进程读过的键**~~ **已修**：`GetAllAsync` 原本只回内存缓存，
   新进程里恒为空。现改为逐个已声明键按 `SettingSourceMap` 的源读盘（与 `config_get` 同一条路径），
   真机 `/config list` 已正确列出 `editorMode`/`language`/`theme` 及其来源。
4. **`SupportedSettings`（15 键）与 `ConfigKey` 枚举（17 键）仍是两套并行清单**，未合并。

剩余 1 条：`SupportedSettings`（15 键）与 `ConfigKey` 枚举（17 键）仍是两套并行清单，未合并。
`model` 语义与嵌套键已在 TASK090 一并解决（语义取「生效值」，读写同字段）。
