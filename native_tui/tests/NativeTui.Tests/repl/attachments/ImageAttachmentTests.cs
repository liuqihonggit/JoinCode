namespace NativeTui.Tests.Repl.Attachments;

/// <summary>
/// 图片附件 DTO 测试 — 基准 PastedContent image 形态（config.ts:54-62）+ onImagePaste
/// 构造默认值（PromptInput.tsx:1159/1161 mediaType/filename 兜底）+ formatImageRef 占位
///（history.ts:58-60）+ base64 字节数换算。
/// </summary>
public sealed class ImageAttachmentTests
{
    [Fact]
    public void Of_DefaultsMediaTypeAndFilename()
    {
        var attachment = ImageAttachment.Of("QUJD");
        Assert.Equal(0, attachment.Id);
        Assert.Equal("QUJD", attachment.Base64);
        Assert.Equal("image/png", attachment.MediaType);
        Assert.Equal("Pasted image", attachment.Filename);
        Assert.Null(attachment.SourcePath);
        Assert.Null(attachment.Dimensions);
        Assert.Null(attachment.CachePath);
    }

    [Fact]
    public void Of_ExplicitValues_Preserved()
    {
        var dimensions = new ImageDimensions(OriginalWidth: 1920, OriginalHeight: 1080, DisplayWidth: 800, DisplayHeight: 450);
        var attachment = ImageAttachment.Of("QUJD", "image/jpeg", "photo.jpg", "/tmp/photo.jpg", dimensions);
        Assert.Equal("image/jpeg", attachment.MediaType);
        Assert.Equal("photo.jpg", attachment.Filename);
        Assert.Equal("/tmp/photo.jpg", attachment.SourcePath);
        Assert.Equal(dimensions, attachment.Dimensions);
    }

    [Fact]
    public void Dimensions_RecordEquality()
    {
        Assert.Equal(
            new ImageDimensions(100, 200, 50, 100),
            new ImageDimensions(OriginalWidth: 100, OriginalHeight: 200, DisplayWidth: 50, DisplayHeight: 100));
    }

    [Theory]
    [InlineData("QUJD", 3L)]      // "ABC" → 3 字节，无填充
    [InlineData("QUI=", 2L)]      // "AB" → 2 字节，1 填充
    [InlineData("QQ==", 1L)]      // "A" → 1 字节，2 填充
    public void ByteSize_Base64PaddingAccounted(string base64, long expectedBytes)
    {
        Assert.Equal(expectedBytes, ImageAttachment.Of(base64).ByteSize);
    }

    [Fact]
    public void Placeholder_RegisteredId_FormatsImageRef()
    {
        // 基准 formatImageRef — history.ts:58-60
        var registered = ImageAttachment.Of("QUJD") with { Id = 3 };
        Assert.Equal("[Image #3]", registered.Placeholder);
    }

    [Fact]
    public void Placeholder_UnregisteredId_FallsBackToBareLabel()
    {
        // 基准 UserImageMessage.tsx:26 — imageId 无效时 [Image]
        Assert.Equal("[Image]", ImageAttachment.Of("QUJD").Placeholder);
    }
}
