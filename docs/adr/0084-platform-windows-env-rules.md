# 0084. 平台专属操作禁令与 Windows 命令行环境

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

项目运行在 Windows 平台，PowerShell 5.1 与 Unix 工具存在诸多差异。本文档定义平台专属操作禁令、路径格式、命令分隔、脚本语言优先级等规范。

## 详细内容

## 🔴 平台专属操作禁令

### PowerShell 相关

1. **❌ 禁止使用 PowerShell `Set-Content` 修改 C# 文件**
   - 错误编号: CS1022
   - 原因: 可能导致文件损坏
   - 正确: 使用 IDE 的 `SearchReplace` 工具修改文件内容

2. **❌ 禁止使用 PowerShell 交互式命令**
   - 禁止: `Out-Host -Paging`
   - 推荐: 使用 `| Select-Object -First N` 替代分页

***

## ⚠️ Windows 命令行环境

### 路径格式

- 使用反斜杠 `\` 作为路径分隔符
  - 正确: `C:\Users\Name\Documents`
  - 错误: `/home/user/project`

### 命令分隔

- **禁止使用 `&&`** 连接命令
- 首选: 分步说明，每个命令单独一行
- PowerShell: 使用分号 `;` 连接
- CMD: 可使用单个 `&`（但忽略前序失败）

### 原生工具优先

- 优先使用 Windows 原生命令（`dir`, `findstr`）
- 或 PowerShell cmdlet（`Get-ChildItem`, `Select-String`）
- 避免依赖 Unix 工具（`grep`, `sed`, `awk`），除非明确要求 WSL

### 脚本语言优先级

> ADR: [0022](0022-csharp-ast-cli-over-regex.md)（C# AST CLI 优先于正则）

1. **C# AST CLI 优先**：涉及 C# 源码的批量分析/重构/检测，优先使用 `tools/JccAuditAstCli`（基于 Roslyn 的 AST 分析工具），而非正则或文本替换
   - 构建命令：`dotnet build tools/JccAuditAstCli/JccAuditCli.csproj -c Release`
   - 输出路径：`artifacts/bin/JccAuditCli/Release/net10.0/jcc-audit.exe`
   - 适用场景：Nullable 抑制检测、using 组织分析、命名规范检查、DI 注册验证等需要语义理解的场景
   - **子命令按功能分三组**（`jcc-audit --help` 查看完整用法）：

     | 组 | 子命令 | 用途 | 是否改文件 |
     |----|--------|------|-----------|
     | **审计(Audit)** | `audit` / `ctor-audit` / `layer-audit` | 扫描诊断输出报告 | 否 |
     | **修复(Fix)** | `replace` / `strip-bom` | 应用 CodeFix / 移除 BOM | 是 |
     | **统计(Stats)** | `top-files` | 大文件行数排行 | 否 |

   - **审计组**：
     - `jcc-audit [audit] <csproj-or-slnx> [--filter JCC规则ID] [--skip-tests] [--format json\|text] [--output <file>]` — JCC 规则审计（`audit` 可省略）
     - `jcc-audit ctor-audit <csproj-or-slnx> [--threshold 8] [--skip-tests]` — 构造函数参数审计，超过阈值报告
     - `jcc-audit layer-audit <slnx> [--skip-tests]` — 七层架构层依赖违规检测
   - **修复组**：
     - `jcc-audit replace <csproj-or-slnx> --rule <JCC规则ID> [--fix-all] [--dry-run]` — AST 批量替换，应用 CodeFix 到磁盘文件
     - `jcc-audit strip-bom <directory> [--dry-run] [--skip-tests]` — 移除指定目录下所有 .cs 文件的 UTF-8 BOM（字节级操作，自动跳过 bin/obj/.xxx/.git/artifacts 和 .Designer.cs/.g.cs）
   - **统计组**：
     - `jcc-audit top-files <directory> [--top 10] [--threshold 200] [--skip-tests]` — 按行数降序返回 Top N 大文件
   - **通用选项**：`--output` 写 JSON 报告、`--format json|text`、`--skip-tests` 跳过测试项目、`--dry-run` 预览不写入
   - **退出码**：0=无诊断/成功，1=参数错误，2=超时，3=有 Warning，4=有 Error
2. **Python 脚本次之**：本机 Python 3.12.10，批量文本处理/脚本检测优先使用 `.py` 脚本，而非 PowerShell
   - 适用场景：文件搜索统计、简单文本替换、报告生成等不需要语义理解的场景
3. **PowerShell 最后**：PowerShell 5.1.19041.6456，仅用于系统操作和 dotnet/gh 命令编排
4. **⛔ jcc gh 工具强制（禁系统 gh）**：操作 PR/Issue/Release/CI 等 GitHub 资源时，**必须**使用 `jcc mcp_call gh_*`（直调 GitHub REST API，无需系统 gh CLI）。**禁止**裸调系统 `gh` CLI，也禁止改用 PowerShell 脚本手动操作 > ADR: [0073](0073-gh-rest-api-direct-call.md)、[0089](0089-jcc-builtin-tools-only-no-system-gh-rg.md)
5. **⛔ jcc rg 强制（禁系统 rg）**：代码/文本搜索时**必须**使用 `jcc rg`（CLI 场景）或 `jcc mcp_call grep`（MCP 场景）（内置 `RgEngine`，mmap+PLINQ+零GC，无需系统 rg）。**禁止**使用系统 `rg`、PowerShell `Select-String` 或宿主 IDE 内置 Grep 工具 > ADR: [0070](0070-rg-engine-mmap-plinq.md)、[0089](0089-jcc-builtin-tools-only-no-system-gh-rg.md)

> **统一入口原则**：`jcc.exe` 启动后已自带 390 个 MCP 工具 + `jcc rg` 等 CLI 子命令，任何可由 jcc 自带工具完成的工作，禁止回退到系统/宿主环境自带工具。详见 ADR [0089](0089-jcc-builtin-tools-only-no-system-gh-rg.md)。

## 替代方案

无。Windows 平台约束与脚本语言优先级是项目既定规范。
