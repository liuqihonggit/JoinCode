# 0026. PR 两段式流水线验证

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

单段 CI 验证存在盲区：PR 上的 CI 通过后合并到 main，但 main 上的构建环境可能与 PR 分支不同（如 squash 合并后哈希变化、main 上有其他合并），导致合并后的 main 实际编译失败。

## 决策

**PR 两段式流水线验证**：

1. **第一段（PR → main）**：PR 触发 CI（编译 + 单元测试 + 集成测试 + E2E + AOT），CI 通过后 auto-merge（squash 方式）合并到 main
2. **第二段（main → main CI）**：main 合并后自动触发自身 CI 实现二次验证

**PR 创建规则**：
- 任务分支 → main：`gh pr create --base main --head wN`
- 创建后启用 auto-merge：`gh pr merge <number> --auto --squash`
- 禁止手动合并 PR（除非 auto-merge 不可用）

**PR 创建前必须先合并最新 main**：
1. `git fetch origin main`
2. `git merge origin/main`（用 merge 保留完整历史供 CI 验证）
3. 解决冲突后编译验证
4. 编译通过后再创建 PR

## 替代方案

1. **单段 CI（仅 PR 验证）**：放弃。squash 合并后 main 哈希与 PR 不同，单段验证无法覆盖 main 实际状态。
2. **手动合并 PR**：放弃。人为因素多，容易跳过 CI 检查。
3. **rebase 合并 PR**：放弃。rebase 会重写哈希，与 squash 语义不同，且多 commit rebase 冲突复杂。

## 后果

- 正面：main 上代码始终经过二次验证，可靠性高；自动化程度高
- 负面：CI 运行两次，耗时增加；auto-merge BLOCKED 需排查 check 名称匹配
- 中性：dirty PR（分支与 main 有冲突）不触发 CI，必须先解决冲突
