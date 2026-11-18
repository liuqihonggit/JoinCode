# TASK096 — 消除 `jcc tool find` 两字母查询噪音

> 起因：TASK093 遗留第 3 项 —— 「`jcc tool find` 对两字母查询仍有子串噪音（`rm` 会命中 `confirm_action`、`permission_*`）；本轮只把真命中排到前面（`+80`），没有改子串语义 —— 改了会影响 `find pr` 之类的既有预期」。

## 一、根因

`CliToolSearch.Contains` 原本对所有关键词做 `haystack.Contains(term)` 子串匹配。两字母词因太短，
极容易命中无关单词的内部：`confirm_action` 的 **rm**（con-f**irm**）、`permission_*` 的 **rm**（pe**rm**ission）、
`terminal_capture` 的 **rm**（te**rm**inal）。真机 `find rm` 命中 6 个节点，其中 5 个与"删除"无关。

TASK093 的 `+80` 只是把真命中（`file_delete` 有别名 `rm`）排到前面，没消除噪音条目本身。

## 二、改法：只对两字母启用词边界

`Contains(haystack, needle)` 分两路：

- `needle.Length == 2` → `ContainsWord`：needle 必须作为独立「词」出现（两侧是边界或串首尾）。
  单词字符为 `char.IsLetterOrDigit`，**下划线不算**——这样 `gh_pr_view` 的 `pr`、`file_delete` 的 `delete`、
  `notification` 之外真正的独立词都成立；`confirm_action` 的 `rm` 两侧是字母，不成立。
- `≥3` 字母 → 保留原子串语义。这是关键约束：`find request` / `find pull request` 命中 `gh_pr_view`
  是测试 `Find_MatchesIdSummaryAndTags_RanksByIdFirst` 锁定的既有预期，不能破。

`MatchesEntry` 的三条判定（规范 ID / node.yaml 别名 / `ToolAliasTable` 别名）都走同一个 `Contains`，
所以两字母同义词（`rm`/`del`/`ls`/`cat`）只要本身是独立词仍命中，不改别名逻辑。

### 一次误判的复盘

排查中曾用 `find cat` 当"两字母"样例，得到 7 个命中（notification/verification 等），一度以为修复无效，
加了多轮打点。根因是 **`cat` 是 3 字母**，本就该走子串语义——那 7 个命中是既有正确行为，不是 bug。
这恰好印证了 TASK093 的担心：改子串语义必须设长度门槛，否则会破坏 `find pr`/`find request`。所有临时打点已清除。

## 三、验证

**红测试**（`CliToolNodeTests`，先红后绿）：

- `Find_TwoLetterTerm_MatchesWordBoundaryNotMidwordNoise`：`confirm_action`/`permission_list` 对 `rm` 不算命中，
  `gh_pr_view` 对 `pr` 命中。修复前第一句失败（confirm_action 命中），修复后通过。
- `Find_LongerTerm_KeepsSubstringSemantics`：`request`/`pull request` 仍命中 `gh_pr_view`。

**真机**（`jcc tool find`，隔离默认）：

| 查询 | 修复前 | 修复后 |
|---|---|---|
| `rm` | 6 个节点（含 confirm_action、permission_*、terminal_capture、slash、tools…） | 1 个（file，含 `file_delete`） |
| `pr` | github | github（未变） |
| `request` | github | github（未变） |
| `ls` | — | 1 个（file，含 `directory_list` 别名） |
| `del` | — | 命中全是 `*delete` 真命中（file_delete / notebook_delete_cell / cron_delete / gh_*_delete） |

**回归**：`Abstractions.Primitives.Tools.Tests` 78（新增 2）、`Host.Tests` 全量通过。

## 四、遗留

剩余最高风险项：工具 ID 命名统一（`read` 与 `file_delete` 并存）—— 见 TASK095，需评估协议层影响。
