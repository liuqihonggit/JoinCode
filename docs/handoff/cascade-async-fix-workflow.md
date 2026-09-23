# 级联异步修复工作流交接文档

> 最后更新: 2026-09-24

## 目标

将同步 private 方法中的 `.GetAwaiter().GetResult()` 改为 `async + await`，用 ast_cli 工具递归处理所有级联编译错误，最终逐个重新启用分析器（JCC3017/JCC3019）修复违规，推送到 PR #276。

## 循环调试手法（核心流程）

### 完整修复流程

```
1. fix-getawaiter-getresult .          → 初始修复 51 处（只改 private 方法）
2. fix-from-build-errors slnx          → 第1轮级联修复
3. fix-from-build-errors slnx          → 第2轮...直到收敛（修复0处）
4. dotnet build slnx > log.txt 2>&1    → 编译验证，完整日志落盘
5. grep "error CS" log.txt | sort -u   → 分析剩余错误
6. 增强工具覆盖未处理场景 → commit 工具 → goto 2
```

### 自动循环脚本

```bash
# 初始修复
dotnet artifacts/bin/JccAuditCli/Debug/net10.0/jcc-audit.dll fix-getawaiter-getresult .

# 自动循环最多10轮
for i in $(seq 1 10); do
  echo "=== 第 $i 轮 ==="
  dotnet artifacts/bin/JccAuditCli/Debug/net10.0/jcc-audit.dll fix-from-build-errors build/sln/JoinCode.slnx 2>&1 | grep -E "修复文件|修复问题|发现"
done
```

### 编译验证纪律

```bash
# 编译日志必须完整保存不过滤，先落盘再分析
dotnet build build/sln/JoinCode.slnx --no-restore > build_log.txt 2>&1
echo "退出码: $?"
grep -c "error CS" build_log.txt
grep "error CS" build_log.txt | grep -oP "error CS\d+" | sort | uniq -c | sort -rn
```

## 工具增强历程

### BuildErrorFixer.cs Visit 方法清单

| Visit 方法 | 处理错误码 | 场景 | 修复方式 |
|-----------|-----------|------|---------|
| `VisitExpressionStatement` | CS4014 | 裸语句调用未 await | 加 await 或 .GetAwaiter().GetResult() |
| `VisitVariableDeclarator` | CS0029 | 变量声明 Task<T>→T | 加 await 或 .GetAwaiter().GetResult() |
| `VisitSwitchExpressionArm` | CS0029 | switch 表达式 arm | .GetAwaiter().GetResult() |
| `VisitBinaryExpression` | CS0019 | `??` 运算符 | .GetAwaiter().GetResult() 或 ?.GetAwaiter().GetResult() |
| `VisitArgument` | CS1503 | 方法组转换/参数不匹配 | lambda 包装或 .GetAwaiter().GetResult() |
| `VisitAssignmentExpression` | CS0029 | 赋值表达式 | 加 await 或 .GetAwaiter().GetResult() |
| `VisitReturnStatement` | CS0029 | return 语句 | .GetAwaiter().GetResult() |
| `VisitSimpleLambdaExpression` | CS4034 | lambda 内 await 但无 async | 给 lambda 加 async 修饰符 |
| `VisitParenthesizedLambdaExpression` | CS4034 | 同上 | 同上 |
| `VisitMethodDeclaration` | — | 给 private 方法加 async | 改返回类型 void→Task, T→Task<T> |

### 关键守卫逻辑

1. **`IsInNoAsyncContext`** — 检测构造函数/属性 getter/public/internal 方法，用 .GetAwaiter().GetResult() 代替 await
2. **`IsTestMethod`** — 检测 [Fact]/[Theory] 特性，测试方法虽 public 但可改 async（避免 xUnit1031）
3. **`VisitExpressionStatement` 跳过 lambda 内裸语句** — 由 VisitSimpleLambdaExpression 处理
4. **`VisitVariableDeclarator` 跳过数组类型变量** — `var paths = new[] { ... }` 不加 .GetAwaiter().GetResult()
5. **`VisitBinaryExpression` IdentifierName 用 `?.`** — `pathResult2?.GetAwaiter().GetResult()` 避免空引用

### 正则收集错误码

```csharp
@"^(.+?)\((\d+),(\d+)\):\s*error\s+(CS4014|CS0029|CS1503|CS1061|CS0019)"
```

## 已知问题与解决方案

### 1. 工具改动被 git checkout 回滚

**问题**: `git checkout -- .` 会回滚所有未提交改动，包括工具改动
**解决**: 工具改好先 commit，然后才批量修改/回滚生产代码
```bash
# 正确流程
dotnet build tool.csproj → git add tool → git commit → git checkout -- app/ kit/ lib/
# 错误流程
git checkout -- .  # 工具改动也回滚了！
```

### 2. 异步传播过度修复

**问题**: `fix-from-build-errors` 给非 Task 变量加 .GetAwaiter().GetResult()，给 lambda 内加 await 但没给 lambda 加 async
**解决**:
- `VisitExpressionStatement` 限定 CS4014，跳过 lambda 内裸语句
- `VisitVariableDeclarator` 限定 CS0029，跳过数组类型变量
- 添加 `VisitSimpleLambdaExpression`/`VisitParenthesizedLambdaExpression` 给 lambda 加 async

### 3. switch 表达式 arm 未覆盖

**问题**: `AgentMemoryScope.Local => GetLocalAgentMemoryDir(dirName)` 返回 Task<string> 但需要 string
**解决**: 添加 `VisitSwitchExpressionArm` 处理 CS0029

### 4. `??` 运算符未覆盖

**问题**: `pathResult ?? pathResult2` 中 pathResult2 是 Task<string?>，?? 运算符类型不匹配
**解决**: 添加 `VisitBinaryExpression` 处理 CS0019，IdentifierName 用 `?.GetAwaiter().GetResult()`

### 5. xUnit1031 测试方法中禁止阻塞

**问题**: 工具在测试方法中用 .GetAwaiter().GetResult()，xUnit 分析器禁止
**解决**: `IsInNoAsyncContext` 排除 [Fact]/[Theory] 测试方法，测试方法可改 async

## 测试流程

### 单元测试验证工具行为

用文件内容作为单元测试输入字符串，断言工具输出，禁止加 Console.WriteLine 调试输出。

```csharp
// 示例：验证 VisitSwitchExpressionArm 处理 switch 表达式 arm
var source = """
    public string GetDir(string scope) {
        return scope switch {
            "local" => GetLocalDir(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
    private async Task<string> GetLocalDir() { return ""; }
    """;
var errorMap = new Dictionary<int, string> { [3] = "CS0029" };
var rewriter = new UnawaitedVariableRewriter(errorMap, isTestFile: false);
var tree = CSharpSyntaxTree.ParseText(source);
var root = tree.GetRoot();
var newRoot = rewriter.Visit(root);
Assert.True(rewriter.FixedCount > 0);
Assert.Contains(".GetAwaiter().GetResult()", newRoot.ToFullString());
```

### 编译验证流程

1. `dotnet build build/sln/JoinCode.slnx --no-restore > log.txt 2>&1` — 完整日志落盘
2. `grep -c "error CS" log.txt` — 统计错误数
3. `grep "error CS" log.txt | sort -u` — 查看去重错误
4. `grep "error CS" log.txt | grep -oP "error CS\d+" | sort | uniq -c | sort -rn` — 统计错误类型分布

## 当前状态（2026-09-24）

### 已 commit 的工具改动

1. `d0ba3d889` — VisitSwitchExpressionArm/VisitBinaryExpression + 限定错误码 + CS0019 正则
2. `96485672c` — lambda async + 数组变量跳过 + lambda 内裸语句跳过

### 待完成

1. **运行完整修复流程** — fix-getawaiter-getresult + fix-from-build-errors 多轮
2. **处理剩余 6 个错误** — CS4034(lambda async) + CS1660(lambda→AgentBase) + CS0826/CS1061(测试文件数组)
3. **全量编译通过后 commit 生产代码**
4. **JCC3017 重新启用 + 修复违规**
5. **JCC3019 重新启用 + 修复违规**
6. **推送修复到 PR #276**

### 工具命令速查

```bash
# 工具路径
TOOL=artifacts/bin/JccAuditCli/Debug/net10.0/jcc-audit.dll

# 初始修复 .GetAwaiter().GetResult() → async + await
dotnet $TOOL fix-getawaiter-getresult .

# 编译错误驱动修复（CS4014/CS0029/CS1503/CS1061/CS0019）
dotnet $TOOL fix-from-build-errors build/sln/JoinCode.slnx

# 分析 .GetAwaiter().GetResult() 分布
dotnet $TOOL analyze-getawaiter .
```
