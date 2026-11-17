# TASK089 — 配置存储源分叉：/theme 与 config 工具读的不是同一份文件

> 状态：**待决策**（已定位根因并真机复现，修复方向需用户选定）
> 起点：TASK086~088 连续三轮修复的都是同一根因 —— **同一语义在两条路径上宽严不一**。
> 本轮在同一根因下找到第 4 例，但这次的「两条路径」分属不同模块，且修复涉及用户数据迁移。

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

## 四、修复方案（待用户决策）

### 方案 A：统一到 settings.json（改动小，不动用户现有数据）

改 `SupportedSettings` 的 10 个 `Source = "global"` → `"settings"`。

- `/theme`、GUI、CLI `/config` 完全不动
- `config` 工具改为读写 settings.json → 与 UI 一致
- 代价：**偏离基准的存储布局**（基准主题在 global.json）
- 用户影响：已经误写进 global.json 的值需要一次性搬回（可顺手做，也可不管）
- 副作用：`global.json` 将变成完全无生产读者的空存储 —— 要么归档，要么后续留给别的用途

### 方案 B：统一到 global.json（对齐基准，需迁移）

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
