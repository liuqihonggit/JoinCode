namespace NativeTui.Core.Repl;

/// <summary>
/// 代理对累加器 — Win32 KEY_EVENT 的 UnicodeChar 只有**单个 UTF-16 码元**，emoji 等非 BMP 字符
/// 会以「高代理 + 低代理」两条记录先后到达（真机取证：粘贴 👍 后输入框与 transcript 落盘为 `��`，
/// 即两个孤立代理分别被当字符插入，渲染/序列化时才显示成 U+FFFD）。
/// 逐码元喂入：凑成完整代理对时返回 2 char 文本；孤立代理丢弃，绝不把半个字符送进输入缓冲。
/// </summary>
public sealed class SurrogatePairAccumulator
{
    private char? _pendingHigh;

    /// <summary>喂入一个 UTF-16 码元 — 返回完整代理对文本（2 char）；半字符/普通码元返回 null。</summary>
    public string? Feed(char codeUnit)
    {
        if (char.IsHighSurrogate(codeUnit))
        {
            _pendingHigh = codeUnit;
            return null;
        }

        if (char.IsLowSurrogate(codeUnit))
        {
            if (_pendingHigh is not { } high)
            {
                return null;
            }

            _pendingHigh = null;
            return new string([high, codeUnit]);
        }

        _pendingHigh = null;
        return null;
    }

    /// <summary>丢弃未配对的半字符（收到普通按键时调用 — 防止高代理与很远的低代理错配）。</summary>
    public void Reset() => _pendingHigh = null;
}
