using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

[PromptSection(
    Name = "performance_audit",
    Keywords = new[] { "GC压力", "字符串GC", "性能优化", "性能问题", "零分配", "技术债", "LINQ链式", "字符串拼接", "AsParallel", "Task.WhenAll", "GC", "Span" },
    InjectOn = PromptSectionInject.Keyword,
    Order = 60)]
public static class PerformanceAuditSection
{
    public static string GetContent()
    {
        return """
# 性能审计规则

每次完成任务后，你必须执行diff检查是否存在以下性能问题。如有，推荐用户下一步解决：

## 字符串GC问题

- 循环内 += 拼接 → 改用 StringBuilder
- Substring/Split 分配 → 改用 Span<T> + Range 切片，零分配
- Path.GetFileName → 用 AsSpan().LastIndexOfAny() + Range 切片替代
- ToLowerInvariant/ToUpperInvariant 做比较 → 改用 StringComparison.OrdinalIgnoreCase
- string.Join/Concat 优先于手动拼接

## 遍历优化

- for/foreach 循环 → 改为 LINQ 链式编程模式，便于评估数据量和加入 .AsParallel()
- 大数据集 → 加 .AsParallel() 并行化
- 延迟执行优于即时求值（IEnumerable vs List）
- 超过5行LINQ链需提取函数

## 异步并行

- list.Add(await ...) 模式 → 改用 Select(async).ToArray() + Task.WhenAll
- Task.WhenAll 等价于异步版 AsParallel()
- 每个 Stopwatch 独立计时互不干扰
- 考虑 ValueTask 减少分配

## 集合优化

- .ToList().AsReadOnly() 双重分配 → 改用 IReadOnlyList<T> 返回类型
- 选择正确类型：ConcurrentDictionary vs ConcurrentBag
- FrozenDictionary 替代 switch-case 硬编码

## 公开方法设计原则

- 公开方法变为薄包装层，核心指标计算只在一处维护
- 这就是"改一个地方其他自然会修改"

## Span<T> 限制说明

- ReadOnlySpan<T> 是 ref struct，不能在 lambda 中捕获
- 此时保留 for 循环，但改用索引访问替代 foreach，确保零额外分配
- Memory<T> 可在 lambda 中捕获，但有一次分配

## 可用MCP工具

- `optimize_code`: 分析并优化 C# 代码性能和可读性
- `find_bugs`: 查找 C# 代码中的潜在错误和问题
- `analyze_csharp_code`: 分析 C# 代码质量并提供改进建议
""";
    }

    public static SystemPromptSection Create()
    {
        return SystemPromptSection.Cached("performance_audit", GetContent);
    }
}
