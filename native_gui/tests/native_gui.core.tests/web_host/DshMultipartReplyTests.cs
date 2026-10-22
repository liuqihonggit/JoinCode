namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// multipart 一元响应契约测试：字节 Blob 必须按 DSH gateway 布局回写，
/// 且 Content-Length 与响应体一致（写入流末尾指针事故会表现为客户端无限等待）。
/// </summary>
public sealed class DshMultipartReplyTests {
    [Fact]
    public void BuildMultipartPayload_EmitsMetadataAndRawBytesPart() {
        const string boundary = "dsh-testboundary";
        var raw = new byte[] { 0x00, 0x01, 0xFF, 0x7F, (byte)'P', (byte)'D', (byte)'F' };
        var value = JsonSerializer.SerializeToElement(new { data = (string?)null, eof = true });
        var metadata = new DshRpcMultipartMetadata {
            RpcId = "rpc-1",
            Result = new DshRpcResult { Ok = true, Value = value },
            Attachments = [new DshRpcAttachmentRef { Path = ["data"], Part = "bytes-0" }],
        };

        var payload = DshWebHost.BuildMultipartPayload(boundary, "bytes-0", metadata, raw);

        var text = Encoding.ASCII.GetString(payload);
        text.Should().StartWith($"--{boundary}\r\nContent-Disposition: form-data; name=\"metadata\"\r\n\r\n");
        text.Should().EndWith($"\r\n--{boundary}--\r\n");
        var metadataStart = text.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4;
        var metadataEnd = text.IndexOf($"\r\n--{boundary}\r\n", StringComparison.Ordinal);
        var envelope = JsonSerializer.Deserialize(text[metadataStart..metadataEnd], DshJsonContext.Default.DshRpcMultipartMetadata)
            ?? throw new Xunit.Sdk.XunitException("metadata JSON 解析失败");
        envelope.Type.Should().Be("server-response");
        envelope.RpcId.Should().Be("rpc-1");
        envelope.Result.Ok.Should().BeTrue();
        envelope.Result.Value.GetValueOrDefault().GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        var attachment = envelope.Attachments.Should().ContainSingle().Subject;
        attachment.Codec.Should().Be("bytes");
        attachment.Part.Should().Be("bytes-0");
        attachment.Path.Should().Equal("data");

        var blobHeaderEnd = text.IndexOf("\r\nContent-Type: application/octet-stream\r\n\r\n", StringComparison.Ordinal)
            + "\r\nContent-Type: application/octet-stream\r\n\r\n".Length;
        payload.AsSpan(blobHeaderEnd, raw.Length).ToArray().Should().Equal(raw);
        payload.Length.Should().Be(blobHeaderEnd + raw.Length + Encoding.ASCII.GetByteCount($"\r\n--{boundary}--\r\n"));
    }
}
