namespace JoinCode.Cli;

/// <summary>
/// Vim 引擎 — CLI 简化版存根，提供基本模式切换但无完整 Vim 键绑定
/// </summary>
[Register(typeof(IVimEngine), ServiceLifetime.Singleton)]
public sealed partial class VimEngine : ServiceEntity, IVimEngine {
    /// <summary>当前 Vim 模式</summary>
    public VimMode CurrentMode { get; private set; }
    /// <summary>是否启用 Vim 引擎</summary>
    public bool IsEnabled { get; private set; }
    /// <summary>命令缓冲区内容</summary>
    public string CommandBuffer { get; private set; } = string.Empty;
    /// <summary>重复次数（如 3dd 表示删除 3 行）</summary>
    public int RepeatCount { get; private set; }
    /// <summary>是否正在录制宏</summary>
    public bool IsRecordingMacro { get; private set; }
    /// <summary>当前宏录制寄存器名</summary>
    public char? MacroRegister { get; private set; }
    /// <summary>是否有搜索高亮</summary>
    public bool HasSearchHighlight { get; private set; }
    /// <summary>显示宽度（列数，默认 80）</summary>
    public int DisplayWidth { get; private set; } = 80;
    /// <summary>视口高度（行数，默认 24）</summary>
    public int ViewportHeight { get; private set; } = 24;

    /// <summary>启用 Vim 引擎</summary>
    public void Enable() => IsEnabled = true;
    /// <summary>禁用 Vim 引擎</summary>
    public void Disable() => IsEnabled = false;
    /// <summary>设置显示宽度</summary>
    /// <param name="width">显示宽度（列数）</param>
    public void SetDisplayWidth(int width) => DisplayWidth = width;
    /// <summary>设置视口高度</summary>
    /// <param name="height">视口高度（行数）</param>
    public void SetViewportHeight(int height) => ViewportHeight = height;

    /// <summary>切换到指定 Vim 模式，并触发 ModeChanged 事件</summary>
    /// <param name="mode">目标模式</param>
    /// <returns>切换前的旧模式</returns>
    public VimMode SwitchToMode(VimMode mode) {
        var oldMode = CurrentMode;
        CurrentMode = mode;
        ModeChanged?.Invoke(this, mode);
        return oldMode;
    }

    /// <summary>处理按键输入，根据当前模式执行对应操作</summary>
    /// <param name="keyInfo">按键信息</param>
    /// <param name="input">输入缓冲区</param>
    /// <param name="cursorPosition">光标位置（引用传递）</param>
    /// <returns>按键处理结果</returns>
    public VimKeyResult ProcessKey(ConsoleKeyInfo keyInfo, StringBuilder input, ref int cursorPosition) {
        if (!IsEnabled) return VimKeyResult.NotHandled;

        if (keyInfo.Key == ConsoleKey.Escape) {
            if (CurrentMode != VimMode.Normal) {
                SwitchToMode(VimMode.Normal);
                return VimKeyResult.Handled;
            }
            return VimKeyResult.Cancelled;
        }

        if (CurrentMode == VimMode.Normal && keyInfo.KeyChar == 'i') {
            SwitchToMode(VimMode.Insert);
            return VimKeyResult.Handled;
        }

        if (CurrentMode == VimMode.Insert && keyInfo.Key == ConsoleKey.Enter) {
            return VimKeyResult.Submit(input.ToString());
        }

        return VimKeyResult.NotHandled;
    }

    /// <summary>设置指定寄存器的内容（存根实现，无实际存储）</summary>
    /// <param name="name">寄存器名</param>
    /// <param name="text">寄存器文本</param>
    /// <param name="isLinewise">是否为行方向</param>
    public void SetRegister(VimRegisterName name, string text, bool isLinewise = false) { }
    /// <summary>获取指定寄存器的内容（存根实现，始终返回 null）</summary>
    /// <param name="name">寄存器名</param>
    /// <returns>寄存器内容，不存在则返回 null</returns>
    public VimRegisterContent? GetRegister(VimRegisterName name) => null;
    /// <summary>设置命名标记（存根实现，无实际存储）</summary>
    /// <param name="name">标记名</param>
    /// <param name="position">绝对位置</param>
    /// <param name="line">行号</param>
    /// <param name="column">列号</param>
    public void SetMark(string name, int position, int line, int column) { }
    /// <summary>获取命名标记（存根实现，始终返回 null）</summary>
    /// <param name="name">标记名</param>
    /// <returns>标记信息，不存在则返回 null</returns>
    public VimMark? GetMark(string name) => null;
    /// <summary>开始录制宏到指定寄存器</summary>
    /// <param name="register">寄存器名</param>
    public void StartMacroRecording(char register) { IsRecordingMacro = true; MacroRegister = register; }
    /// <summary>停止宏录制</summary>
    public void StopMacroRecording() { IsRecordingMacro = false; MacroRegister = null; }
    /// <summary>回放指定寄存器中录制的宏（存根实现，始终返回 false）</summary>
    /// <param name="register">寄存器名</param>
    /// <param name="input">输入缓冲区</param>
    /// <param name="cursorPosition">光标位置（引用传递）</param>
    /// <returns>是否成功回放</returns>
    public bool ReplayMacro(char register, StringBuilder input, ref int cursorPosition) => false;

    /// <summary>模式切换事件 — 切换模式后触发</summary>
    public event EventHandler<VimMode>? ModeChanged;
#pragma warning disable CS0067
    /// <summary>命令执行完成事件</summary>
    public event EventHandler<VimCommandEventArgs>? CommandExecuted;
#pragma warning restore CS0067
}