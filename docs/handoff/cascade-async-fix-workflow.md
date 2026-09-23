# .GetAwaiter().GetResult() → async + await 递归级联修复流程

## 概述

将同步 private 方法中的 `.GetAwaiter().GetResult()` 改为 `async + await`，并用 `CascadeAsyncFixer` 递归处理所有级联编译错误（CS4014/CS0029/CS1503/CS1929/CS0019）。

**核心原则**：具体问题，具体分析。遇到错误就回退工具修改部分，修复 CLI 工具进行递归级联。工具不行就撤回内容，再改工具，让工具丰富。

## 修复流程

### 第一步：初始修复（GetAwaiterFixer）

```
jcc-audit fix-getawaiter-getresult <项目根目录> [--dry-run] [--cascade <slnx>]
```

`GetAwaiterFixer` 遍历所有 .cs 文件，AST 解析每个方法节点：
1. 检测 private + 非 async 方法直接体内的 `.GetAwaiter().GetResult()`（用 `GetAwaiterPatternDetector.IsFixableViolation` 共享检测逻辑）
2. 给方法加 `async` 修饰符 + 改返回类型（`void→Task`, `T→Task<T>`）
3. 替换 `.GetAwaiter().GetResult()` → `await expr.ConfigureAwait(false)`

**检测逻辑共享**：`GetAwaiterPatternDetector`（在 `gen/aot_safety.shared/RuleDetectors/`），分析器和 ast_cli 调用同一套判定，同检同换。

### 第二步：级联修复（CascadeAsyncFixer）

初始修复后，调用方会报编译错误（方法签名变了）。`CascadeAsyncFixer` 用状态机递归处理：

```
状态机流转：
  Initial → Building → ParsingErrors →
    (无错误) → Done
    (有错误) → Fixing → Verifying →
      (修复了) → Building (继续循环)
      (未修复) → Failed (报告剩余错误)
```

**编译错误驱动**：每轮运行 `dotnet build`，解析错误输出，按错误类型决策修复策略。

## 状态机设计

### 状态枚举

| 状态 | 说明 |
|------|------|
| `Initial` | 初始状态，检查迭代上限 |
| `Building` | 运行 `dotnet build`，捕获输出 |
| `ParsingErrors` | 解析编译错误（CS4014/CS0029/CS1503/CS1929/CS0019） |
| `Fixing` | 按文件分组，按方法范围修复错误 |
| `Verifying` | 重新编译验证修复结果 |
| `Done` | 完成（无错误） |
| `Failed` | 失败（有未修复错误，需手动处理） |

### 修复策略（按错误类型决策）

| 错误码 | 含义 | 策略 |
|--------|------|------|
| CS4014 | 未 await 的 Task | `AddAwait`（加 await） |
| CS0029 | 类型不匹配 | `AddAsync`（改 async + 加 await） |
| CS1503 | 类型转换失败 | `AddAsync`（改 async + 加 await） |
| CS1929 | 方法不存在（过度修复） | `RevertToSync`（回退为 .GetAwaiter().GetResult()） |
| CS0019 | 运算符不适用（Lazy 约束） | `RevertToSync`（回退为 .GetAwaiter().GetResult()） |

## 跳过规则

### 1. using 声明中的调用

```csharp
// 跳过 — using 声明中的调用不加 await（结果类型是 IDisposable 不是 Task）
using var scope = TempFileScope.Create();  // 不加 await
```

**原因**：`using var x = Create()` 中 `Create()` 返回 `IDisposable`，加 await 会报 CS1929（`IDisposable` 不包含 `GetAwaiter`）。

**检测**：`IsInUsingDeclaration(node)` — 检查祖先中是否有 `LocalDeclarationStatementSyntax` 且带 `UsingKeyword`。

### 2. Lazy&lt;T&gt; 约束的方法

```csharp
// 跳过 — Lazy<T> 约束的方法不能改 async
private string Detect() => ...;  // 不能改 async，因为 Lazy<T> 需要 Func<T>
private readonly Lazy<string> _lazy = new(Detect);
```

**原因**：`Lazy<T>` 构造函数需要 `Func<T>`，改 async 后返回 `Task<T>`，类型不匹配报 CS0019。

**检测**：`DetectLazyConstrainedMethods(root)` — 遍历 `ObjectCreationExpressionSyntax`，找 `new Lazy<...>(MethodName)` 模式，收集方法名集合。

### 3. 构造函数

构造函数不能加 `async`，但可以修复方法组参数（如 `new Thread(ScanLoop)` → `new Thread(() => ScanLoop().GetAwaiter().GetResult())`）。

## 回退策略

当加 await 后编译报 CS1929/CS0019，说明该调用不适合改 async+await。回退为 `.GetAwaiter().GetResult()`：

1. 解析 CS1929/CS0019 错误行号
2. 在 `CascadeRewriter` 中，这些行号的调用用 `SyntaxHelpers.CreateGetAwaiterGetResult` 替代 `CreateAwaitExpression`
3. 重新编译验证

## 共享组件

### SyntaxHelpers（`gen/aot_safety.shared/SyntaxHelpers.cs`）

所有 fixer 共享的 trivia/格式处理：
- `CreateAwaitExpression` — 创建 `await expr.ConfigureAwait(false)`
- `CreateGetAwaiterGetResult` — 创建 `expr.GetAwaiter().GetResult()`
- `AddAsyncModifier` — 给方法加 async 修饰符
- `TransformReturnType` — 转换返回类型（void→Task, T→Task<T>）
- `PreserveTrivia` — 保留原始 trivia

**核心原则**：内部表达式去掉所有 trivia，leading/trailing trivia 由最终表达式统一设置，避免缩进翻倍。

### GetAwaiterPatternDetector（`gen/aot_safety.shared/RuleDetectors/`）

检测和修复共享同一套判定逻辑（同检同换）：
- `IsFixableViolation` — 判断是否是可修复的 .GetAwaiter().GetResult() 违规
- `IsInAsyncContext` — 判断直接包含的函数体（lambda/方法）是否 async
- `GetEnclosingFunction` — 获取直接包含的函数体（不是外层方法）

### FileFilter（`non_deliverables_tools/jcc_audit_ast_cli/core/`）

共享文件过滤：
- `ShouldSkipFile` — 两级排除（通用 bin/obj/.xxx/.git + 修复器额外 bcl_bridge/aot_safety.generator）
- `IsTestFile` — 判断是否是测试文件
- `EnumerateCsFiles` — 遍历 .cs 文件

## 使用方法

### 完整修复流程

```bash
# 1. 先 dry-run 预览
dotnet run --project non_deliverables_tools/jcc_audit_ast_cli -- fix-getawaiter-getresult . --dry-run

# 2. 实际修复 + 级联
dotnet run --project non_deliverables_tools/jcc_audit_ast_cli -- fix-getawaiter-getresult . --cascade build/sln/JoinCode.slnx

# 3. 全量编译验证
dotnet build build/sln/JoinCode.slnx
```

### 仅分析（不修复）

```bash
dotnet run --project non_deliverables_tools/jcc_audit_ast_cli -- analyze-getawaiter .
```

输出详细分类表格：不能改 / 高风险 / 中风险。

## 已知限制

1. **无语义模型** — ast_cli 不加载 MSBuildWorkspace（104 项目超时），无法用 `GetSymbolInfo` 判断返回类型。改用编译错误驱动：先尝试加 await，编译报错再回退。
2. **方法组转换** — `new Thread(ScanLoop)` 不是 invocation 表达式，需 `VisitArgument` 特殊处理。
3. **构造函数** — 不能加 async，只修复方法组参数。
4. **lambda 委托类型** — 改 async lambda 会变委托类型（`Func<T>` → `Func<Task<T>>`），跳过。
5. **属性 getter / Main** — 不能改 async，跳过。

## 遇到新错误类型的处理流程

1. **回退当前改动**：`git checkout -- <受影响文件>`
2. **分析错误类型**：查看 `dotnet build` 输出的新错误码
3. **增强工具**：
   - 在 `ParseCascadeErrors` 的正则中添加新错误码
   - 在 `CascadeRewriter` 中添加对应的修复策略
   - 如果是跳过场景，添加对应的跳过检测方法
4. **重新运行**：`fix-getawaiter-getresult . --cascade build/sln/JoinCode.slnx`
5. **验证**：全量编译通过后提交

## 文件位置

| 文件 | 说明 |
|------|------|
| `non_deliverables_tools/jcc_audit_ast_cli/fixers/CascadeAsyncFixer.cs` | 状态机级联修复器 |
| `non_deliverables_tools/jcc_audit_ast_cli/fixers/GetAwaiterFixer.cs` | 初始修复器（private 方法改 async） |
| `non_deliverables_tools/jcc_audit_ast_cli/audit/GetAwaiterAnalyzer.cs` | 分析器（输出分类表格） |
| `gen/aot_safety.shared/SyntaxHelpers.cs` | 共享 trivia/格式处理 |
| `gen/aot_safety.shared/RuleDetectors/GetAwaiterPatternDetector.cs` | 共享检测逻辑 |
| `non_deliverables_tools/jcc_audit_ast_cli/core/FileFilter.cs` | 共享文件过滤 |
