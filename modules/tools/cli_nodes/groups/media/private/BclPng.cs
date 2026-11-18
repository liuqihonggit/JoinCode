// 纯 BCL PNG 编解码 — 替代 SkiaSharp 的 SKBitmap.Decode/Encode。
// PNG = 签名 + IHDR + IDAT(zlib 压缩的扫描行, 每行首字节 filter) + IEND；CRC32 校验。
// zlib 用 System.IO.Compression.ZLibStream（BCL），不引任何第三方库。



namespace JoinCode.CliNode.Media;

/// <summary>RGBA 像素图 — 宽/高/row-major 像素（每像素 4 字节 R,G,B,A）。</summary>
internal sealed class RgbaImage {
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Pixels { get; init; } = [];
    public byte[] Row(int y) => Pixels.AsSpan(y * Width * 4, Width * 4).ToArray();
}

internal static class BclPng {
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static RgbaImage Decode(string base64) => Decode(Convert.FromBase64String(base64));

    public static RgbaImage Decode(byte[] bytes) {
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(Signature))
            throw new ArgumentException("无效 PNG：签名不匹配");

        var pos = 8;
        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        var idat = new List<byte>();
        while (pos + 12 <= bytes.Length) {
            var len = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos, 4));
            var type = bytes.AsSpan(pos + 4, 4);
            var data = bytes.AsSpan(pos + 8, len);
            if (type.SequenceEqual("IHDR"u8)) {
                width = BinaryPrimitives.ReadInt32BigEndian(data[0..4]);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..8]);
                bitDepth = data[8]; colorType = data[9]; interlace = data[12];
            } else if (type.SequenceEqual("IDAT"u8)) {
                idat.AddRange(data.ToArray());
            } else if (type.SequenceEqual("IEND"u8)) break;
            pos += 12 + len;
        }
        if (width <= 0 || height <= 0) throw new ArgumentException("无效 PNG：IHDR 宽高非法");
        if (bitDepth != 8 || (colorType != 2 && colorType != 6) || interlace != 0)
            throw new ArgumentException($"不支持的 PNG 格式：bitDepth={bitDepth} colorType={colorType} interlace={interlace}（需 8-bit RGB/RGBA 非隔行）");

        var channels = colorType == 6 ? 4 : 3;
        var stride = width * channels;
        byte[] raw;
        using (var ms = new MemoryStream(idat.ToArray()))
        using (var z = new ZLibStream(ms, CompressionMode.Decompress)) {
            raw = new byte[stride * height + height]; // 每行 1 字节 filter
            var read = 0;
            while (read < raw.Length) {
                var n = z.Read(raw, read, raw.Length - read);
                if (n == 0) break;
                read += n;
            }
        }

        var pixels = new byte[width * height * 4];
        var prev = new byte[stride];
        var rp = 0;
        for (var y = 0; y < height; y++) {
            var filter = raw[rp++];
            var line = new byte[stride];
            Array.Copy(raw, rp, line, 0, stride);
            rp += stride;
            Unfilter(filter, line, prev, channels);
            for (var x = 0; x < width; x++) {
                var s = x * channels;
                var d = (y * width + x) * 4;
                pixels[d] = line[s];
                pixels[d + 1] = line[s + 1];
                pixels[d + 2] = line[s + 2];
                pixels[d + 3] = channels == 4 ? line[s + 3] : (byte)255;
            }
            prev = line;
        }
        return new RgbaImage { Width = width, Height = height, Pixels = pixels };
    }

    private static void Unfilter(int filter, byte[] line, byte[] prev, int bpp) {
        for (var i = 0; i < line.Length; i++) {
            var a = i >= bpp ? line[i - bpp] : 0;
            var b = prev[i];
            var c = i >= bpp ? prev[i - bpp] : 0;
            line[i] = filter switch {
                0 => line[i],
                1 => (byte)(line[i] + a),
                2 => (byte)(line[i] + b),
                3 => (byte)(line[i] + (a + b) / 2),
                4 => (byte)(line[i] + Paeth(a, b, c)),
                _ => line[i]
            };
        }
    }

    private static byte Paeth(int a, int b, int c) {
        var p = a + b - c;
        var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c);
        return (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
    }

    /// <summary>把 RGBA 像素编码成 base64 PNG（filter=0，zlib 压缩）。</summary>
    public static string EncodeBase64(RgbaImage img) => Convert.ToBase64String(Encode(img));

    public static byte[] Encode(RgbaImage img) {
        var stride = img.Width * 4;
        var raw = new byte[(stride + 1) * img.Height];
        var rp = 0;
        for (var y = 0; y < img.Height; y++) {
            raw[rp++] = 0; // filter none
            Array.Copy(img.Pixels, y * stride, raw, rp, stride);
            rp += stride;
        }
        byte[] compressed;
        using (var ms = new MemoryStream())
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true)) {
            z.Write(raw);
            z.Flush();
            compressed = ms.ToArray();
        }
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0, 4), img.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4, 4), img.Height);
        ihdr[8] = 8; ihdr[9] = 6; // bitDepth=8, colorType=RGBA
        using var result = new MemoryStream();
        result.Write(Signature);
        WriteChunk(result, "IHDR"u8, ihdr);
        WriteChunk(result, "IDAT"u8, compressed);
        WriteChunk(result, "IEND"u8, []);
        return result.ToArray();
    }

    private static void WriteChunk(Stream s, ReadOnlySpan<byte> type, byte[] data) {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len); s.Write(type); s.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(type, data));
        s.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> a, byte[] b) {
        uint crc = 0xFFFFFFFF;
        foreach (var v in a) crc = Step(crc, v);
        foreach (var v in b) crc = Step(crc, v);
        return crc ^ 0xFFFFFFFF;
    }
    private static uint Step(uint crc, byte v) {
        crc ^= v;
        for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        return crc;
    }
}
