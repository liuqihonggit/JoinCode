namespace NativeTui.Core.Repl.Vim;

/// <summary>
/// vim 模式 — 基准 types/textInputTypes.ts:222 `VimMode = 'INSERT' | 'NORMAL'` 仅两态
///（基准 src/vim/types.ts:49-51 VimState 判别联合同源）。
/// 基准没有 Visual/VisualLine/VisualBlock/Replace 模式 —— 替换字符是 NORMAL 下的 r 瞬时命令
///（types.ts:74 replace 状态），选区模式未实现，按"基准没有的不做"原则不引入。
/// </summary>
public enum VimMode
{
    /// <summary>插入模式（默认初始态 — 基准 createInitialVimState 返回 INSERT，types.ts:188-190）：
    /// 输入透传给宿主输入缓冲，引擎仅跟踪 insertedText 供 dot-repeat（useVimInput.ts:209-229）。</summary>
    Insert,

    /// <summary>普通模式：命令状态机解析按键（operator/motion/text-object/count 组合）。</summary>
    Normal,
}
