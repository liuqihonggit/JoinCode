namespace NativeTui.Core.Repl.Vim;

/// <summary>
/// vim 单寄存器 — 基准 src/vim/types.ts:81-86 PersistentState 的 { register: string; registerIsLinewise: boolean }
///（无多寄存器/a-z 命名寄存器体系，基准仅无名寄存器一个）。
/// 数据通道：算子删除/复制写入（operators.ts:505 setRegister），p/P 读出（operators.ts:299 getRegister）。
/// linewise 判定以内容尾随换行为准（operators.ts:302 `register.endsWith('\n')`），
/// IsLinewise 字段与基准 registerIsLinewise 同源 —— 仅作记录，粘贴路径不消费它（对齐基准）。
/// </summary>
public sealed class VimRegister
{
    /// <summary>创建空寄存器（基准 createInitialPersistentState，types.ts:192-198）。</summary>
    public VimRegister()
    {
    }

    /// <summary>创建指定内容的寄存器（测试/恢复用）。</summary>
    public VimRegister(string content, bool linewise)
    {
        Content = content;
        IsLinewise = linewise;
    }

    /// <summary>寄存器内容（linewise 内容以 \n 结尾 — 算子写入时保证，operators.ts:121-124/501-504）。</summary>
    public string Content { get; private set; } = string.Empty;

    /// <summary>最近一次写入是否 linewise（记录性字段 — 粘贴路径按 <see cref="EndsLinewise"/> 判定，对齐基准）。</summary>
    public bool IsLinewise { get; private set; }

    /// <summary>寄存器为空（无内容可粘贴 — 空寄存器粘贴为无操作，operators.ts:299-300）。</summary>
    public bool IsEmpty => Content.Length == 0;

    /// <summary>按内容尾随换行判定 linewise（粘贴分流依据，operators.ts:302）。</summary>
    public bool EndsLinewise => Content.EndsWith('\n');

    /// <summary>
    /// 写入寄存器 — 对齐基准 setRegister(content, linewise)
    ///（operators.ts:33-34：register = content + registerIsLinewise = linewise；linewise 时内容保证尾随 \n）。
    /// </summary>
    /// <param name="content">内容（linewise 时调用方已保证尾随 \n）。</param>
    /// <param name="linewise">是否行级（dd/yy/j/k/G 系 motion）。</param>
    public void Write(string content, bool linewise)
    {
        Content = content;
        IsLinewise = linewise;
    }

    /// <summary>清空寄存器（测试/重置用）。</summary>
    public void Clear()
    {
        Content = string.Empty;
        IsLinewise = false;
    }
}
