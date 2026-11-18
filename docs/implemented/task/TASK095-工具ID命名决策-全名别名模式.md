# TASK095 — 工具 ID 命名决策：全名 + 别名模式（不改运行时 ID）

> 起因：TASK093 遗留第 2 项 —— 「工具 ID 命名仍未统一（`read` 与 `file_delete` 并存）」。
> 本轮评估后**决定不改运行时 ID**，并把用户要的「全名 + 别名模式」现状与边界固化在本文档，防止反复重开。

## 一、用户诉求与确认

用户原始诉求（TASK093）：「`read` 和 `file_read` 尤其是删除，这些词汇没有统一，应该全名和模糊都要匹配才行。」
本轮进一步明确：「我要求是全名 + 别名模式，别名通常是简短的，全名则详细。」
并就"是否改对外呈现"拍板：**保持呈现用短名**。

## 二、结论：诉求已达成，不改 ID

真机验证（`jcc tool call file <名> --file_path /nonexistent`，三种写法走同一条链）：

| 写法 | 类别 | 结果 |
|---|---|---|
| `read` | 短名（运行时注册名 / 别名） | 解析到工具 `read`，报"文件不存在"（正确到达工具层） |
| `file_read` | 详细全名 | 同上，报错文案完全一致 |
| `cat` | 简短同义词 | 同上 |

**全名（详细）+ 别名（简短）并存的模式已经生效**，且 `ToolAliasTableTests` 有 21 条契约测试锁定：

- `Resolve_PrefixIsBidirectional`：`file_read`↔`read`、`file_write`↔`write`、`delete`↔`file_delete` 双向互认；
- `Resolve_SynonymLandsOnCanonical`：`cat`→`read`、`rm`/`del`/`erase`→`file_delete`、`ls`→`directory_list` 等；
- `Resolve_SynonymsDoNotLeakAcrossGroups`：`rm` 不会在 git/github 组误命中。

## 三、为什么不改运行时 ID（`read` → `file_read`）

1. **解析链不认全名**：`LocalToolRegistry.GetToolAsync` 是精确字典查找（`_tools.GetValueOrDefault`），
   LLM 发的工具名必须先经 `ToolNameResolver.RepairToolName` 归一。`RepairToolName` 目前只有
   枚举 `FromValue` + 去下划线兜底，`file_read` 去下划线是 `fileread`，与枚举值 `read` 不相等 —— 
   直接把首轮 tools 字段改成 `file_read` 会让工具调用找不到 handler。要改就得动协议执行链。
2. **牵动面远超工具层**：`FileToolName` 的 `[EnumValue("read")]` 被源码生成器编译期嵌入提示词、
   安全分类、内容替换策略等几十处；`"read"` 字面出现在 54 个源码文件，`OperationType.Read`、
   `MemorySubCommand.Edit` 等与文件工具毫无关系的枚举也用同一字符串。改名是全局字符串重构。
3. **有前车之鉴**：`resources/tools/base.yaml` 注释明确记录——id 写成 `file_read` 曾导致
   "首轮 tools 只剩 bash 一个，模型无论如何都读不到文件"。首轮 tools 字段的 id 必须等于运行时注册名。
4. **收益为负**：首轮 tools 给 LLM 看短名 `read`（更短、更省 token）；改 `file_read` 反而更长，
   而"全名可用"已由别名表覆盖。

## 四、边界与逃逸舱

- **规范呈现（首轮 tools / 提示词）**：保持短名 `read`/`write`/`edit`（用户确认）。
- **调用侧**：全名 `file_read`、短名 `read`、同义词 `cat`/`rm`/`ls` 全部可用，`ToolAliasTable` 是唯一数据源。
- **防回归**：`ToolAliasTableTests`（21 条）锁契约；`CliToolNodeTests.Find_TwoLetterTerm_*`（TASK096）锁搜索侧词边界。
- 若将来确需让 LLM 用全名，正确做法是**先**给 `ToolNameResolver` 加"组前缀全名→短名"映射并真机验证
  `jcc tool call`，**再**改 base.yaml，顺序不能反。

## 五、本轮三项遗留的收口

| 遗留（TASK093） | 处置 | 提交 |
|---|---|---|
| 40 个斜杠命令 JSON 参数 | 修复（GetNormalizedArgs 复用 GetSplitArgs） | TASK094 / `3bda28382` |
| 工具 ID 命名统一 | 评估后不改，决策归档（本文档） | 本轮 |
| 两字母查询噪音 | 修复（词边界匹配） | TASK096 / `4edecce73` |

TASK091 遗留的 `/config set <未知键>` 警告放行是有意设计（插件/历史键仍可能被读取，
`ConfigCommand.SetConfigAsync` 注释已说明），不是 bug，不硬拒。
