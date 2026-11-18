# TASK093 — 诚实模块贴合提示词 + 工具名全名/短名互认

> 起因（用户原话）：「你好像有**诚实功能**，和我的代码实现是不一样的，能不能去贴合你现在的提示词？
> 而且我发现了一个问题，文件的读写改：`read` 和 `file_read` 尤其是删除，这些词汇没有统一，
> 应该全名和模糊都要匹配才行。」

## 一、诚实模块：代码和提示词说的不是一回事

`goal_driven` 提示词最后一条写的是：

> 用户切换目标或切换模型时，先**复述已确认的目标**和诚实清单里**未完成项**，等用户重新确认方向再继续。

代码里的 `HonestyNotice` 只做了后半句，而且做得不对：

| 提示词要求 | 原实现 | 实际后果 |
|---|---|---|
| 先复述已确认的目标 | 完全不读目标状态 | 用户切模型后看不到「上一轮定的目标还算不算数」，模型只能凭上下文猜 |
| 只列未完成项 | 打勾/未打勾全打印（15 行） | 清单全 done 时等于把整份文档再念一遍，真正的缺口被 `[x]` 埋掉 |
| 「查不到」不等于「没有」 | 直接读 `IGoalEngine.CurrentState` | 该属性只在内存里，未 `Rehydrate` 时恒为 null → 打印「（无）」，等于告诉用户「你没定过目标」 |

改法：

- 新增 `HonestyNotice.RenderGoal(state, engineAvailable, loadError)` —— 纯函数，三态分明：
  引擎未就绪 → 「无法核实（目标引擎未就绪）」；加载失败 → 「无法核实（目标状态加载失败：…）」；
  确实没有 → 「（无）」+ 「请先说明这次要做什么」。**前两种不得出现「（无）」**。
- 打印前先 `RehydrateAsync()`：目标持久化在磁盘，进程重启后必须先加载，否则「无目标」是假话。
- 清单只列 `status != done` 的项；全 done 时打印一句结论
  「15 项全部有验证证据，无未完成项（doc）」。
- 清单条目本身同步：`honesty_module` 的描述改为「先复述目标、再列未完成项」；补上 TASK091 / TASK092
  两条已完成项；把已废弃的 `SettingSourceMap` / `SettingPathMapTests` 名字改成现行名。
- 新增门禁 `non_deliverables_tools/scripts/Verify-HonestyChecklist.py`（已进 CI `honesty-checklist`）：
  `done` 项数必须等于 TASK083 文档里 `- [x] **` 的行数，每项必须有 evidence / next，status 只允许
  done / pending，id 不许重复。**实测把一条改成 pending 立刻红**（14 ≠ 15），还原后绿。

### 顺带修掉的真 bug：`/model` 把整串 JSON 当模型名

`slash_call` 会把 `key=value` / JSON 规范化成 JSON 串放进 `ChatCommandContext.Arguments`；
`ModelCommand` 用的是 `GetNormalizedArgs`（返回整串），于是
`jcc slash_call /model model_id=probe` 把 `{"model_id":"probe"}` 写成模型名 —— 屏幕上还打印
「模型已切换为 {"model_id":"probe-honesty"}」。改用 `GetSplitArgs`（同文件里已有这个工具，
注释就写着「只按空格 Split 会把 JSON 当成一个整体参数」），取第一个值。

## 二、工具名：`read` / `file_read` / `delete` / `rm` 必须都落到同一个工具

### 现状（file 组 13 个工具）

```
read            ← 无前缀
write           ← 无前缀
edit            ← 无前缀
file_delete     ← 有前缀
file_edit_regex / file_insert_lines / file_delete_lines / file_batch_edit  ← 有前缀
directory_list / apply_patch / send_user_file
```

调用方每次都得先猜「这次要不要带 `file_`」，猜错得到的是「工具不存在」而不是可用提示。
而且这条判断散在三处：宿主 `jcc tool call`（`CliToolDescriptor.FindTool`）、
分组 exe（`CliNodeRunner.CanonicalToolId`）、发现入口（`jcc tool find` / `<exe> -h`）。

### 改法：一张表 + 三层判定，两端共用

契约层新增 `ToolAliasTable`（`abstractions.primitives.tools/src/cli_nodes/`），判定顺序固定：

1. **精确** —— 忽略大小写、忽略前导 `/`（斜杠组 ID 写作 `/agents`，Git Bash 会把它展开成路径）。
2. **组前缀互认** —— 名字带本组前缀就去掉，不带就补上：`file_read` ↔ `read`、`delete` ↔ `file_delete`。
3. **组内同义词** —— 只对 `file` 组声明（`rm`/`del`/`remove`/`erase`/`unlink` → 删除，
   `cat`/`type`/`open` → 读，`save`/`create`/`new` → 整体写，`sed`/`replace`/`sub` → 局部替换，
   `ls`/`dir`/`ll` → 目录，`patch` → `apply_patch`）。不做全局表：`rm` 不该在 git / github 组里指向任何东西。

命中多个声明名时返回 `null`（歧义不猜），由上层报「工具不存在 + 相近建议」。

消费点四处，全部走这张表：

| 位置 | 改动 |
|---|---|
| `CliToolDescriptor.FindTool` | 先按条目自带 ID/aliases 精确匹配（斜杠组的 `aliases` 不能被动），再走 `ToolAliasTable.Resolve` |
| `CliNodeRunner.CanonicalToolId` | 整段替换成 `ToolAliasTable.Resolve(groupId, known, requested)`；**签名加 `groupId`** |
| `CliToolSearch.ScoreTerm` / `AppendMatchedTools` | 别名参与打分与条目筛选；「名字恰好等于关键词」额外 +80，压过「恰好含这两个字母」的噪音 |
| `CliNodeRunner.PrintToolHelp` | `-h` 多打一行 `等价写法: file_read / cat / type / open` |

### 踩到的坑

同义词映射后直接走「补前缀」是错的：`cat` → `read`，而 `read` 本身不以 `file_` 开头，
补成 `file_read` 后在该组里不存在 → 返回 null。必须**先试短名本身、再试补前缀**。
测试 `Resolve_SynonymLandsOnCanonical` 当场抓到（5 条同义词全红）。

### 明确没做的事

工具 **ID 本身没有改名**。`read` / `write` / `edit` 是运行时注册名，也是协议原生 `tools` 字段的名字，
`base.yaml` 用 `id: read, name: FileRead`；改 ID 会连带动内核注册表、首轮工具清单与提示词。
本轮让「全名和短名都能用」，不是把两套名字合并成一套。
真要统一命名，建议方向是**全部带 `file_` 前缀**（`file_read` / `file_write` / `file_edit`），
`base.yaml` 保留 `id: read` 作为旧名别名 —— 但那是一次独立迁移，需单独评估协议层影响。

## 三、验证

**真机**（隔离 `JCC_APP_DATA_FOLDER`）：

| 场景 | 结果 |
|---|---|
| `jcc tool call file read`（短名） | 输出文件内容 |
| `jcc tool call file file_read`（全名） | 输出文件内容 |
| `jcc tool call file_read`（不带分组） | 输出文件内容 |
| `jcc tool call file cat`（同义词） | 输出文件内容 |
| `file delete` / `file file_delete` / `file rm` / `file del` 各删一个文件 | 4 个文件都删掉，目录只剩预期文件 |
| `file save`（同义词 → write） | 写入成功，内容正确 |
| `file ls`（同义词 → directory_list） | 解析到 `directory_list`（报的是缺 `--directory_path`，不是「工具不存在」） |
| 直接跑 `jcc-tool-file.exe file_read` / `cat` / `rm` / `file_delete` | 4 条全通（exe 侧不经宿主也认） |
| `jcc tool find rm` | file 组排第一，并列出 `· file_delete`（改前 0 命中） |
| `jcc tool call file read -h` | 多一行 `等价写法: file_read / cat / type / open` |
| `/model` 输出 | 先「已确认的目标」，再「诚实清单：15 项全部有验证证据，无未完成项」 |
| 把一条清单临时改 `pending` 后 `/model` | 只列该条（`[ ]` + 证据 + 下一步），还原后恢复一句结论 |
| `/model` 经 `slash_call` 传 `model_id=probe-3` | 打印「模型已切换为 probe-3」（改前是整串 JSON） |

**测试**：`Abstractions.Primitives.Tools.Tests` 76（新增 `ToolAliasTableTests` 21 条）；
`Composition.Tests` 1079（新增 `HonestyNoticeTests` 5 条）；`Hands.ToolHandlers` 611；`Guard.Config` 1310。

**门禁**：`Verify-ToolParamNames.py` 36 项、`Verify-RegistrationOrder.py`、`Verify-ExplicitOrder.py`、
`Verify-ToolNodes.ps1 -DeclarationsOnly`（13 组 635 工具、388 隔离）、
`Verify-HonestyChecklist.py`（新增，15 项 15 done）全绿。

## 四、遗留（下一轮）

1. **40 个斜杠命令仍用 `GetNormalizedArgs`**（`rename` / `fork` / `resume` / `effort` / `vendor` / `trust` /
   `hooks` / `env` / `tag` / `export` …）。这些命令经 `slash_call` 传 JSON 时拿到的是整串 JSON，
   与本次修的 `/model` 同一个病因。不能一刀切换成 `GetSplitArgs()[0]`：其中 `trust add <dir>`、
   `hooks add …` 这类是多段参数的，得逐命令判断「取几段」。
2. **工具 ID 命名仍未统一**（`read` 与 `file_delete` 并存）—— 见上文「明确没做的事」。
3. `jcc tool find` 对两字母查询仍有子串噪音（`rm` 会命中 `confirm_action`、`permission_*`）。
   本轮只把真命中排到前面，没有改子串语义 —— 改了会影响 `find pr` 之类的既有预期。
