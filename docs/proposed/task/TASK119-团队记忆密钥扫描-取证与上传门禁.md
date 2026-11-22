# TASK119 — 团队记忆密钥扫描（TASK103 §2.3 #50 取证）

> 状态：**上传路径真缺口，已补门禁** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#50 / TASK071-A6**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「团队记忆密钥扫描（gitleaks 规则）」在本仓要拆两半：

- **规则库已具备，不属于缺失**：`modules/abstractions/guard/security/scanning/SecurityPatterns.cs:104-300` 内联 30+ 条高置信度 gitleaks 规则（对齐 TS `SECRET_RULES`），有惰性编译正则与 `ScanForSecretMatches`；已被 Git 提交前扫描（`GitToolHandlers.cs:39`）与子代理 checkpoint（`SubagentStopCheckpoint.cs:11/41-55`）消费。基准同样不用 gitleaks 二进制/独立 TOML，而是内联规则（`secretScanner.ts:48-224`），因此"规则文件"不是差距。
- **真缺口在团队记忆上传链**：基准在写入前（`FileWriteTool.ts:157-160` / `FileEditTool.ts:144-147` 的 `validateInput`）和上传前（`teamMemorySync/index.ts:599-615`，命中则整文件跳过 + warn + 埋点 `:924-945`）各扫一次。本仓 `TeamMemorySyncService`/`SyncFileTransfer` 全链路无 `ScanForSecretMatches` 调用——`PushToRemoteAsync` 读到内容（`SyncFileTransfer.cs:39`）后直接算 hash 并写远端索引，**密钥可随团队记忆同步出去**。
- **本次补上传前门禁**：`PushToRemoteAsync` 读到内容后用同一份 `SecurityPatterns` 扫描；命中则只记 ruleId（绝不记密钥值）、跳过本文件、不写远端索引、不发 Synced 事件，其余文件不受影响。写入前拦截属于工具层 validateInput 改造，另记后续（本项先堵"出站"这一最高风险面）。

---

## 1. 四问回答

### 问1 专家隐性实践
密钥防泄漏要卡在"数据离开信任边界"之前：团队记忆同步是明确的出站点，哪怕写入时漏过，上传门禁也应拦住。扫描结果只回规则 ID/label，绝不回匹配文本——日志与埋点本身不能成为二次泄漏面。命中时跳过单文件而不是终止整批，避免一个误报阻塞整个团队同步。

### 问2 原始素材（一手）
- 基准：`src/services/teamMemorySync/secretScanner.ts:48-224/227-237/277/312-324`（规则、惰性编译、扫描、redact）、`teamMemSecretGuard.ts:15-44`（路径判定 + 写入拒绝）、`index.ts:567-615/889-945`（上传逐文件扫描 + 跳过 + warn/埋点）、`tools/FileWriteTool/FileWriteTool.ts:157-160` 与 `FileEditTool/FileEditTool.ts:144-147`（写入前 validateInput）。
- 本仓：`modules/abstractions/guard/security/scanning/SecurityPatterns.cs:104-300`（同款规则/扫描）、`modules/guard/guard_hooks/lifecycle/SubagentStopCheckpoint.cs:41-55` + `modules/cognition/hands_core/tool_handlers/dev_tools/handlers/GitToolHandlers.cs:39`（已有消费点）、`modules/storage/vault/memdir/sync/helpers/SyncFileTransfer.cs:35-66`（上传链，读内容但零扫描）。

### 问3 具体方法与工具
按基准"写入前 + 上传前"双门禁对照本仓；全仓 grep `ScanForSecretMatches` 确认已有规则库但同步链零调用；选 `PushToRemoteAsync` 作为出站卡点（内容已读入，改动最小且不改跨 slnx 契约）。

### 问4 独立验证来源
基准 `secretScanner.ts:1-18` 注释明确规则手工摘自 gitleaks 且无二进制依赖，与本仓 `SecurityPatterns.cs:105` 的"对齐 TS SECRET_RULES"注释互证规则同源；`SyncFileTransferTests` 提供 InMemoryFileSystem 夹具可做确定性回归。

---

## 2. 差异与判定

| 维度 | 基准 | 本仓（修改前） | 本仓（修改后） |
|---|---|---|---|
| 规则来源 | 内联 30+ gitleaks 规则 | 同款内联规则库 | 不变 |
| 写入前拦截 | Write/Edit validateInput 拒绝 | Git/checkpoint 有，团队记忆写入无 | 未覆盖（后续） |
| 上传前拦截 | 逐文件扫描，命中跳过 | **零扫描，密钥可出站** | 用同一规则库跳过 |
| 日志 | warn + 埋点（不含密钥） | — | 只记 ruleId，不含内容 |

---

## 3. 施工

1. `SyncFileTransfer.PushToRemoteAsync` 在 `ReadAllTextAsync` 之后、`ComputeAsync`/远端索引写入之前调用 `SecurityPatterns.ScanForSecretMatches(content)`；命中即 `_logger?.LogWarning("[TeamMemorySync] 跳过含密钥文件: {Path} rules={Rules}", filePath, ruleIds)` 并 `return`。
2. 测试：在 `SyncFileTransferTests` 加两项——含 AWS/Access-Key 形态内容不上传（远端索引未写、无 Synced 事件）；普通内容仍正常上传（回归保护）。
3. 不改规则库、不改事件模型、不改文件写入路径。

---

## 4. 验收（回填）

- 单测：`SyncFileTransfer` 筛选 **13/13 绿**（新增"含密钥跳过上传/索引/事件"与"普通内容仍上传"两项）；Vault.Memdir 全量 **486/486 绿**。
- 编译/真机：`Build-AllDomains.ps1 -Changed` 2 项目通过；CLI 全量单线程重编 0 错误；真机 `--selftest-tui` `HOST SELFTEST OK` EXIT=0。
- 产物：`JoinCode.Vault.dll` 含 UTF-16 文案「跳过含密钥的团队记忆文件」，确认门禁进产物。
- 总账 #50 划掉并指向本文档，写入前拦截登记为残余。
