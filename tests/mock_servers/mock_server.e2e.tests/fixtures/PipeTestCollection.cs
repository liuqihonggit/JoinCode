
namespace MockServer.E2E.Tests.Fixtures;

/// <summary>
/// 管道测试集合定义
/// 使用 PipeMockServerFixture 作为共享上下文
/// </summary>
[CollectionDefinition(nameof(PipeTestCollection))]
public sealed class PipeTestCollection : ICollectionFixture<PipeMockServerFixture>
{
    // 此类仅用于标记集合定义
    // ICollectionFixture 接口的实现由 xUnit 自动处理
}
