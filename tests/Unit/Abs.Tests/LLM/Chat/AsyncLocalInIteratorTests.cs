namespace JoinCode.Abs.Tests.LLM.Chat;

/// <summary>
/// AsyncLocal 在异步迭代器内的行为验证（平台行为固化测试）—
/// 结论：迭代器段内 Set 的 AsyncLocal 在后续 MoveNext 段（yield 恢复后）不可见。
/// 因此 SubAgentEventChannel 禁止在 QueryLoop 迭代器内走环境态，
/// 排空侧必须经 ChatMiddlewareContext.SubAgentEvents 显式传递（2026-08-26 实测）。
/// </summary>
public class AsyncLocalInIteratorTests
{
    private static readonly AsyncLocal<string?> Probe = new();

    [Fact]
    public async Task AsyncLocal_SetInsideIterator_ShouldBeInvisibleAfterYieldResumes()
    {
        static async IAsyncEnumerable<int> Iterator()
        {
            Probe.Value = "inside";
            yield return 1;
            yield return Probe.Value == "inside" ? 2 : -2;
        }

        var seen = new List<int>();
        await foreach (var v in Iterator())
        {
            seen.Add(v);
        }

        // yield 恢复后的段运行在 MoveNext 时捕获的上下文中，段内 Set 不跨段存活
        seen.Should().Equal([1, -2]);
    }
}
