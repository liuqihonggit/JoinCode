# TASK091 — 配置键唯一清单 / 工具描述可达 / 通用迁移框架

> 状态：**已完成**
> 起点：TASK090 收尾时留下三条「已知未做」：`SupportedSettings`（16 键）与 `ConfigKey`（15 键）两套清单并存、
> `ConfigToolPrompt` 零调用方、通用迁移框架（版本号 + 迁移目录）未建。
> 用户 2026-11-18：「把剩余都做了」。

---

## 一、四张表合并成一张唯一清单

### 合并前的事实分布

| 表 | 位置 | 装什么 | 键数 |
|---|---|---|---|
| `ConfigKey` 枚举 | 契约层 | 键名 + 一句描述 | 15 |
| `SupportedSettings` | hands_core | 类型 / 描述 / 选项 / 校验 | 16 |
| `SettingSourceMap` | 契约层 | 键 → 哪个文件 | 16 |
| `SettingPathMap` | 契约层 | 键 → 文件内哪个字段 | 2 |

四张表各有增删节奏，于是**同一个键能出现三种说法**，且用户与模型各看见一半：

- `editorMode`：一处写 `default/vim`，另两处写 `vim/emacs/default` —— `emacs` 根本没有实现（全仓 grep 无第二处引用），
  写进设置的人会发现键盘行为没变。
- `apiKey`：枚举里登记成可配置项，`CurrentSettings` 里**没有这个字段** —— `config_get apiKey` 永远返回 null。
- `profile` / `fastMode` / `effortLevel` / `outputStyle`：只在枚举里有，于是 `/config set` 能写、
  `config_set` 报 `Unknown setting` —— LLM 看不到这 4 个键，用户以为工具坏了。

### 合并结果：`ConfigKeyRegistry`

契约层 `src/settings/ConfigKeyRegistry.cs`，每行自洽：

```csharp
[ConfigKeys.Model] = new() {
    Key = ConfigKeys.Model,
    Type = "string",
    Description = "当前生效模型（存于 vendor.{当前供应商}.model，写入即生效；可用 model_search 查看候选）",
    Source = SettingSource.UserSettings,
    Path = ["vendor", ProfilePlaceholder, "model"],
    AppStateKey = "mainLoopModel",
    FormatOnRead = v => v ?? "default",
    ValidateOnWrite = ValidateModelId,
},
```

- `ConfigSetting` 增加 `Key` / `Source` / `Path` 三个成员，成为**唯一定义类型**（不再有第二套元数据类型）。
- 删除 `SupportedSettings` / `SettingSourceMap` / `SettingPathMap` / `ConfigKey`（归档到 `.xxx/*.del`）。
- `ConfigKeys` 常量类取代枚举：常量只是这张表的索引标签，编译期即可核对，不再是一份独立清单。
- 清单从 16 键扩到 **20 键**（补回 `profile`/`fastMode`/`effortLevel`/`outputStyle`，删掉无落地字段的 `apiKey`）。

### 附属修正

- `permissions.defaultMode` 的选项从「只写在 hands_core」改为清单内声明，并**刻意不列 `unattended`**：
  那是无人值守长任务模式（ADR 0012），只能由用户从命令行开启，不给模型可调用的 config 工具留入口。
  选项字面量与 `PermissionMode` 枚举的一致性由 `ConfigKeyRegistryTests` 逐项锁定 ——
  契约层不引 security 契约层（不为 5 个常量加一条打包依赖边），改用测试对账。
- 未知键建议算法下沉为 `ConfigKeyRegistry.Suggest(key, max)`：斜杠命令与 `config_get` 诊断共用一份，
  按路径段命中数打分（`permissions.mode` → `permissions.defaultMode` 得 2 分，`editorMode` 只有 1 分）。
  此前 `config_get` 只做「互相包含」判断，`permissions.mode2` 一个候选都给不出来。

---

## 二、`ConfigToolPrompt` 从未到达模型

`ConfigToolPrompt.GeneratePrompt(Dictionary<string, ConfigSetting>, List<ModelOption>)` **零调用方**：
源码生成器只认**无参**的 `GetDescription` / `GetPrompt` / `Content` / `Prompt` / `Description`，
带参重载永远不会被注册，于是那份「可配置设置清单」从来没进过请求体。模型只知道有个 config 工具，
不知道能设哪些键 —— 于是猜 `permissions.mode`、猜 `model: opus`，再被逐个拒绝。

改法：删掉带参重载，新增无参 `GetDescription()`，内容由 `ConfigKeyRegistry` 直接生成（分组按 `Source`，
选项按 `Options`，模型候选项指向 `model_search`）。生成器按优先级选中它，
`ToolPromptRegistration.GetDetailedDescription("config")` 返回的就是这份长文本。

`brain.prompts.tests/ConfigToolPromptTests.cs` 锁 5 条：注册到的是长描述而非一行短描述、
每个键都在、枚举键带选项、global/project 分组与该键真实存储源一致、不再出现写死的 `sonnet/opus/haiku/best`。

---

## 三、通用迁移框架：版本号 + 迁移目录

原迁移是手写方法 `SettingsSourceMigration.RunAsync`，每次配置读写前跑一遍，靠「搬完就没得搬」的结构幂等自保。
这条约定**只对搬键成立**：改值、合并字段、换存储格式都无法用「还有没有可搬项」判断跑过没有。

新增 `core/loading/migrations/`：

| 文件 | 职责 |
|---|---|
| `IConfigMigration.cs` | 迁移契约（`Version` / `Name` / `ApplyAsync`）+ `ConfigMigrationOutcome` / `State` / `Report` |
| `ConfigMigrationRunner.cs` | 读版本 → 按序执行 → **每步成功即落盘** → 失败停在该步且不推进版本 |
| `SettingsSourceMigration.cs` | 迁移 v1 = 原来的 global 键搬迁，逻辑不变 |

- 版本标记：`~/.jcc/config-migrations.json`（`AppDataPaths.ConfigMigrationsFilePath`），
  内容 `{ appliedVersion, history[≤10] }`，`history.detail` 用宽松序列化写真实中文（默认编码器会转成 `\uXXXX`）。
- 登记显式而非反射扫描：`ConfigMigrationRunner.Migrations` 一行一个迁移 —— 迁移顺序是数据正确性的一部分，必须肉眼可审。
- 标记损坏 / 缺失 → 当作 v0 重跑（每个迁移仍必须幂等）；写标记失败只记日志，不阻断配置读写。
- 单步失败不吞异常：失败即返回报告并停止推进，下次启动从该版本重试，已完成步骤不重跑。
  原实现在迁移内部 `catch` 掉一切，导致「失败」和「没什么可做」长得一样，用户永远等不到重试。

`ConfigMigrationRunnerTests`（8 条，用可注入迁移集合）：升序执行、已应用跳过、已是最新不动、
失败停驻且版本不推进、下次补上、标记损坏当 v0、history 截断到 10 条但版本继续走、生产迁移注册合法性。

---

## 四、顺带修掉：扁平键写入丢字段

真机验证时发现 `config_set effortLevel high` 之后，用户文件里的 `autoFetchModels: false` 变成了 `true`，
还多出 `env` / `mcpServers` / `enabledPlugins` / `toolPenalties` / `searchProviders` 五个空字典。

病根与 TASK090 修嵌套键时一模一样：**扁平键写入走强类型往返**（`SettingsJson` 反序列化 → 改一个字段 → 整份序列化），
DTO 不认识的顶层字段按默认值重写。对离线用户来说，这等于悄悄把「不联网拉模型」开关打开了。

改法：强类型只用来决定「这个值该是什么类型」，落盘时只把 `current.<key>` 这一个字段合并回原 JSON
（`ConfigLoader.MergeCurrentScalar`），其余字节一律保持原样。新增 3 条测试锁住：
不丢未知顶层字段、不注入空集合、删键不影响其它键。

---

## 五、验证

### 真机（`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`，隔离 `JCC_APP_DATA_FOLDER`）

| 动作 | 结果 |
|---|---|
| 冷启动旧布局，`slash_call config get theme` | `theme = dark`；`theme`/`showTurnDuration` 搬进 `global.json`，`settings.json` 里的副本删除 |
| `config-migrations.json` | `appliedVersion: 1`，`detail: "搬运: theme, showTurnDuration"`（中文未转义） |
| 再跑一次 | 不重复迁移，`history` 仍 1 条、时间戳不变 |
| `config_get effortLevel`（合并前报 Unknown setting） | `effortLevel = null` |
| `config_set effortLevel high` → 读回 | `effortLevel = high` |
| `config_list` | 20 键，按 global/project 分组，枚举键带选项 |
| `config_set theme neon` | 拒绝，列出 7 个允许值 |
| `config_set permissions.defaultMode plan` | 写入 `current.permissions.defaultMode` |
| `config_set model deepseek-v3` | 写入 `vendor.p1.model`，读回一致 |
| `config_set effortLevel high` 后检查 `autoFetchModels` | 仍为 `false`（修复前会被改回 `true`） |
| `config_get permissions.mode2` | `[诊断] 你是不是想用: permissions.defaultMode` |
| `/config set them dark` | `注意: 'them' 不在已知配置项里…你是不是想用: theme？`（仍写入，不硬拦） |
| `jcc prompt --section tools` | 段落正常，无回归 |

### 测试与门禁

- `Guard.Config.Tests` **1310** 通过（含新增 `ConfigKeyRegistryTests` 13 条、`ConfigMigrationRunnerTests` 8 条、
  `ConfigKeyPathsTests` 新增 3 条扁平写入保留测试）。
- `Composition.Tests` 1074、`Brain.Prompts.Tests` 225、`Hands.ToolHandlers.Tests` 611 全绿。
- 探针 `non_deliverables_tools/config_source_probe`：12 条断言全过（三条路径 + 迁移一致）。
- 门禁脚本：`Verify-RegistrationOrder`、`Verify-ExplicitOrder`、`Verify-ToolParamNames`（36 项）、
  `Verify-ToolNodes.ps1 -DeclarationsOnly`（13 组 / 635 工具 / 388 声明隔离）全部通过。

---

## 六、已知遗留（未在本轮处理）

1. **`settings.json` 写入会带 UTF-8 BOM**：`FileEncodingDetector.DecodeBytes` 对无 BOM 的 UTF-8 返回带 preamble 的
   `Encoding`，`EncodeString` 便写出 BOM。属既有行为（TASK090 的嵌套写入同样如此），影响是 git diff 噪音，
   不影响解析。修它要动 `BclFileIO.DecodeBytes`，影响面覆盖整个文件 IO，需单独一轮。
2. **斜杠命令路径不做硬校验**：`/config set <未知键>` 只警告不拒（插件/历史键可能被其它代码读取）。
   工具侧 `config_set` 仍是硬拒 —— 两条路径的严格程度不同，属有意取舍，若将来确认无外部键依赖可统一。
