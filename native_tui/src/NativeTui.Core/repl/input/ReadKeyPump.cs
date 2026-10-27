namespace NativeTui.Core.Repl;

/// <summary>
/// ReadKey 输入泵（DSG042 P1 — 现役逻辑收编，语义零变化）— Console.ReadKey 归一路径：
/// Key 枚举归一 KeyPress（<see cref="ConsoleKeyAdapter"/>）+ bracketed paste 重组状态机
///（ReadKey 把 ESC[200~/201~ 拆散为逐 KeyInfo — ESC 后 KeyAvailable 连续判定重组整块；
/// 孤立 Esc 即时分派不受影响 — paste 序列一次性到达键流恒连续）+ 误撞组合塞回待分派缓冲防丢键。
/// 限制（原始流通道的迁移动机）：VT 序列经 Key 枚举归一不可逆（修饰组合丢失）；
/// P3 灰度对齐后本通道退役（DSG042），RawStdinPump 接任。
/// </summary>
public sealed class ReadKeyPump : IInputPump
{
    /// <summary>已读待分派缓冲（误判回退塞回 — 键不丢）。</summary>
    private readonly Queue<ConsoleKeyInfo> _pendingKeys = new();

    /// <summary>排空键盘事件（非阻塞）— ReadKey 归一 KeyPress 后统一分派；
    /// bracketed paste 重组：ESC 键已消费后尝试收集整块（失败回退塞回正常分派）。</summary>
    public bool Drain(Action<KeyPress> dispatch)
    {
        var dispatched = false;
        var forceTty = Environment.GetEnvironmentVariable("JCC_FORCE_TTY") == "1";
        while (_pendingKeys.Count > 0 || (forceTty ? Console.In.Peek() != -1 : Console.KeyAvailable))
        {
            ConsoleKeyInfo info;
            if (_pendingKeys.Count > 0) {
                info = _pendingKeys.Dequeue();
            } else if (forceTty) {
                var ch = (char)Console.In.Read();
                info = ch switch {
                    '\r' or '\n' => new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false),
                    '\u001b' => new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false),
                    '\u0010' => new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false),
                    '\u000e' => new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false),
                    _ => new ConsoleKeyInfo(ch, ConsoleKey.None, false, false, false),
                };
            } else {
                info = Console.ReadKey(intercept: true);
            }
            if (!forceTty && info.Key == ConsoleKey.Escape && info.KeyChar == '\u001b' && TryCollectPaste(out var content))
            {
                dispatch(ConsoleKeyAdapter.Paste(content));
                dispatched = true;
                continue;
            }

            if (ConsoleKeyAdapter.ToKeyPress(info) is { } key)
            {
                dispatch(key);
                dispatched = true;
            }
        }

        return dispatched;
    }

    /// <summary>尝试收集 bracketed paste 块 — 前提：ESC 键已消费。
    /// ESC 后无后续键 = 孤立 Esc（false，调用方正常分派）；后续键非 '[' 或非 "200~" 起始标记 =
    /// 误撞组合（false，已读键塞回 pending 两键都走正常分派）；起始标记完整后收集内容直到
    /// ESC[201~ 终止标记（true，out 携带粘贴原文）；收集期流中断整体放弃吞掉（防泄漏为文本）。</summary>
    private bool TryCollectPaste(out string content)
    {
        content = string.Empty;
        if (!Console.KeyAvailable)
        {
            return false;
        }

        var bracket = Console.ReadKey(intercept: true);
        if (bracket.KeyChar != '[')
        {
            _pendingKeys.Enqueue(bracket);
            return false;
        }

        var consumed = new List<ConsoleKeyInfo> { bracket };
        if (!TryMatchPasteStart(consumed))
        {
            Requeue(consumed);
            return false;
        }

        // 内容收集（2.7 — 空粘贴也恒产出空 Paste 事件，对齐基准
        /// "Always emit a paste key, even for empty pastes"；流中断/终止标记丢失已有内容仍产出 — 批5 语义）
        content = CollectPasteBody();
        return true;
    }

    /// <summary>匹配 paste 起始标记剩余字符 "200~"（逐字符读，不匹配/流中断返回 false — 已读键入 consumed）。</summary>
    private static bool TryMatchPasteStart(List<ConsoleKeyInfo> consumed)
    {
        foreach (var expected in "200~")
        {
            if (!Console.KeyAvailable)
            {
                return false;
            }

            var info = Console.ReadKey(intercept: true);
            consumed.Add(info);
            if (info.KeyChar != expected)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>收集 paste 内容直到终止标记 ESC[201~ — 内容字符直积（\r\n 原样保留由
    /// PastedContentTracker 清洗归一）；终止标记匹配失败/流中断（慢链路跨包间隙）时
    /// 已有内容仍产出（基准 PASTE_TIMEOUT 语义 — 终止标记丢失产出部分内容释放通道，
    /// 已读的 201~ 部分字符吞掉防泄漏为文本）；空粘贴返回空。</summary>
    private static string CollectPasteBody()
    {
        var buffer = new StringBuilder();
        while (Console.KeyAvailable)
        {
            var info = Console.ReadKey(intercept: true);
            if (info.KeyChar == '\u001b')
            {
                // 终止标记匹配 — 失败时已读部分吞掉（防泄漏），已有 body 产出
                TryMatchPasteEnd();
                return buffer.ToString();
            }

            buffer.Append(info.KeyChar);
        }

        return buffer.ToString();
    }

    /// <summary>匹配 paste 终止标记剩余 "[201~"（ESC 已消费）。</summary>
    private static bool TryMatchPasteEnd()
    {
        foreach (var expected in "[201~")
        {
            if (!Console.KeyAvailable)
            {
                return false;
            }

            if (Console.ReadKey(intercept: true).KeyChar != expected)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>已读键按序塞回待分派缓冲（误判回退 — 键不丢）。</summary>
    private void Requeue(List<ConsoleKeyInfo> consumed)
    {
        foreach (var info in consumed)
        {
            _pendingKeys.Enqueue(info);
        }
    }
}
