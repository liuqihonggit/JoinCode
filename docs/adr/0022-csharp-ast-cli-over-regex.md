# 0022. C# AST CLI 优先于正则

- 状态：accepted
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

涉及 C# 源码的批量分析/重构/检测（Nullable 抑制检测、using 组织分析、命名规范检查、DI 注册验证等），早期用正则或文本替换，易误匹配、丢失语义、破坏代码结构。

## 决策

**脚本语言优先级**：

1. **C# AST CLI 优先**：涉及 C# 源码的批量分析/重构/检测，优先使用 `tools/JccAuditAstCli`（基于 Roslyn 的 AST 分析工具）
   - 构建命令：`dotnet build tools/JccAuditAstCli/JccAuditCli.csproj -c Release`
   - 输出路径：`artifacts/bin/JccAuditCli/Release/net10.0/jcc-audit.exe`
   - 适用场景：Nullable 抑制检测、using 组织分析、命名规范检查、DI 注册验证等需要语义理解的场景
2. **Python 脚本次之**：本机 Python 3.12.10，批量文本处理/脚本检测优先用 `.py` 脚本
   - 适用场景：文件搜索统计、简单文本替换、报告生成等不需要语义理解的场景
3. **PowerShell 最后**：PowerShell 5.1.19041.6456，仅用于系统操作和 dotnet/gh 命令编排
4. **gh CLI 优先**：操作 PR/Issue/Release 等 GitHub 资源时，优先用 `gh` CLI

**批量替换 C# 源码禁令**：
- ⛔ 禁止 `Out-File`/`Set-Content` 写 C# 文件（UTF-8 BOM → CS0234）
- ⛔ 禁止 `[regex]::Replace` 处理 C# 代码
- ✅ 正确：`ReadAllBytes` → `.Replace()` → `WriteAllBytes`

## 替代方案

1. **用正则处理 C# 代码**：放弃。正则无语义理解，易误匹配字符串内容、注释、嵌套结构。
2. **用 PowerShell 脚本处理 C# 代码**：放弃。PowerShell 文本处理能力弱，且编码问题多。
3. **手动逐个文件改**：放弃。效率低，大型重构不可行。

## 后果

- 正面：语义理解准确；不破坏代码结构；可处理复杂场景（嵌套、泛型、特性）
- 负面：AST CLI 需先编译；新增检测规则需改 CLI 代码
- 中性：Python 脚本用于非语义场景，与 AST CLI 分工
