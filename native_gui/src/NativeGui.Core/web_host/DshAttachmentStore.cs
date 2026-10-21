namespace JoinCode.Gui.WebHost;

/// <summary>
/// 附件存储（TASK058 P2-B2）：fileUploads/upload 的 receipt 落盘与 session/attachment 的读取。
/// 存储 = <c>{dir}/{owner}/ 索引 json + {owner}/{receiptId}.bin</c>；mediaType 由魔数探测
/// （PNG/JPEG/GIF/WEBP 四类，前端 zod 值域）；宽高由文件头解析（手写字节读取，AOT 安全零反射）。
/// receiptId 同时充当 attachmentId（v1 单层寻址，前端两字段透传一致）。
/// </summary>
internal sealed class DshAttachmentStore(IFileSystem fs, string? directory = null) {
    private readonly string _directory = directory
        ?? Path.Combine(AppDataPaths.Default.JccDirectory, "dsh_attachments");

    /// <summary>上传：base64 数据 → 魔数校验 → 落盘 → receipt 元数据</summary>
    internal async Task<DshRpcResult> UploadAsync(string owner, string base64Data, string? name, CancellationToken cancellationToken) {
        byte[] bytes;
        try {
            bytes = Convert.FromBase64String(base64Data);
        } catch (FormatException) {
            return DshRpcResults.Fail("fileUploads/rejected", "data 不是合法 base64；请用 FileReader.readAsDataURL 的纯 base64 部分");
        }
        if (bytes.Length == 0) {
            return DshRpcResults.Fail("fileUploads/rejected", "附件内容为空；请选择非空文件重试");
        }
        var mediaType = SniffMediaType(bytes);
        if (mediaType is null) {
            return DshRpcResults.Fail("fileUploads/rejected",
                "前端仅支持图片附件（image/png、image/jpeg、image/webp、image/gif 四类）；请转换格式后重传");
        }
        var receiptId = Guid.NewGuid().ToString("N");
        var ownerDir = Path.Combine(_directory, owner);
        if (!fs.DirectoryExists(ownerDir)) {
            fs.CreateDirectory(ownerDir);
        }
        await fs.WriteAllBytesAsync(Path.Combine(ownerDir, $"{receiptId}.bin"), bytes, cancellationToken);
        var record = new DshAttachmentRecord {
            AttachmentId = receiptId,
            Name = string.IsNullOrWhiteSpace(name) ? "attachment" : name,
            MediaType = mediaType,
            Bytes = bytes.Length,
            Width = ReadWidth(bytes) ?? 0,
            Height = ReadHeight(bytes) ?? 0,
        };
        await fs.WriteAllTextAsync(Path.Combine(ownerDir, $"{receiptId}.json"),
            RelaxedJsonSerializer.Serialize(record, DshJsonContext.Default), cancellationToken);
        return DshRpcResults.Ok(JsonSerializer.SerializeToElement(
            new DshFileUploadResult {
                ReceiptId = receiptId,
                File = new DshUploadedFileInfo {
                    AttachmentId = receiptId,
                    Name = record.Name,
                    Bytes = record.Bytes,
                },
            },
            DshJsonContext.Default.DshFileUploadResult));
    }

    /// <summary>读取：receiptId → 元数据 + base64 数据（前端聊天内图片渲染）</summary>
    internal async Task<DshRpcResult> ReadAsync(string owner, string attachmentId, CancellationToken cancellationToken) {
        if (!IsValidReceiptId(attachmentId) || !IsValidOwner(owner)) {
            return DshRpcResults.Fail("session/attachment-not-found",
                $"附件 {attachmentId} 不存在（ID 非法）；请重新上传后用新 receiptId 引用");
        }
        var recordPath = Path.Combine(_directory, owner, $"{attachmentId}.json");
        if (!fs.FileExists(recordPath)) {
            return DshRpcResults.Fail("session/attachment-not-found",
                $"附件 {attachmentId} 不存在；请重新上传后用新 receiptId 引用");
        }
        var record = RelaxedJsonSerializer.Deserialize(
            await fs.ReadAllTextAsync(recordPath, cancellationToken), DshJsonContext.Default.DshAttachmentRecord);
        if (record is null) {
            return DshRpcResults.Fail("session/attachment-not-found", $"附件 {attachmentId} 元数据损坏；请重新上传");
        }
        var bytes = await fs.ReadAllBytesAsync(Path.Combine(_directory, owner, $"{attachmentId}.bin"), cancellationToken);
        return DshRpcResults.Ok(JsonSerializer.SerializeToElement(
            new DshSessionAttachmentResult {
                Attachment = new DshAttachmentInfo {
                    AttachmentId = record.AttachmentId,
                    MediaType = record.MediaType,
                    Bytes = record.Bytes,
                    Width = record.Width,
                    Height = record.Height,
                    Name = record.Name,
                },
                Data = Convert.ToBase64String(bytes),
            },
            DshJsonContext.Default.DshSessionAttachmentResult));
    }

    /// <summary>魔数探测（PNG/JPEG/GIF/WEBP；null=非四类图片）</summary>
    internal static string? SniffMediaType(ReadOnlySpan<byte> bytes) {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G') {
            return "image/png";
        }
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) {
            return "image/jpeg";
        }
        if (bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F') {
            return "image/gif";
        }
        if (bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P') {
            return "image/webp";
        }
        return null;
    }

    /// <summary>读图片宽（PNG IHDER / GIF 头 / WEBP VP8X|VP8|VP8L；JPEG 扫 SOF 段）</summary>
    internal static int? ReadWidth(ReadOnlySpan<byte> bytes) => ReadDimensions(bytes)?.Width;

    /// <summary>读图片高</summary>
    internal static int? ReadHeight(ReadOnlySpan<byte> bytes) => ReadDimensions(bytes)?.Height;

    /// <summary>四格式尺寸头解析（手写字节读取，AOT 安全；解析失败返回 null）</summary>
    private static (int Width, int Height)? ReadDimensions(ReadOnlySpan<byte> b) {
        if (b.Length >= 24 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G') {
            // PNG IHDER：宽高在 16..23（大端）
            return (BinaryPrimitives.ReadInt32BigEndian(b[16..20]), BinaryPrimitives.ReadInt32BigEndian(b[20..24]));
        }
        if (b.Length >= 10 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F') {
            // GIF 头：宽高在 6..9（小端 ushort）
            return (BinaryPrimitives.ReadUInt16LittleEndian(b[6..8]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..10]));
        }
        if (b.Length >= 30 && b[0] == 0xFF && b[1] == 0xD8) {
            return ReadJpegDimensions(b);
        }
        if (b.Length >= 30 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F') {
            return ReadWebpDimensions(b);
        }
        return null;
    }

    /// <summary>JPEG 尺寸：扫 SOF0-SOF15 段（跳过 FFE0-FFEF 应用段）</summary>
    private static (int, int)? ReadJpegDimensions(ReadOnlySpan<byte> b) {
        var offset = 2;
        while (offset + 9 < b.Length) {
            if (b[offset] != 0xFF) {
                return null;
            }
            var marker = b[offset + 1];
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) {
                offset += 2;
                continue;
            }
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 2)..]);
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC) {
                if (offset + 9 >= b.Length) {
                    return null;
                }
                // SOF：高在 +5..+7，宽在 +7..+9（大端）
                return (BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 7)..(offset + 9)]),
                    BinaryPrimitives.ReadUInt16BigEndian(b[(offset + 5)..(offset + 7)]));
            }
            offset += 2 + segmentLength;
        }
        return null;
    }

    /// <summary>WEBP 尺寸：VP8X（广域）/VP8（有损 keyframe）/VP8L（无损）三分支</summary>
    private static (int, int)? ReadWebpDimensions(ReadOnlySpan<byte> b) {
        var chunk = b[12..];
        var tag = chunk.Length >= 4 ? System.Text.Encoding.ASCII.GetString(chunk[..4]) : "";
        switch (tag) {
            case "VP8X": {
                if (chunk.Length < 14) {
                    return null;
                }
                // VP8X：canvas 宽=1+24bit（b[4..7] 低 24 位），高=1+24bit（b[7..10]）
                var width = 1 + (chunk[4] | chunk[5] << 8 | chunk[6] << 16);
                var height = 1 + (chunk[7] | chunk[8] << 8 | chunk[9] << 16);
                return (width, height);
            }
            case "VP8 ": {
                if (chunk.Length < 14) {
                    return null;
                }
                // VP8 keyframe：宽在 chunk[6..8]、高在 chunk[8..10]（小端 ushort）
                return (BinaryPrimitives.ReadUInt16LittleEndian(chunk[6..8]),
                    BinaryPrimitives.ReadUInt16LittleEndian(chunk[8..10]));
            }
            case "VP8L": {
                if (chunk.Length < 9) {
                    return null;
                }
                // VP8L：bits[0..13]=宽-1，bits[14..27]=高-1（28 位打包小端位序）
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..8]);
                return ((int)(bits & 0x3FFF) + 1, (int)(bits >> 14 & 0x3FFF) + 1);
            }
            default:
                return null;
        }
    }

    /// <summary>receipt/attachment ID 合法性（hex 32 位 GUID — 防路径注入）</summary>
    private static bool IsValidReceiptId(string id) =>
        id.Length == 32 && id.All(char.IsAsciiHexDigit);

    /// <summary>owner（会话/代理 ID）合法性：非空且不含路径分隔符与 .. 段（防目录逃逸；形状由前端会话体系决定，不限定 hex）</summary>
    private static bool IsValidOwner(string owner) =>
        !string.IsNullOrWhiteSpace(owner)
        && owner.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) < 0
        && !owner.Contains("..");
}
