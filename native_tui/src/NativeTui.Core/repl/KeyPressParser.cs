namespace NativeTui.Core.Repl;

/// <summary>
/// 按键序列解码器 — 对齐基准 ink/parse-keypress + useTextInput 的序列语义。
/// 契约：TryParseNext 把 buffer 视为当前全部可用输入 —
/// chunk 末尾的孤立 ESC 解析为 Escape（基准用 ESC_TIMEOUT flush，此处以 chunk 边界近似）；
/// 不完整前缀（如 "\x1b["）返回 false 且不消耗，由调用方攒够后重试。
/// 无法识别的序列整体吞掉为 Unknown（Input 空），防止转义序列泄漏成文本（对齐基准 suppress 语义）。
/// bracketed paste：\x1b[200~ 内容 \x1b[201~ 整块产出 Paste 事件（Input=粘贴原文），
/// 终止标记未到达视为不完整前缀不消耗（粘贴块可跨 chunk 攒齐）。
/// kitty keyboard protocol（CSI u：ESC[codepoint[;mod[;text]]u）与 xterm modifyOtherKeys（ESC[27;mod;code~）：
/// 码点+mod-1 位掩码形式解析（对齐基准 ink/parse-keypress.ts 的 CSI_U_RE / MODIFY_OTHER_KEYS_RE 分支），
/// 双修饰组合（ctrl+shift 等）完整保留 — 传统 \x01 控制码路径丢失 shift 的补全。
/// SGR 鼠标（DECSET 1006）：\x1b[&lt;b;x;y M/m 解码为 KeyKind.Mouse + 结构化 MouseEvent
/// （位序/滚轮掩码对齐基准 SGR_MOUSE_RE + parseMouseEvent + wheel 分支，详见 ParseSgrMouse；板块C基建C1）。
/// </summary>
public static class KeyPressParser
{
    /// <summary>bracketed paste 起始标记（DEC 2004 模式开启后终端包裹粘贴内容）。</summary>
    private const string PasteStart = "\u001b[200~";

    /// <summary>bracketed paste 终止标记。</summary>
    private const string PasteEnd = "\u001b[201~";

    /// <summary>从 buffer 头部解析一个按键；成功则消耗并推进 buffer，前缀不完整返回 false。</summary>
    public static bool TryParseNext(ref ReadOnlySpan<char> buffer, out KeyPress key) =>
        TryParseNext(ref buffer, out key, force: false);

    /// <summary>超时 flush 入口（基准 App.tsx:309-333 flushIncomplete — 50ms NORMAL_TIMEOUT /
    /// :121,356 PASTE_TIMEOUT 500ms）：序列后半段永不到达时强制产出 —
    /// 不完整 ESC 前缀产出 Escape（消耗 1 字节，剩余下轮按文本解析）；
    /// 不完整粘贴产出已有部分内容 Paste 事件（终止标记丢失释放通道 — 基准 emit pasteBuffer）。</summary>
    public static bool TryParseNext(ref ReadOnlySpan<char> buffer, out KeyPress key, bool force)
    {
        if (TryParseNormal(ref buffer, out key))
        {
            return true;
        }

        if (force && buffer.Length > 0 && buffer[0] == '\u001b')
        {
            if (buffer.StartsWith(PasteStart))
            {
                key = new KeyPress(KeyKind.Paste, buffer[PasteStart.Length..].ToString());
                buffer = buffer[buffer.Length..];
            }
            else
            {
                key = KeyPress.Named(KeyKind.Escape);
                buffer = buffer[1..];
            }

            return true;
        }

        return false;
    }

    private static bool TryParseNormal(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        if (buffer.IsEmpty)
        {
            key = NamedUnknown();
            return false;
        }

        if (buffer.StartsWith(PasteStart))
        {
            return ParseBracketedPaste(ref buffer, out key);
        }

        var first = buffer[0];
        switch (first)
        {
            case '\r':
            case '\n':
                key = KeyPress.Named(KeyKind.Return);
                buffer = buffer[1..];
                return true;
            case '\t':
                key = KeyPress.Named(KeyKind.Tab);
                buffer = buffer[1..];
                return true;
            case '\u007f':
            case '\u0008':
                key = KeyPress.Named(KeyKind.Backspace);
                buffer = buffer[1..];
                return true;
            case '\u001f':
                // Ctrl+_（= ctrl+shift+- 同码）— 基准 chat:undo 键位，归一为 Ctrl+"_" 名
                key = new KeyPress(KeyKind.Char, "_", Ctrl: true);
                buffer = buffer[1..];
                return true;
            case '\u001b':
                return ParseEscape(ref buffer, out key);
        }

        if (first < ' ')
        {
            // \x01-\x1a 中的控制字母 → Ctrl+字母（\b \t \n \r 已在上方捕获）
            if (first is >= '\u0001' and <= '\u001a')
            {
                var letter = (char)('a' + first - 1);
                key = new KeyPress(KeyKind.Char, letter.ToString(), Ctrl: true);
            }
            else
            {
                key = NamedUnknown();
            }

            buffer = buffer[1..];
            return true;
        }

        key = KeyPress.Char(first.ToString());
        buffer = buffer[1..];
        return true;
    }

    /// <summary>bracketed paste 块：\x1b[200~ 内容 \x1b[201~ — 整块消费产出 Paste 事件；终止标记缺失返回 false 不消耗（跨 chunk 攒齐）。</summary>
    private static bool ParseBracketedPaste(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        var content = buffer[PasteStart.Length..];
        var endOffset = content.IndexOf(PasteEnd);
        if (endOffset < 0)
        {
            key = NamedUnknown();
            return false;
        }

        key = new KeyPress(KeyKind.Paste, content[..endOffset].ToString());
        buffer = content[(endOffset + PasteEnd.Length)..];
        return true;
    }

    /// <summary>ESC 前缀：CSI（[）/ SS3（O）/ Meta 后缀 / 孤立 Esc。</summary>
    private static bool ParseEscape(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        if (buffer.Length == 1)
        {
            key = KeyPress.Named(KeyKind.Escape);
            buffer = buffer[1..];
            return true;
        }

        var second = buffer[1];
        return second switch
        {
            '[' => ParseCsi(ref buffer, out key),
            'O' => ParseSs3(ref buffer, out key),
            '\u001b' => Consume(ref buffer, 2, KeyPress.Named(KeyKind.Escape, meta: true), out key),
            '\r' or '\n' => Consume(ref buffer, 2, KeyPress.Named(KeyKind.Return, meta: true), out key),
            '\t' => Consume(ref buffer, 2, KeyPress.Named(KeyKind.Tab, meta: true), out key),
            '\u007f' => Consume(ref buffer, 2, KeyPress.Named(KeyKind.Backspace, meta: true), out key),
            >= ' ' => Consume(ref buffer, 2, KeyPress.Char(second.ToString()) with { Meta = true }, out key),
            _ => Consume(ref buffer, 2, NamedUnknown(), out key),
        };
    }

    /// <summary>CSI 序列：ESC [ params {;mod} final — 方向键/Home/End/Delete/Page + 修饰符 + SGR 鼠标解码
    /// + kitty CSI u / xterm modifyOtherKeys 分派（B1 融合 — final 'u' 进 kitty 完整版、三段 ~ 进 modifyOtherKeys）。
    /// 序列边界 = 参数区（0x30-0x3F 数字/;/…）+ intermediate（0x20-0x2F）扫描至 final（0x40-0x7E）；
    /// 非「数字{;数字}」两段形态（私有参数 ESC[?…、intermediate ESC[…$…、超长数字）
    /// 整体吞 Unknown（防泄漏契约）；数字解析全部 TryParse — 空第一段按 xterm 默认 1、
    /// 超长数字 -1 无匹配（对齐基准 parseInt 永不抛，外部畸形输入不可击穿读线程）。</summary>（板块B基建） | 决策: 主代理debug修复AOT反序列化Type重载/F键全链打通/全被拒条目保留默认非解绑,子代理只写码主代理统一编译修复)
    private static bool ParseCsi(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        var rest = buffer[2..];
        if (rest.IsEmpty)
        {
            key = NamedUnknown();
            return false;
        }

        // SGR 鼠标（ESC[<b;x;y M/m）解码为结构化鼠标事件（板块C基建C1）
        if (rest[0] == '<')
        {
            return ParseSgrMouse(ref buffer, out key);
        }

        // linux console 功能键变体（ESC[[A-E = F1-F5 — 基准 parse-keypress.ts:310-312）
        if (rest[0] == '[' && rest.Length >= 2 && rest[1] is >= 'A' and <= 'E')
        {
            key = KeyPress.Named(rest[1] switch
            {
                'A' => KeyKind.F1,
                'B' => KeyKind.F2,
                'C' => KeyKind.F3,
                'D' => KeyKind.F4,
                _ => KeyKind.F5,
            });
            buffer = buffer[4..];
            return true;
        }

        var scan = rest.IndexOfAnyExceptInRange('\u0020', '\u003f');
        if (scan < 0)
        {
            scan = rest.Length;
        }

        if (scan == rest.Length)
        {
            // 参数区未闭合（final 未到）— 不完整前缀，等待更多输入
            key = NamedUnknown();
            return false;
        }

        var final = rest[scan];

        // B1 融合 — kitty CSI u（终点 'u' + 首段正数码点；含 97u 单段 / 97;5u 双段 / 97;5;120u 三段形态）：
        // 完整版含修饰位解码与防御（代理区/越界吞 Unknown），取代 main 侧 BuildSimple 的 'u' 简版分支
        if (final == 'u' && HasLeadingNumber(rest, scan))
        {
            return ParseKittyCsiU(ref buffer, rest[..scan], out key);
        }

        // B1 融合 — xterm modifyOtherKeys（27;mod;code~ 三段形态，参数顺序与 CSI u 相反）
        if (final == '~' && IsModifyOtherKeys(rest[..scan]))
        {
            return ParseModifyOtherKeys(ref buffer, rest[..scan], out key);
        }

        key = ParseTwoSegmentForm(rest, scan, final) ?? NamedUnknown();
        buffer = buffer[(2 + scan + 1)..];
        return true;
    }

    /// <summary>首段为正数数字判定（kitty CSI u 的形态门槛 — '?' 私有参数/空段形态不误入）。</summary>
    private static bool HasLeadingNumber(ReadOnlySpan<char> rest, int scan)
    {
        var numEnd = 0;
        while (numEnd < scan && char.IsDigit(rest[numEnd]))
        {
            numEnd++;
        }

        return numEnd > 0;
    }

    /// <summary>「数字{;数字}」两段形态语义解析：单段=简单键、双段=修饰键；空第一段=xterm 默认 1；畸形/超长=null（整体吞 Unknown）。</summary>
    private static KeyPress? ParseTwoSegmentForm(ReadOnlySpan<char> rest, int scan, char final)
    {
        var numEnd = 0;
        while (numEnd < scan && char.IsDigit(rest[numEnd]))
        {
            numEnd++;
        }

        if (numEnd == scan)
        {
            var number = numEnd == 0 ? 1 : int.TryParse(rest[..numEnd], out var plain) ? plain : -1;
            return BuildSimple(final, number);
        }

        if (rest[numEnd] != ';')
        {
            return null;
        }

        var modStart = numEnd + 1;
        var modEnd = modStart;
        while (modEnd < scan && char.IsDigit(rest[modEnd]))
        {
            modEnd++;
        }

        if (modEnd != scan || modEnd == modStart)
        {
            return null;
        }

        var number2 = int.TryParse(rest[..numEnd], out var head) ? head : -1;
        if (!int.TryParse(rest[modStart..modEnd], out var mod))
        {
            return null;
        }

        var simple = BuildSimple(final, number2);
        if (simple is null)
        {
            return null;
        }

        var bits = Math.Max(0, mod - 1);
        return simple with
        {
            Shift = simple.Shift || (bits & 1) != 0,
            Meta = simple.Meta || (bits & 2) != 0,
            Ctrl = simple.Ctrl || (bits & 4) != 0,
        };
    }

    /// <summary>
    /// kitty keyboard protocol CSI u：ESC [ codepoint [; modifiers [; text-codepoints]] u —
    /// 对齐基准 ink/parse-keypress.ts 的 CSI_U_RE + decodeModifier + keycodeToName 分支。
    /// codepoint 为 Unicode 码点十进制（97='a'，13=Enter，27=Esc，127=Backspace，9=Tab）；
    /// modifiers 位掩码值 = 1 + 位（bit0=Shift bit1=Alt bit2=Ctrl bit3=Super…，与 ParseTwoSegmentForm 的 mod-1 语义一致），缺省 1=无修饰；
    /// 第三段 text-codepoints 仅是终端回显文本（如 shift 产生的字形），忽略不消费语义。
    /// params 已剔除 ESC [ 前缀与终点 'u'；非法段（空/非正数）整体吞为 Unknown 防泄漏。
    /// </summary>
    private static bool ParseKittyCsiU(ref ReadOnlySpan<char> buffer, ReadOnlySpan<char> @params, out KeyPress key)
    {
        var firstSemi = @params.IndexOf(';');
        var codepoint = ParseSegment(firstSemi < 0 ? @params : @params[..firstSemi]);
        var mod = 1;
        if (firstSemi >= 0)
        {
            var second = @params[(firstSemi + 1)..];
            var secondSemi = second.IndexOf(';');
            // 第二分号后的第三段（text-codepoints）忽略 — 基准 CSI_U_RE 同样不消费该段
            mod = ParseSegment(secondSemi < 0 ? second : second[..secondSemi]);
        }

        key = codepoint > 0 && mod > 0
            ? BuildCodepointKey(codepoint, mod - 1)
            : NamedUnknown();
        buffer = buffer[(@params.Length + 3)..];
        return true;
    }

    /// <summary>
    /// xterm modifyOtherKeys：ESC [ 27 ; modifiers ; keycode ~ —
    /// 对齐基准 ink/parse-keypress.ts 的 MODIFY_OTHER_KEYS_RE 分支：
    /// Ghostty/tmux/xterm 在 modifyOtherKeys=2 激活或 SSH 下 TERM 嗅探失效时的修饰键通道，
    /// 参数顺序与 CSI u 相反（修饰在前、键码在后）。keycode 码点语义与 kitty 共用 BuildCodepointKey。
    /// params 已剔除 ESC [ 前缀与终点 '~'（形如 "27;5;97"）。
    /// </summary>
    private static bool ParseModifyOtherKeys(ref ReadOnlySpan<char> buffer, ReadOnlySpan<char> @params, out KeyPress key)
    {
        var firstSemi = @params.IndexOf(';');
        var tail = @params[(firstSemi + 1)..];
        var secondSemi = tail.IndexOf(';');
        var mod = ParseSegment(tail[..secondSemi]);
        var codepoint = ParseSegment(tail[(secondSemi + 1)..]);

        key = mod > 0 && codepoint > 0
            ? BuildCodepointKey(codepoint, mod - 1)
            : NamedUnknown();
        buffer = buffer[(@params.Length + 3)..];
        return true;
    }

    /// <summary>modifyOtherKeys 形态判定（只读前瞻）：参数区恰为 27;数字;数字 三段（终点 '~' 由调用方保证）；两段（27;5~ 传统形态）不算。</summary>
    private static bool IsModifyOtherKeys(ReadOnlySpan<char> @params)
    {
        if (!@params.StartsWith("27;"))
        {
            return false;
        }

        var tail = @params[3..];
        var semi = tail.IndexOf(';');
        return semi >= 0
            && IsDigits(tail[..semi])
            && IsDigits(tail[(semi + 1)..]);
    }

    /// <summary>非空纯数字段判定。</summary>
    private static bool IsDigits(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty)
        {
            return false;
        }

        foreach (var c in segment)
        {
            if (!char.IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 码点 + 修饰位掩码 → KeyPress（kitty CSI u 与 modifyOtherKeys 共用，对齐基准 keycodeToName + decodeModifier）。
    /// bits = mod-1：bit0=Shift bit1=Alt/Meta bit2=Ctrl；bit3 起的 Super/ShiftLock 等 KeyPress 未建模，丢弃。
    /// 可打印码点走 Char（大写自动 Shift 复用 KeyPress.Char 构造，mod 的 shift 位再 OR 上）。
    /// </summary>
    private static KeyPress BuildCodepointKey(int codepoint, int bits)
    {
        var ctrl = (bits & 4) != 0;
        var meta = (bits & 2) != 0;
        var shift = (bits & 1) != 0;
        switch (codepoint)
        {
            case 13:
                return KeyPress.Named(KeyKind.Return, ctrl, meta, shift);
            case 27:
                return KeyPress.Named(KeyKind.Escape, ctrl, meta, shift);
            case 127:
                return KeyPress.Named(KeyKind.Backspace, ctrl, meta, shift);
            case 9:
                return KeyPress.Named(KeyKind.Tab, ctrl, meta, shift);
            // kitty 功能键区码点 57414-57417（F1-F4）— KeyKind.F1-F12 引入（B2 键位系统）
            case 57414:
                return KeyPress.Named(KeyKind.F1, ctrl, meta, shift);
            case 57415:
                return KeyPress.Named(KeyKind.F2, ctrl, meta, shift);
            case 57416:
                return KeyPress.Named(KeyKind.F3, ctrl, meta, shift);
            case 57417:
                return KeyPress.Named(KeyKind.F4, ctrl, meta, shift);
            default:
                // 控制码点（9/13/27/127 已在上方捕获）与越界/代理区码点不可打印 — Unknown 吞掉
                if (codepoint is < 32 or > 0x10FFFF or (>= 0xD800 and <= 0xDFFF))
                {
                    return NamedUnknown();
                }

                var text = char.ConvertFromUtf32(codepoint);
                var key = KeyPress.Char(text);
                return key with { Ctrl = ctrl, Meta = meta, Shift = key.Shift || shift };
        }
    }

    /// <summary>十进制数字段解析；空段/非数字/溢出返回 -1（调用方按非法吞为 Unknown）。</summary>
    private static int ParseSegment(ReadOnlySpan<char> segment) =>
        int.TryParse(segment, out var value) ? value : -1;

    /// <summary>SS3 应用模式（ESC O final）：方向键/Home/End/Clear/F1-F4，其余单字符吞掉。</summary>
    private static bool ParseSs3(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        if (buffer.Length == 2)
        {
            key = NamedUnknown();
            return false;
        }

        var final = buffer[2];
        key = final switch
        {
            'A' => KeyPress.Named(KeyKind.UpArrow),
            'B' => KeyPress.Named(KeyKind.DownArrow),
            'C' => KeyPress.Named(KeyKind.RightArrow),
            'D' => KeyPress.Named(KeyKind.LeftArrow),
            'E' => KeyPress.Named(KeyKind.Clear),
            'H' => KeyPress.Named(KeyKind.Home),
            'F' => KeyPress.Named(KeyKind.End),
            'P' => KeyPress.Named(KeyKind.F1),
            'Q' => KeyPress.Named(KeyKind.F2),
            'R' => KeyPress.Named(KeyKind.F3),
            'S' => KeyPress.Named(KeyKind.F4),
            _ => NamedUnknown(),
        };
        buffer = buffer[3..];
        return true;
    }

    /// <summary>
    /// SGR 鼠标解码（DECSET 1006）：ESC[&lt;b;x;y M/m — 终点 M=按下 m=释放。
    /// b 位掩码（基准 parse-keypress.ts:573-575 ParsedMouse.button 注释 + :678-684 滚轮分支）：
    /// bit0-1 按钮（0=左 1=中 2=右 3=无按钮）、bit2(0x04)=Shift、bit3(0x08)=Meta、bit4(0x10)=Ctrl、
    /// bit5(0x20)=移动/拖拽、bit6(0x40)=滚轮（低 2 位定方向 — 0x43 掩码判定忽略修饰位：
    /// 64=上 65=下基准 :683-684，66/67 横滚为 SGR 规范扩展显式建模）。
    /// 坐标 1-based → 0-based（基准 events/click-event.ts:11-12 0-indexed 消费语义）。
    /// 不完整前缀（final 未到）false 不消耗（跨 chunk 攒齐，现有契约）；
    /// 非法形态（分号数≠2/空段/非数字/溢出/坐标&lt;1）整体含 final 吞 Unknown 防泄漏。
    /// </summary>
    private static bool ParseSgrMouse(ref ReadOnlySpan<char> buffer, out KeyPress key)
    {
        var rest = buffer[3..];
        var i = 0;
        var semicolons = 0;
        while (i < rest.Length && (char.IsDigit(rest[i]) || rest[i] == ';'))
        {
            if (rest[i] == ';')
            {
                semicolons++;
            }

            i++;
        }

        if (i == rest.Length)
        {
            // 参数区未闭合（final 未到）— 不完整前缀，等待更多输入
            key = NamedUnknown();
            return false;
        }

        var final = rest[i];
        // M/m 终点整体含 final 吞（非法形态也不留 final 泄漏成文本）；其余终点吞到扫描位（现状契约）
        var consumed = 3 + i + (final is 'M' or 'm' ? 1 : 0);
        key = final is 'M' or 'm' && semicolons == 2
            ? DecodeSgrParams(rest[..i], final)
            : NamedUnknown();
        buffer = buffer[consumed..];
        return true;
    }

    /// <summary>SGR 参数区（"b;x;y"）+ 终点 → 鼠标 KeyPress；段非法（空/非数字/溢出→-1）或坐标&lt;1（SGR 1-based 最小 1，0 为畸形）吞 Unknown — 外部畸形输入不可击穿读线程。</summary>
    private static KeyPress DecodeSgrParams(ReadOnlySpan<char> @params, char final)
    {
        var firstSemi = @params.IndexOf(';');
        var secondSemi = @params.LastIndexOf(';');
        var b = ParseSegment(@params[..firstSemi]);
        var x = ParseSegment(@params[(firstSemi + 1)..secondSemi]);
        var y = ParseSegment(@params[(secondSemi + 1)..]);

        if (b < 0 || x < 1 || y < 1)
        {
            return NamedUnknown();
        }

        var mouse = new MouseEvent(
            SgrMouseKind(b, final),
            SgrButton(b),
            x - 1,
            y - 1,
            (b & 0x04) != 0,
            (b & 0x08) != 0,
            (b & 0x10) != 0);
        return new KeyPress(KeyKind.Mouse, string.Empty, Mouse: mouse);
    }

    /// <summary>SGR b 码 → 事件种类：滚轮优先（0x43 掩码判方向忽略修饰位，基准 :683-684 — 修饰滚轮 button=80 仍识别）；
    /// 次终点 Release；motion 位 0x20 区分 Drag（按钮位 0-2）/Move（按钮位 3）；其余 Press。</summary>
    private static MouseKind SgrMouseKind(int b, char final) =>
        (b & 0x40) != 0
            ? (b & 0x43) switch
            {
                0x40 => MouseKind.WheelUp,
                0x41 => MouseKind.WheelDown,
                0x42 => MouseKind.WheelLeft,
                _ => MouseKind.WheelRight,
            }
            : final == 'm'
                ? MouseKind.Release
                : (b & 0x20) != 0
                    ? (b & 3) == 3 ? MouseKind.Move : MouseKind.Drag
                    : MouseKind.Press;

    /// <summary>SGR b 码 → 按钮（低 2 位映射；3=无按钮占位 Left — 滚轮/Move 事件无按钮语义）。</summary>
    private static MouseButton SgrButton(int b) =>
        (b & 3) switch
        {
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            _ => MouseButton.Left,
        };

    /// <summary>简单序列 → 按键（final + 可选数字）：字母终点 / ~ 终点数字表
    ///（F1-F12/Insert/Clear/rxvt Home/End — 对齐基准 parse-keypress.ts:304-406 keyName 表）/
    /// u 终点 kitty 键位码（CSI codepoint;mods u — Windows Terminal 等 kitty 协议终端的
    /// ctrl+shift+字母唯一可区分形态：标准 xterm 下该组合与 ctrl+字母同码不可区分）。</summary>
    private static KeyPress? BuildSimple(char final, int number) =>
        final switch
        {
            'A' => KeyPress.Named(KeyKind.UpArrow),
            'B' => KeyPress.Named(KeyKind.DownArrow),
            'C' => KeyPress.Named(KeyKind.RightArrow),
            'D' => KeyPress.Named(KeyKind.LeftArrow),
            'E' => KeyPress.Named(KeyKind.Clear),
            'H' => KeyPress.Named(KeyKind.Home),
            'F' => KeyPress.Named(KeyKind.End),
            'I' => KeyPress.Named(KeyKind.FocusIn),
            'O' => KeyPress.Named(KeyKind.FocusOut),
            'Z' => KeyPress.Named(KeyKind.BackTab, shift: true),
            '~' => number switch
            {
                1 => KeyPress.Named(KeyKind.Home),
                2 => KeyPress.Named(KeyKind.Insert),
                3 => KeyPress.Named(KeyKind.Delete),
                4 => KeyPress.Named(KeyKind.End),
                5 => KeyPress.Named(KeyKind.PageUp),
                6 => KeyPress.Named(KeyKind.PageDown),
                7 => KeyPress.Named(KeyKind.Home),
                8 => KeyPress.Named(KeyKind.End),
                11 => KeyPress.Named(KeyKind.F1),
                12 => KeyPress.Named(KeyKind.F2),
                13 => KeyPress.Named(KeyKind.F3),
                14 => KeyPress.Named(KeyKind.F4),
                15 => KeyPress.Named(KeyKind.F5),
                17 => KeyPress.Named(KeyKind.F6),
                18 => KeyPress.Named(KeyKind.F7),
                19 => KeyPress.Named(KeyKind.F8),
                20 => KeyPress.Named(KeyKind.F9),
                21 => KeyPress.Named(KeyKind.F10),
                23 => KeyPress.Named(KeyKind.F11),
                24 => KeyPress.Named(KeyKind.F12),
                _ => null,
            },
            // kitty 'u' 终点不在此处理 — ParseCsi 已拦截（HasLeadingNumber 门槛）全部进 ParseKittyCsiU
            // 完整版（含修饰位解码/防御），简版 'u' 分支（裸码点直接 ConvertFromUtf32）被取代
            _ => null,
        };

    /// <summary>消耗前 n 个字符并返回指定按键。</summary>
    private static bool Consume(ref ReadOnlySpan<char> buffer, int count, KeyPress key, out KeyPress result)
    {
        result = key;
        buffer = buffer[Math.Min(count, buffer.Length)..];
        return true;
    }

    private static KeyPress NamedUnknown() => KeyPress.Named(KeyKind.Unknown);
}
