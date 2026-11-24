# TASK132 jcc-gh 工具体验补强 — 假绿产物、jq 子集与写方法可见性

> 来源：2026-11-24 排查 PR #734「无冲突仍要 rebase 重跑 CI」时的真实工具摩擦。
> 状态：proposed（规划阶段，未实施）。
> 职责边界：**只改 PR 环境工具链**，不碰 CI/E2E 代码问题（后者属另一职责）。
> 约束：每项走「红测试 → 实现 → 编译 → 绿测试 → 真机 exe 验收 → 提交」。

## 1. 实证（本次排查逐条踩到）

| # | 现象 | 定位 | 性质 |
|---|------|------|------|
| 1 | `gh --version` 报「缺少 gh 分组」 | **根因已定位（2026-11-24 深挖修正）**：PATH 的 `jcc` → `C:/Users/54076/bin/jcc.d/dev/jcc.exe`（11-16 部署，旧 CLI 无 `gh` 命令节点），报「缺少 gh 分组」的是**旧 CLI 路由层**，不是 delegate。当前源码 `modules/apps/cli/core/commands/core/CliCommandNodes.cs:370` 有 `gh` 节点、`GhSubCommand.cs` 正确透传；新编 `artifacts/bin/JoinCode/.../jcc.exe gh --version` 正常路由（只因 artifacts 无 jcc-gh.exe 而报「找不到」，非「缺少分组」）。TASK101 C6 验收用 `jcc-gh.exe` 直调绕过 CLI → 假绿 | **部署陈旧 + 验收绕过真实入口**：部署目录 jcc.exe 半年未刷新，且 C6 的 exe 验收没走用户实际路径 |
| 2 | `gh api ... --jq '.a, .b'` 报「不支持的 jq 语法」 | `SimpleJqEvaluator.IsSupportedStage` 只认单表达式，逗号分隔被拒；官方 jq 合法 | 能力缺口：文档化的「jq 子集」未含多字段选择 |
| 3 | REST PATCH 返回 404，`documentation_url` 是泛化的 `https://docs.github.com/rest` | 本机网络层拦写方法（GET 正常、PATCH/POST-to-non-graphql 被伪装 404）；GraphQL POST 可用 | 环境问题，但工具**无法自证**，只能靠人肉猜到「改走 GraphQL」 |
| 4 | `gh pr merge --auto` 报告成功，但 REST 读 `auto_merge` 为 null | GraphQL 查 `autoMergeRequest.enabledAt` 有值——REST 字段映射与 GraphQL 不一致 | 观测口径分裂，容易误判「操作没生效」 |
| 5 | 查 `strict` 字段名靠 introspection 试错 | GraphQL 字段是 `requiresStrictStatusChecks`（非 `strictRequiredStatusChecksPolicy`）；无高层命令 | 缺高层封装：`gh branch set-protection` 之类 |

## 2. 优化项

### A. 消除 delegate 副本假绿（P0）

| 项 | 改法 | 验收 |
|----|------|------|
| A1 | `Deploy-JccDev.ps1` 部署完成后，**用部署目录的 jcc.exe 实跑 `gh --version` 与 `tool tree`**（用户实际入口），非 0 即 fail；不再只验 `jcc-gh.exe` 直调 | 负向实验：部署旧 CLI → `gh --version` 红；新部署 → 两条路径均 exit 0 |
| A2 | `Deploy-JccDev.ps1` 增加「部署目录 jcc.exe 与 `artifacts/bin/JoinCode/Debug/net10.0/jcc.dll` 逐字节对账」（现有对账只查主项目产物与 deploy 目录，未检查 deploy 的 jcc 是否来自本次编译） | 负向：改源码不重新 Deploy → 对账红；正常 Deploy → 绿 |
| A3 | `AGENTS.md` 编译段与 TASK101 C 批补「改 gh/CLI 后必须重跑 Deploy-JccDev 并验 PATH 入口」；测试脚本固定取 PATH 入口 | 文档可查；下次同类改动不再漏 |

> **注**：初稿 A1 写的是 delegate 副本字符串对账，深挖后否定——UTF-16 证据显示 delegate 确实旧，
> 但它不是 `jcc gh --version` 失败的原因（旧 CLI 路由层就没走到 delegate）。
> delegate 陈旧是**另一个**待办（pack 同步），不在本批解决用户实际痛点。

### B. jq 子集补多字段（P1）

| 项 | 改法 | 验收 |
|----|------|------|
| B1 | `SimpleJqEvaluator` 支持顶层逗号分隔 `.a, .b`（等价于 `select` 之外的常用投影），返回数组逐行输出；不支持的逗号位置仍明确报错 | 单测：`.login, .id` 两字段、`[.a,.b]` 数组构造、非法 `.a,,.b` 报错；真机 `gh api repos/o/r --jq '.name, .default_branch'` |
| B2 | 错误信息列出**最接近的合法写法**，而非只列语法清单（现已是好方向，继续补例子） | 负向测试断言错误文案含可执行示例 |

### C. 写方法可见性（P1）

| 项 | 改法 | 验收 |
|----|------|------|
| C1 | `gh api` 收到 404 且 body 的 `documentation_url` 为泛化 `https://docs.github.com/rest` 时，追加提示「写方法可能被网络层拦截；GET 正常可先确认资源存在；分支保护写操作可改走 GraphQL `updateBranchProtectionRule`」 | 单测：构造泛化 404 → 含提示；构造真实 404（具体 docs url）→ 不误报 |
| C2 | 高层命令 `gh branch set-protection --no-strict`（或等价）封装 GraphQL mutation，内部查 rule id + 字段名，用户不必记 `requiresStrictStatusChecks` | 真机对测试分支改 strict 后 GraphQL 回读一致；不传 `--no-strict` 时不改动其他设置 |
| C3 | `gh pr merge --auto` 回读统一走 GraphQL `autoMergeRequest`，避免 REST null 假阴性 | 真机：启用后命令自身回读 `enabledAt` 非空 |

## 3. 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|:------:|:------:|
| A1 delegate 对账 | `Pack-ToolNodes.ps1` | 待实施 | 待负向实验 |
| A2 文档与编译约定 | `AGENTS.md`、TASK101 C 批 | 待实施 | 待文档核对 |
| A3 双路径 exe 验收 | gh 验收脚本 | 待实施 | 待真机 |
| B1 多字段 jq | `SimpleJqEvaluator` | 待实施 | 待单测 + 真机 |
| B2 错误文案 | 同上 | 待实施 | 待负向测试 |
| C1 泛化 404 提示 | `gh api` 错误路径 | 待实施 | 待单测 |
| C2 高层 set-protection | `gh branch` | 待实施 | 待真机 |
| C3 auto-merge 回读 | `gh pr merge` | 待实施 | 待真机 |

## 4. 风险与回滚

| 风险 | 缓解 |
|------|------|
| A1 对账过严影响 pack 速度/兼容性 | 只比对关键行为分支（可先只查 `--version` 字符串），不全量哈希 |
| B1 jq 多字段改变输出格式 | 仅顶层逗号新增数组输出；既有单表达式输出不变（回归测试锁定） |
| C2 高层命令误改保护规则 | 默认 no-op，必须显式 flag；改前 GraphQL 回读现值并记录 |

## 5. 批次建议

A（假绿修复，P0）→ B（jq 子集）→ C（写方法可见性）。每批独立提交、独立真机验收。

## 6. 自主决策记录

<!-- 🤖 Auto Decision: 2026-11-24 TASK132 -->
<!-- 决策: 只做规划不改代码；A 批优先（假绿最危险：文档说验收过，产物却是旧的） -->
<!-- 原因: 本次排查绕圈的根因是「文档✅ ≠ 产物有」和「工具报错不指向真因」，两者都在 PR 环境职责内；E2E 退出码 210 属 CI 代码问题，用户明确划走 -->
<!-- 替代方案: 立即改 C2 高层命令（先把工具补齐）——否决，因为 A 的假绿会让任何新功能的验收再次失真，地基先修 -->
<!-- 验证: 规划阶段未改代码；实证来自本次 exe/GraphQL/UTF-16 DLL 字符串检查 -->
