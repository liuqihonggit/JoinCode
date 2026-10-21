namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// 附件链测试（TASK058 P2-B2）：fileUploads/upload → receipt 落盘 → session/attachment 读取。
/// mediaType 魔数探测（PNG/JPEG/GIF/WEBP）+ 尺寸头解析；非四类图片拒绝上传（前端 zod 值域如此）。
/// </summary>
public sealed class DshAttachmentTests {
    /// <summary>1x1 红色 PNG 字节（合法 PNG：签名+IHDR+IDAT+IEND）</summary>
    private static readonly byte[] PngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>GIF89a 1x1 透明像素</summary>
    private static readonly byte[] GifBytes = Convert.FromBase64String(
        "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    /// <summary>非图片字节（文本）</summary>
    private static readonly byte[] TextBytes = "hello attachment"u8.ToArray();

    private static (DshHostServices Services, string AgentId) CreateServices() {
        var services = new DshHostServices(new InMemoryFileSystem());
        return (services, "agent-1");
    }

    private static JsonElement UploadArgs(string agentId, byte[] data, string? name = "pic.png") =>
        JsonSerializer.SerializeToElement(new {
            agentId,
            request = new { data = Convert.ToBase64String(data), name },
        });

    [Fact]
    public async Task Upload_Png_ReturnsReceiptAndFileMeta() {
        var (services, agentId) = CreateServices();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("fileUploads/upload", UploadArgs(agentId, PngBytes));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        value.GetProperty("receiptId").GetString().Should().NotBeNullOrEmpty();
        var file = value.GetProperty("file");
        file.GetProperty("attachmentId").GetString().Should().NotBeNullOrEmpty();
        file.GetProperty("name").GetString().Should().Be("pic.png");
        file.GetProperty("bytes").GetInt32().Should().Be(PngBytes.Length);
    }

    [Fact]
    public async Task Upload_NonImage_ReturnsStableRejection() {
        var (services, agentId) = CreateServices();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("fileUploads/upload", UploadArgs(agentId, TextBytes, "note.txt"));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("fileUploads/rejected");
        result.Error?.Message.Should().Contain("图片", "前端 zod 值域仅四种图片类型");
    }

    [Fact]
    public async Task Attachment_RoundTrips_PngWithDimensions() {
        var (services, agentId) = CreateServices();
        var dispatcher = new DshRpcDispatcher(services);
        var upload = await dispatcher.HandleAsync("fileUploads/upload", UploadArgs(agentId, PngBytes));
        var receiptId = upload.Value.GetValueOrDefault().GetProperty("receiptId").GetString()
            ?? throw new Xunit.Sdk.XunitException("upload 未返回 receiptId");

        var result = await dispatcher.HandleAsync("session/attachment", JsonSerializer.SerializeToElement(new {
            sessionId = agentId,
            attachmentId = receiptId,
        }));

        result.Ok.Should().BeTrue(result.Error?.Message);
        var value = result.Value.GetValueOrDefault();
        var attachment = value.GetProperty("attachment");
        attachment.GetProperty("mediaType").GetString().Should().Be("image/png");
        attachment.GetProperty("bytes").GetInt32().Should().Be(PngBytes.Length);
        attachment.GetProperty("width").GetInt32().Should().Be(1);
        attachment.GetProperty("height").GetInt32().Should().Be(1);
        var data = value.GetProperty("data").GetString()
            ?? throw new Xunit.Sdk.XunitException("data 缺失");
        Convert.FromBase64String(data).Should().Equal(PngBytes);
    }

    [Fact]
    public async Task Attachment_GifDetected() {
        var (services, agentId) = CreateServices();
        var dispatcher = new DshRpcDispatcher(services);
        var upload = await dispatcher.HandleAsync("fileUploads/upload", UploadArgs(agentId, GifBytes, "dot.gif"));
        var receiptId = upload.Value.GetValueOrDefault().GetProperty("receiptId").GetString()
            ?? throw new Xunit.Sdk.XunitException("upload 未返回 receiptId");

        var result = await dispatcher.HandleAsync("session/attachment", JsonSerializer.SerializeToElement(new {
            sessionId = agentId,
            attachmentId = receiptId,
        }));

        result.Ok.Should().BeTrue(result.Error?.Message);
        result.Value.GetValueOrDefault().GetProperty("attachment").GetProperty("mediaType").GetString()
            .Should().Be("image/gif");
    }

    [Fact]
    public async Task Attachment_UnknownId_ReturnsNotFound() {
        var (services, agentId) = CreateServices();
        var dispatcher = new DshRpcDispatcher(services);

        var result = await dispatcher.HandleAsync("session/attachment", JsonSerializer.SerializeToElement(new {
            sessionId = agentId,
            attachmentId = "no-such-receipt",
        }));

        result.Ok.Should().BeFalse();
        result.Error?.Code.Should().Be("session/attachment-not-found");
    }
}
