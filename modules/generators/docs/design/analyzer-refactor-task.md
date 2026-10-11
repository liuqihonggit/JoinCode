# 语法分析器重构 — 状态机/守卫/反射

## 目标
降低 AsyncSafetyRules.cs (1012行) 复杂度,用状态机/守卫/反射模式重构为每规则独立类。

## 步骤

### 1. 基础设施
- [ ] `IAnalyzerRule` 接口 — 统一规则接口
- [ ] `ProjectContext` — 封装 ProjectType + IsLibrary/IsTest/IsUi
- [ ] `AnalyzerRuleAttribute` — 特性标记规则类
- [ ] `GuardChain` — 守卫链替代 if-else
- [ ] `RuleRegistry` — 反射自动发现规则

### 2. 样板规则
- [ ] `ConfigureAwaitFalseRule` (JCC3008) — 第一个迁移的规则
- [ ] 反射注册验证 — 确认 RuleRegistry.Discover() 能发现规则
- [ ] 编译验证 — 确认样板规则工作正常

### 3. 推广到所有规则
- [ ] JCC2001-2003 (Console.ReadLine/ReadKey/Read) — InteractiveInputRule
- [ ] JCC3003-3004 (ProcessDeadlock/UnreadStderr) — ProcessSafetyRule
- [ ] JCC3005 (AsyncVoid) — AsyncVoidRule
- [ ] JCC3006 (BlockingAsyncCall) — BlockingAsyncCallRule
- [ ] JCC3007 (SequentialAwaitInLoop) — SequentialAwaitRule
- [ ] JCC3008 (ConfigureAwaitFalse) — 已做样板
- [ ] JCC3009 (ConfigureAwaitTrueForTests) — ConfigureAwaitTestRule
- [ ] JCC3010-3012 (TaskDelayInTests) — TaskDelayTestRule
- [ ] JCC3013 (EmptyCatchBlock) — EmptyCatchRule
- [ ] JCC3014 (ConfigureAwaitUi) — ConfigureAwaitUiRule

### 4. 清理
- [ ] 删除 AsyncSafetyRules.cs 中已迁移的代码
- [ ] 删除 AotSafetyHelpers.IsInsideTestMethod (已无调用者)
- [ ] 处理 JsonSerializerAotRules.cs 和 AotSafetyRules.cs 中的 IsInsideTestMethod 调用

### 5. 验证
- [ ] 全量编译 0 错误 0 警告
- [ ] 单元测试通过
- [ ] git commit

## 设计

### IAnalyzerRule 接口
```csharp
public interface IAnalyzerRule {
    void Register(CompilationStartAnalysisContext context, ProjectContext projectContext);
}
```

### 反射注册
```csharp
private static readonly IAnalyzerRule[] Rules = typeof(IAnalyzerRule).Assembly.GetTypes()
    .Where(t => t.GetCustomAttribute<AnalyzerRuleAttribute>() is not null)
    .Where(t => typeof(IAnalyzerRule).IsAssignableFrom(t) && !t.IsAbstract)
    .Select(t => (IAnalyzerRule)Activator.CreateInstance(t)!)
    .ToArray();
```

### 守卫链
```csharp
if (GuardChain.Create(ctx)
    .RequireNotCancellationRequested()
    .Require(projectContext.IsLibrary)
    .RequireNotTaskYield()
    .Failed) return;
```

## 状态
- 当前: AsyncSafetyRules.cs 1012行,13规则堆在一个类
- 目标: 每规则独立类,反射自动注册,守卫链替代if-else
