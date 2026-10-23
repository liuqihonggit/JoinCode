namespace NativeTui.Tests.Repl.Attachments;

/// <summary>
/// 附件集合测试 — 基准 pastedContents 增量登记 + nextPasteIdRef 顺序编号（PromptInput.tsx:1154-1183）、
/// 孤儿修剪（PromptInput.tsx:1185-200 — [Image #N] 占位被删即清理）、摘要文本（[图片: 名.png 123KB]）。
/// </summary>
public sealed class AttachmentListTests
{
    [Fact]
    public void Add_AssignsSequentialIdsAndReturnsPlaceholders()
    {
        var list = new AttachmentList();
        var (first, firstPlaceholder) = list.Add(ImageAttachment.Of("QUJD", filename: "a.png"));
        var (second, secondPlaceholder) = list.Add(ImageAttachment.Of("Qg==", filename: "b.jpg"));

        Assert.Equal(1, first.Id);
        Assert.Equal("[Image #1]", firstPlaceholder);
        Assert.Equal(2, second.Id);
        Assert.Equal("[Image #2]", secondPlaceholder);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Add_OverwritesCallerProvidedId()
    {
        // 登记时分配编号覆盖传入 Id（基准 onImagePaste — pasteId 由 nextPasteIdRef 取，不由内容携带）
        var list = new AttachmentList();
        var (registered, _) = list.Add(ImageAttachment.Of("QUJD") with { Id = 99 });
        Assert.Equal(1, registered.Id);
    }

    [Fact]
    public void TryGet_ReturnsRegisteredAttachmentOrNull()
    {
        var list = new AttachmentList();
        list.Add(ImageAttachment.Of("QUJD", filename: "a.png"));
        Assert.NotNull(list.TryGet(1));
        Assert.Equal("a.png", list.TryGet(1)!.Filename);
        Assert.Null(list.TryGet(2));
    }

    [Fact]
    public void Remove_DeletesById()
    {
        var list = new AttachmentList();
        list.Add(ImageAttachment.Of("QUJD"));
        list.Add(ImageAttachment.Of("Qg=="));
        Assert.True(list.Remove(1));
        Assert.False(list.Remove(1));
        Assert.Single(list.Items);
        Assert.Equal(2, list.Items[0].Id);
    }

    [Fact]
    public void Clear_EmptiesAndResetsSequentialIds()
    {
        // 编号重置对齐 PastedContentTracker.Clear 先例（提交后随输入清空联动）
        var list = new AttachmentList();
        list.Add(ImageAttachment.Of("QUJD"));
        list.Add(ImageAttachment.Of("Qg=="));
        list.Clear();
        Assert.Equal(0, list.Count);
        var (registered, placeholder) = list.Add(ImageAttachment.Of("QUJD"));
        Assert.Equal(1, registered.Id);
        Assert.Equal("[Image #1]", placeholder);
    }

    [Fact]
    public void Items_OrderedByIdAscending()
    {
        var list = new AttachmentList();
        list.Add(ImageAttachment.Of("QUJD", filename: "a.png"));
        list.Add(ImageAttachment.Of("Qg==", filename: "b.png"));
        Assert.Equal(["a.png", "b.png"], list.Items.Select(item => item.Filename).ToArray());
    }

    // ==== 孤儿修剪（基准 PromptInput.tsx:1185-1200）====

    [Fact]
    public void PruneOrphans_RemovesAttachmentsWhosePlaceholderWasDeleted()
    {
        var list = new AttachmentList();
        list.Add(ImageAttachment.Of("QUJD", filename: "a.png"));
        list.Add(ImageAttachment.Of("Qg==", filename: "b.png"));

        // 输入中 [Image #2] 占位被删（backspace/Ctrl+U 等任意编辑）→ #2 成孤儿
        var orphaned = list.PruneOrphans("look at [Image #1] please");

        Assert.Single(orphaned);
        Assert.Equal(2, orphaned[0].Id);
        Assert.Single(list.Items);
        Assert.Equal(1, list.Items[0].Id);
    }

    [Fact]
    public void PruneOrphans_AllPlaceholdersDeleted_RemovesEverything()
    {
        var list = new AttachmentList();
        list.Add(ImageAttachment.Of("QUJD"));
        list.Add(ImageAttachment.Of("Qg=="));

        var orphaned = list.PruneOrphans("no refs left");

        Assert.Equal(2, orphaned.Count);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void PruneOrphans_AllReferenced_NothingRemoved()
    {
        var list = new AttachmentList();
        list.Add(ImageAttachment.Of("QUJD"));
        list.Add(ImageAttachment.Of("Qg=="));

        var orphaned = list.PruneOrphans("[Image #1] and [Image #2]");

        Assert.Empty(orphaned);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void PruneOrphans_TextRefsDoNotProtectImageAttachments()
    {
        // [Pasted text #N] 文本引用编号独立 — 不算 Image 引用（基准 referencedIds 只解析全部引用，
        // 但文本登记在 PastedContentTracker 独立编号空间；Image #1 无对应文本引用即孤儿）
        var list = new AttachmentList();
        list.Add(ImageAttachment.Of("QUJD"));

        var orphaned = list.PruneOrphans("[Pasted text #1 +3 lines]");

        Assert.Single(orphaned);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void PruneOrphans_UnregisteredRefInInput_NoSideEffect()
    {
        var list = new AttachmentList();
        list.Add(ImageAttachment.Of("QUJD"));

        // 输入含未登记编号引用（历史残留）— 已登记 #1 仍被引用不受影响
        var orphaned = list.PruneOrphans("[Image #1] [Image #42]");

        Assert.Empty(orphaned);
        Assert.Single(list.Items);
    }

    [Fact]
    public void PruneOrphans_EmptyInput_RemovesAll()
    {
        var list = new AttachmentList();
        list.Add(ImageAttachment.Of("QUJD"));
        Assert.Single(list.PruneOrphans(string.Empty));
        Assert.Equal(0, list.Count);
    }

    // ==== 摘要文本（w2 附件列表展示形态）====

    [Theory]
    [InlineData("QUJD", "[图片: a.png 3B]")]
    [InlineData("QUJDQUJD", "[图片: a.png 6B]")]
    public void FormatSummary_ByteSizes(string base64, string expected)
    {
        Assert.Equal(expected, AttachmentList.FormatSummary(ImageAttachment.Of(base64, filename: "a.png")));
    }

    [Fact]
    public void FormatSummary_KilobytesAndMegabytes()
    {
        // 123KB = 125952 字节（base64 长度 167936 无填充）
        var kb = new string('A', 125952 / 3 * 4);
        Assert.Equal("[图片: shot.png 123KB]", AttachmentList.FormatSummary(ImageAttachment.Of(kb, filename: "shot.png")));

        // 1.5MB = 1572864 字节
        var mb = new string('A', 1572864 / 3 * 4);
        Assert.Equal("[图片: big.png 1.5MB]", AttachmentList.FormatSummary(ImageAttachment.Of(mb, filename: "big.png")));
    }

    [Fact]
    public void FormatSummary_DefaultFilenameWhenUnspecified()
    {
        Assert.StartsWith("[图片: Pasted image ", AttachmentList.FormatSummary(ImageAttachment.Of("QUJD")));
    }
}
