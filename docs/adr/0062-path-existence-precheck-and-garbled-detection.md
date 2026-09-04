# 0062. 路径存在性前置检查与乱码检测

- 状态：accepted
- 日期：2026-09-04
- 决策者：AI + 用户确认

## 背景

LLM 可能输出乱码路径（如 `D:\project\w2\ä¸­æ\æ¶å¤\ææ¡.txt`），该路径在工作目录外且不存在。原有 `PathPermissionChecker` 的 9 步决策链走到步骤9 `Ask("路径在允许的工作目录之外，需要用户确认")`，拦截等候用户确认。用户被迫手动拒绝，体验差且不合理——路径乱码本应直接报错"路径不存在"。

原有路径不存在检查分散在 `FileToolHandlers`/`SearchToolHandlers` 的实际文件操作时（后置检查），权限检查阶段不检查存在性。

## 决策

1. 新增 `PermissionBehavior.Invalid` 枚举值（`[EnumValue("invalid")]`）：语义为"路径无效（不存在/乱码），硬错误直接报错给 AI，不询问不执行"。

2. 新增 `PathPermissionCheckResult.Invalid(reason)` 工厂方法。

3. 修改 `PathPermissionChecker.CheckReadPermission`：在步骤8（allow 规则）和步骤9（默认 Ask）之间插入步骤8.5：
   - 仅对**工作目录外**的读取路径检查（工作目录内允许不存在，支持创建新文件）
   - 先检查 `IsLikelyGarbledPath`（含 U+FFFD 替换字符或控制字符）→ `Invalid("路径含乱码字符")`
   - 再检查 `!FileExists && !DirectoryExists` → `Invalid("路径不存在")`
   - allow 规则已优先（允许尝试读取可能不存在的文件）

4. 修改 `PermissionCheckContext.MapPathResult`：`Invalid` → `Rejected`（不触发 PendingConfirmation/ask 面板，直接拒绝并报错给 AI）。

5. 乱码检测 `IsLikelyGarbledPath`：仅检测 U+FFFD（UTF-8 解码失败标志）和控制字符，**不检测连续非 ASCII 字符**（避免误判中文路径如 `D:\项目\文档.txt`）。

## 替代方案

- **复用 Deny 而非新增 Invalid**：语义混淆（Deny=规则拒绝，Invalid=路径本身无效）。用户选择新增枚举值。
- **检测连续非 ASCII 字符判定乱码**：会误判合法中文路径。仅检测 U+FFFD 和控制字符更保守安全。
- **所有读取路径都检查存在性（含工作目录内）**：会拦截"先检查文件是否存在再读取"的合法模式。仅检查工作目录外。
- **写入路径也检查存在性**：会拦截所有文件创建操作。写入不检查。

## 后果

- 正面：LLM 输出乱码/错误路径时直接报错给 AI，不进 ask 面板拦截用户；AI 可立即修正路径重新调用。
- 负面：工作目录外不存在的合法路径（如远程文件未同步）也会直接报错，而非询问用户。但这种情况罕见且"路径不存在"报错信息清晰。
- 中性：PermissionBehavior 枚举新增值，所有 switch 消费方需 awareness（MapPathResult 已处理，其他 switch 有默认分支不受影响）。
