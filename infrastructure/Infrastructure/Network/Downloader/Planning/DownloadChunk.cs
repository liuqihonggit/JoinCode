namespace Infrastructure.Network.Downloader.Planning;

/// <summary>
/// 下载分片模型 — 描述一个 Range 分片的区间和下载进度
/// <para>闭区间 [Start, End],字节长度 = End - Start + 1</para>
/// <para>Downloaded 字段在断点续传时记录已下载字节数,Resume 从 Start+Downloaded 继续</para>
/// </summary>
public sealed class DownloadChunk
{
    /// <summary>分片序号(从 0 开始,合并时按此顺序拼接)</summary>
    public int Index { get; set; }

    /// <summary>分片起始字节偏移(含)</summary>
    public long Start { get; set; }

    /// <summary>分片结束字节偏移(含)</summary>
    public long End { get; set; }

    /// <summary>已下载字节数(0 ~ End-Start+1,断点续传用)</summary>
    public long Downloaded { get; set; }

    /// <summary>是否已完成(Downloaded == End-Start+1)</summary>
    public bool Completed { get; set; }

    /// <summary>分片总字节长度</summary>
    public long Length => End - Start + 1;

    /// <summary>剩余未下载字节数</summary>
    public long Remaining => Length - Downloaded;
}
