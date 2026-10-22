namespace Composition.Tests.Commands;

/// <summary>
/// /code-index 命令单元测试 — 覆盖索引器未注册降级/重建成功(统计渲染)/重建异常降级
/// <para>ICodeIndexer 经 DI 注入;统计经 IndexStats 桩值断言渲染不变量</para>
/// </summary>
[Collection("command-output")]
public sealed class CodeIndexCommandTest {
    private static IndexStats CreateStats() => new() {
        FileCount = 12,
        SymbolCount = 340,
        CallEdgeCount = 56,
        DependencyEdgeCount = 78,
        ProjectCount = 3,
        LastUpdated = DateTimeOffset.UtcNow,
    };

    private static (CodeIndexCommand Command, Mock<ICodeIndexer> Indexer, ChatCommandContext Context) Arrange(
        bool withIndexer = true,
        IndexStats? stats = null,
        Exception? rebuildError = null) {
        var indexer = new Mock<ICodeIndexer>();
        if (rebuildError is not null) {
            indexer.Setup(i => i.RebuildIndexAsync(It.IsAny<CancellationToken>())).ThrowsAsync(rebuildError);
        } else {
            indexer.Setup(i => i.RebuildIndexAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }
        indexer.Setup(i => i.GetStatsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(stats ?? CreateStats());

        var services = CommandTestHarness.CreateServices(
            configureDi: di => {
                if (withIndexer) di.AddSingleton(indexer.Object);
            });
        var context = CommandTestHarness.CreateContext(services);
        return (new CodeIndexCommand(), indexer, context);
    }

    [Fact]
    public async Task Execute_WithoutIndexer_ShowsNotRegistered() {
        var (cmd, indexer, context) = Arrange(withIndexer: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("代码索引服务未注册，无法构建索引");
        indexer.Verify(i => i.RebuildIndexAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_WithIndexer_RebuildsAndRendersStats() {
        var (cmd, indexer, context) = Arrange();

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        indexer.Verify(i => i.RebuildIndexAsync(It.IsAny<CancellationToken>()), Times.Once,
            "应显式触发索引重建");
        output.Should().Contain("正在构建代码索引...");
        output.Should().Contain("代码索引构建完成");
        output.Should().Contain("文件数: 12");
        output.Should().Contain("符号数: 340");
        output.Should().Contain("调用边: 56");
        output.Should().Contain("依赖边: 78");
        output.Should().Contain("项目数: 3");
    }

    [Fact]
    public async Task Execute_RebuildThrows_ShowsFailureWithExceptionMessage() {
        var (cmd, indexer, context) = Arrange(rebuildError: new InvalidOperationException("parse failed"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("代码索引构建失败");
        output.Should().Contain("parse failed");
        indexer.Verify(i => i.GetStatsAsync(It.IsAny<CancellationToken>()), Times.Never,
            "重建失败后不应继续读取统计");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new CodeIndexCommand();
        cmd.Name.Should().Be("code-index");
        cmd.Usage.Should().Be("/code-index");
        cmd.ExposeToMcp.Should().BeTrue();
    }
}
