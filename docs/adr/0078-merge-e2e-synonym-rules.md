# 0078. 合并与 E2E 同义词规则

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

用户对话中使用"合并"、"E2E"等词汇时，需要统一解释为具体的工程操作，避免歧义。本文档定义这些同义词的标准含义与执行规范。

## 详细内容

## 用户说的"合并"

- **合并 = rebase，禁止 merge**

- 当用户说"合并 main"、"同步 main"、"把 main 合过来"等，一律执行 `git rebase main`，**禁止使用 `git merge`**

- 原因: merge 会产生大量 "Merge branch 'xxx' into yyy" 合并提交，污染历史；rebase 保持线性历史，干净可读

- 唯一例外: 首次将功能分支合入 main 时，由用户手动执行 `git merge --ff-only` 或 `git rebase`

- **rebase 前必须确保工作区干净**：rebase 要求无未提交修改，否则会拒绝执行。处理方式：
  - 先提交：`git add -A; git commit -m "wip: 临时保存"` → `git rebase main`
  - 或暂存：`git stash` → `git rebase main` → `git stash pop`
  
- **⚠️ `reset --hard` vs `rebase` 的生死线**：

  | 场景 | 命令 | 原因 |
  |------|------|------|
  | 分支有**未合入 main** 的新 commit | `git rebase main` | rebase 会把独有 commit 变基到 main 之上，**不丢失** |
  | PR 已合入 main，分支同步 | `git reset --hard main` | 分支 commit 已在 main 中，reset 只是快进指针，**不丢失** |
  | main 与开发分支哈希冲突 | `git reset --hard {分支名}`（在 main 上执行） | squash 合并后哈希不同，reset 直接指向，**不丢失** |

  - **⛔ 绝对禁止**：分支有未合入 main 的独有 commit 时执行 `git reset --hard main` — 这会**永久丢失**这些 commit
  - **判断方法**：`git log --oneline w3 --not main` — 有输出说明有独有 commit，只能 rebase；无输出说明已全部合入，可以 reset
  
- **分支工作流**：任务分支（w1/w2/w3...）→ main 两阶段流水线

  - 任务分支同步 main：`git rebase main`
  - 禁止在 main 上直接提交或 rebase 任务分支

- **两阶段流水线（强制）**：

  1. **任务分支 → main**：PR 触发 CI（编译+单元测试+集成测试+E2E+AOT），CI 通过后 auto-merge（squash）合并到 main
  2. **main 合并后**：开发分支 `git reset --hard main` 同步

- **PR 创建前必须先合并最新 main（强制）**：

  1. `git fetch origin main` — 拉取最新 main
  2. `git merge origin/main` — 合并到当前任务分支（用 merge，不是 rebase，因为要保留完整历史供 CI 验证）
  3. 如有冲突，解决冲突后编译验证
  4. 编译通过后再创建 PR

- **PR 合并后同步流程（强制）**：

  1. main 分支：`git pull --rebase origin main`（拉取 squash 合并后的新提交）
  2. 任务分支：`git reset --hard main`（覆盖为 main 最新状态，避免哈希分叉）
  - 原因: squash 合并后 main 的提交哈希与任务分支不同，不 reset 会导致分支分叉

- **PR 创建规则**：

  - 任务分支 → main：`gh pr create --base main --head w3 --title "feat: xxx"`
  - 创建后启用 auto-merge：`gh pr merge <number> --auto --squash`
  - 禁止手动合并 PR（除非 auto-merge 不可用）

- **CI 触发**：
  - PR 到 main 时触发全量 CI
  - CI 必须通过才允许合并
  - **⛔ dirty PR 不触发 CI**：`mergeable_state=dirty`（分支与 main 有冲突）时 GitHub 不会运行 CI。必须先在分支上 `git merge origin/main` 解决冲突并推送，CI 才会触发
  - **CI 重试**：`gh run rerun <run-id> --failed` 只重试失败的 job（不加 `--failed` 也是默认只重试失败项）
  - **⚠️ auto-merge BLOCKED 排查清单**（2026-07-30 踩坑记录，2026-08-18 修正区分正常/异常）：
    1. **先区分正常 vs 异常 BLOCKED**（最关键一步，跳过会误判）：
       - 诊断命令：`gh pr view {number} --json mergeable,mergeStateStatus,statusCheckRollup --jq '{mergeable:.mergeable, state:.mergeStateStatus, checks:[.statusCheckRollup[] | {name:.name, status:.status, conclusion:.conclusion}]}'`
       - **正常 BLOCKED**（无需处理）：`mergeable=MERGEABLE` 且存在 `status=IN_PROGRESS` 或 `status=PENDING` 的 check → CI 正在运行，等全部通过后 auto-merge 自动触发。其他 `needs: build` 的 job 在 build 完成前不会出现在 checks 列表中，**不要因为"实际 check 数 < required check 数"就判定为名称不匹配**
       - **异常 BLOCKED**（需修复）：所有 check 的 `conclusion` 均非 null（全部完成）且无 `IN_PROGRESS`/`PENDING`，但 `mergeStateStatus` 仍为 `BLOCKED` → 这才是 check 名称不匹配
    2. **异常 BLOCKED 根因**：Branch protection 的 required status checks 名称与 CI workflow 实际 job 名称不匹配 → GitHub 认为该 required check 永远未完成 → PR 永远 `mergeStateStatus: BLOCKED` → auto-merge 永远不触发
    3. **典型案例**：protection 配了 `McpToolHandlers`（旧名），CI 实际是 `McpToolDispatch`（新名），差一个词就导致所有 PR 永远无法 auto-merge
    4. **触发场景**：重命名 CI job、删除/重建保护分支、修改 workflow 文件名后未同步更新 branch protection
    5. **异常 BLOCKED 诊断命令**（仅在确认异常后执行）：
       - `gh api repos/{owner}/{repo}/branches/main/protection --jq '.required_status_checks.contexts'` — 查看保护规则要求的 check 名称
       - `gh pr checks {number}` — 查看 PR 实际的 check 名称
       - 对比两者，找出不匹配的名称
    6. **修复命令**：构造 JSON body 调用 `gh api -X PUT repos/{owner}/{repo}/branches/main/protection -H "Accept: application/vnd.github+json" --input protection_update.json`（需要 `restrictions: null` 字段，否则 422）
    7. **预防**：每次重命名 CI job 后，必须同步更新 branch protection 的 required status checks

## 用户说的E2E

1. MockServers是真实的服务exe,每个用配置文件绑定不同的端口.预设一些对话返回,包括调用read工具.
2. jcc.exe真实启动,通过-p发送对话,到本机服务,加端口参数.不要模拟对话,遇到直接删除.
启动之后,需要观察发生什么错误,并且修复.
3. 当前可能有直接启动jcc.exe的卡死问题,你修改它内部,提供一个启动参数-await 5,
表示停留5s自动关闭.这个agnet肯定可以5s内完成任务.
触发计时器死亡的话,提供一个返回值1234,这个时候你就去修复它内部的东西.
4. 遇到bug,卡死,等等,应该尽可能去加日志点位,不要自己猜测,去行动证明.
过程中遇到任务问题,都必须要修复.并记录到doc.
5. 修复全部的链路和服务.

## 替代方案

无。rebase 保持线性历史是团队既定规范；E2E 真实启动测试是验证完整链路的唯一方式。
