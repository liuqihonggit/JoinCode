namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// DSH 前端资产缓存校验（TASK066 C1）：旧缓存缺懒加载分片时必须判定不完整并重新解包。
/// </summary>
public sealed class DshWebAssetsTests {
    /// <summary>有 meta.json 但缺懒加载分片 → 不完整（触发重新解包）</summary>
    [Fact]
    public async Task IsComplete_MissingLazyChunks_ReturnsFalse() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        await fs.WriteAllText("cache/meta.json", "{}");

        DshWebAssets.IsComplete(fs, "cache").Should().BeFalse();
    }

    /// <summary>完成标记与三个懒加载分片都在 → 完整（复用缓存）</summary>
    [Fact]
    public async Task IsComplete_WithMarkerAndChunks_ReturnsTrue() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();
        await fs.WriteAllText("cache/meta.json", "{}");
        foreach (var chunk in new[] {
                     "plugins/@deepseek-ai/dsh-client-ui-sidebar-terminal/client.terminal.js",
                     "plugins/@deepseek-ai/dsh-client-ui-sidebar-documentpreview/client.pdf.js",
                     "plugins/@deepseek-ai/dsh-client-ui-sidebar-documentpreview/client.excel.js",
                 }) {
            await fs.WriteAllText($"cache/{chunk}", "// chunk");
        }

        DshWebAssets.IsComplete(fs, "cache").Should().BeTrue();
    }

    /// <summary>缺完成标记 → 不完整</summary>
    [Fact]
    public async Task IsComplete_WithoutMarker_ReturnsFalse() {
        await using var fs = new IO.FileSystem.InMemoryFileSystem();

        DshWebAssets.IsComplete(fs, "cache").Should().BeFalse();
    }
}
