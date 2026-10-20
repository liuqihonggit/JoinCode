namespace Core.Configuration.ModelFetch.Tests;

/// <summary>
/// SettingsJsonModelWriter 模型持久化路径测试（修复：模型列表写回 settings.json 内联 vendor 块 —
/// 违反用户布局"settings.json 只留当前配置"，巨量模型列表膨胀到 29KB+）—
/// 模型持久化必须落 providers_llm/{vendor}/models.json（VendorModelsFile DTO，TASK054 拆分布局），
/// settings.json 零触碰（模型更新不涉及 current 指针）。
/// </summary>
public class SettingsJsonModelWriterTests {
    /// <summary>写回落点 — 每个供应商写 providers_llm/{name}/models.json，不写 settings.json。</summary>
    [Fact]
    public async Task WriteAsync_WritesVendorModelsSubFiles_NotSettingsJson() {
        var captured = new List<string>();
        var (writer, _) = CreateWriter(captured);
        var updates = new Dictionary<string, List<ModelItemConfig>> {
            ["openai"] = [new ModelItemConfig { Id = "gpt-5.6-sol" }],
            ["zhipu"] = [new ModelItemConfig { Id = "glm-5.2" }],
        };

        await writer.WriteAsync(updates);

        captured.Should().Equal(
            $"{TestBaseDir}/providers_llm/openai/models.json",
            $"{TestBaseDir}/providers_llm/zhipu/models.json");
        captured.Should().NotContain(path => path.EndsWith("settings.json", StringComparison.Ordinal));
    }

    /// <summary>写入内容 — VendorModelsFile 包裹（{"models":[...]}）而非裸数组。</summary>
    [Fact]
    public async Task WriteAsync_ContentIsVendorModelsFileEnvelope() {
        var captured = new List<(string Path, string Content)>();
        var (writer, _) = CreateWriter(paths: null, contents: captured);
        var updates = new Dictionary<string, List<ModelItemConfig>> {
            ["zhipu"] = [new ModelItemConfig { Id = "glm-5.2" }],
        };

        await writer.WriteAsync(updates);

        captured.Should().HaveCount(1);
        captured[0].Content.Should().Contain("\"models\"");
        captured[0].Content.Should().Contain("glm-5.2");
    }

    /// <summary>空更新 — 零写入。</summary>
    [Fact]
    public async Task WriteAsync_NoUpdates_NoWrite() {
        var captured = new List<string>();
        var (writer, _) = CreateWriter(captured);

        await writer.WriteAsync(new Dictionary<string, List<ModelItemConfig>>());

        captured.Should().BeEmpty();
    }

    private const string TestBaseDir = "/test-jcc";

    /// <summary>构造写回器 + 路径捕获 mock fs（CreateStream 走 MemoryStream 包装，不落盘）。</summary>
    private static (SettingsJsonModelWriter Writer, Mock<IFileSystem> Fs) CreateWriter(
        List<string>? paths = null, List<(string, string)>? contents = null) {
        var mock = new Mock<IFileSystem>();
        mock.Setup(x => x.CreateDirectory(It.IsAny<string>()));
        mock.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(true);
        mock.Setup(x => x.CreateStream(It.IsAny<string>(), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>()))
            .Returns<string, FileMode, FileAccess, FileShare>((path, _, _, _) => {
                paths?.Add(path.Replace('\\', '/'));
                if (contents is not null) {
                    return new CapturingStream(new MemoryStream(), content => contents.Add((path, content)));
                }
                return new MemoryStream();
            });
        var writer = new SettingsJsonModelWriter(mock.Object, baseDir: TestBaseDir);
        return (writer, mock);
    }

    /// <summary>捕获写入内容的流包装（Dispose 时快照 — 双重 Dispose 幂等只捕获一次）。</summary>
    private sealed class CapturingStream(MemoryStream inner, Action<string> onClosed) : Stream {
        private bool _captured;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) {
            if (disposing && !_captured) {
                _captured = true;
                onClosed(System.Text.Encoding.UTF8.GetString(inner.ToArray()));
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
