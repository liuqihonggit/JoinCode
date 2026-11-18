# TASK094 — 斜杠命令 JSON 参数归一化（40 个 GetNormalizedArgs 命令）

> 起因：TASK093 遗留第 1 项 —— 「40 个斜杠命令仍用 `GetNormalizedArgs`，经 `slash_call` 传 JSON 时拿到的是整串 JSON，与 `/model` 同一个病因」。
> `/model` 当轮只修了自己（改用 `GetSplitArgs()[0]`），其余 40 个命令没动。

## 一、根因链路（先定位，再动手）

`jcc slash_call <cmd> key=value` 的参数流向：

```
SlashCallExecutor.ResolveArgsJsonAsync
  → BuildArgsJsonFromKeyValueAsync 把 key=value 组装成 JSON 对象串 {"k":v}
  → ChatCommandContext.Arguments = 整串 JSON
  → 命令用 GetNormalizedArgs 读取
```

`GetNormalizedArgs` 原本只是 `context.Arguments.Trim()`，于是返回 `{"vendor":"openai"}` 整串。
真机基线（修复前）：

```
$ jcc slash_call vendor vendor=openai
未知供应商: {"vendor":"openai"}。输入 /vendor 查看可用列表
```

而 `GetSplitArgs`（TASK093 为 `/model`、`/debuglog` 引入）已经能解析 JSON 对象、按声明顺序抽值。
两条路径分叉的根源就在这里。

## 二、改法：一处收敛，命令零改动

没有逐命令改成 `GetSplitArgs()[0]` —— TASK093 文档的判断是对的：`trust add`、`hooks add`
这类多段参数不能只取第 0 段；而且 40 个命令各改一遍，既易错又破坏 TUI 手输路径。

改为让 `GetNormalizedArgs` 自己兼容两种形态（`IChatCommand.cs`）：

- 参数是 JSON 对象（`{...}`）→ 复用 `GetSplitArgs` 抽值 → 用空格拼回「还原后的原始文本」，
  与用户在 TUI 里手输 `vendor openai`、`hooks add PreToolUse ...` 的形态完全一致；
- 非 JSON → 仍走原来的 `Trim()`，位置参数命令行为不变。

这样命令内部原本的 `ToLowerInvariant()` / `StartsWith("add")` / `int.TryParse` / `Split(' ', 2)`
全部照常工作，40 个命令一行都不用改。JSON 解析只有 `GetSplitArgs` 一份实现，无重复。

### 为什么不硬拒未知配置键（TASK091 遗留第 2 项）

`/config set <未知键>` 与工具侧 `config_set` 行为不同：工具侧硬拒，斜杠侧只警告后仍写入。
看 `ConfigCommand.SetConfigAsync` 的注释，这是**有意设计**：插件/历史键仍可能被别的代码读取，
硬拒会破坏这类键的写入能力。警告里已经带上 `ConfigKeyRegistry.Suggest()` 的相近键提示。
这是「有理由的不一致」，不是 bug，本轮保留。

## 三、验证

**红测试**（`SlashCallArgumentTests`，先红后绿）：
新增 7 条 —— `GetNormalizedArgs` 对 JSON 单值 / 数字 / 多字段 / 布尔还原成文本，
对非 JSON 与带空格原样保留。修复前 4 条红（拿到整串 JSON），修复后 11 条全绿。

**真机**（隔离 `JCC_APP_DATA_FOLDER`，`jcc slash_call`）：

| 命令 / 参数 | 修复前 | 修复后 |
|---|---|---|
| `vendor vendor=openai` | 未知供应商: `{"vendor":"openai"}` | 已切换供应商 → openai |
| `vendor '{"vendor":"openai"}'` | 同上 | 已切换供应商 → openai |
| `vendor`（无参） | 列供应商 | 列供应商（未变） |
| `effort level=high` | 整串 JSON | 推理力度: high |
| `color name=red` | 整串 JSON | 颜色主题: red |
| `hooks action=list` | 未知操作整串 JSON | Hook 配置列表 |
| `thinkback count=2` | 解析失败取默认 1 | 正常进入回放 |
| `trust action=add` | 走 default 报用法 | 已信任当前工作区 |
| `trust action=list` | 走 default 报用法 | 信任目录 (1 个) |
| `goal objective=test` | 整串 JSON 当初始提示 | 初始提示: test |

单值（`vendor`/`effort`/`color`）、多段（`hooks`/`trust`）、数字（`thinkback`）、
复杂多字段（`goal`）四类都覆盖。

**回归**：`Host.Tests` 1336、`Composition.Tests` 1079 全绿；
`Verify-HonestyChecklist.py` 通过（清单与文档同步后）。

## 四、遗留

本项的能力修复已完成。剩余两项见 TASK095（工具 ID 命名统一）、TASK096（两字母查询噪音）。
