# 0003. rebase 而非 merge

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

项目采用任务分支工作流（w1/w2/w3...）→ main 的两阶段流水线。频繁同步 main 到任务分支时，若用 `git merge`，会产生大量 "Merge branch 'main' into wN" 合并提交，污染历史、干扰 `git log` 阅读和 bisect。

## 决策

1. **用户说"合并 main"一律执行 `git rebase main`**，禁止 `git merge`
2. **唯一例外**：首次将功能分支合入 main 时，由用户手动 `git merge --ff-only` 或 `git rebase`
3. **PR 合并用 squash**：auto-merge 启用 squash 方式，main 上每个 PR 只留一个提交
4. **PR 合并后同步**：任务分支 `git reset --hard main`（因 squash 后哈希不同，reset 避免分叉）

## 替代方案

1. **允许 merge commit**：放弃。历史噪声大，"Merge branch" 提交无业务语义。
2. **只用 squash，禁止 rebase**：放弃。开发期同步 main 用 squash 语义不对（squash 是合并 PR 用的），rebase 是同步基线的正确操作。
3. **始终 reset --hard main**：放弃。分支有未合入 main 的独有 commit 时 reset 会永久丢失，必须先 `git log --oneline wN --not main` 判断。

## 后果

- 正面：线性历史，`git log` 干净可读，bisect 不被合并提交干扰
- 负面：rebase 会重写提交哈希，已推送的分支需 force push（非 main/master 允许）；rebase 冲突需逐个 commit 解决
- 中性：rebase 前必须确保工作区干净（先 commit 或 stash）
