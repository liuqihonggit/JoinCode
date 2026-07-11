namespace JoinCode.Cli;

/// <summary>
/// Vim 引擎 — CLI 简化版存根，提供基本模式切换但无完整 Vim 键绑定
/// </summary>
[Register]
public sealed class VimEngine : IVimEngine
{
    public VimMode CurrentMode { get; private set; }
    public bool IsEnabled { get; private set; }
    public string CommandBuffer { get; private set; } = string.Empty;
    public int RepeatCount { get; private set; }
    public bool IsRecordingMacro { get; private set; }
    public char? MacroRegister { get; private set; }
    public bool HasSearchHighlight { get; private set; }
    public int DisplayWidth { get; private set; } = 80;
    public int ViewportHeight { get; private set; } = 24;

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;
    public void SetDisplayWidth(int width) => DisplayWidth = width;
    public void SetViewportHeight(int height) => ViewportHeight = height;

    public VimMode SwitchToMode(VimMode mode)
    {
        var oldMode = CurrentMode;
        CurrentMode = mode;
        ModeChanged?.Invoke(this, mode);
        return oldMode;
    }

    public VimKeyResult ProcessKey(ConsoleKeyInfo keyInfo, StringBuilder input, ref int cursorPosition)
    {
        if (!IsEnabled) return VimKeyResult.NotHandled;

        if (keyInfo.Key == ConsoleKey.Escape)
        {
            if (CurrentMode != VimMode.Normal)
            {
                SwitchToMode(VimMode.Normal);
                return VimKeyResult.Handled;
            }
            return VimKeyResult.Cancelled;
        }

        if (CurrentMode == VimMode.Normal && keyInfo.KeyChar == 'i')
        {
            SwitchToMode(VimMode.Insert);
            return VimKeyResult.Handled;
        }

        if (CurrentMode == VimMode.Insert && keyInfo.Key == ConsoleKey.Enter)
        {
            return VimKeyResult.Submit(input.ToString());
        }

        return VimKeyResult.NotHandled;
    }

    public void SetRegister(VimRegisterName name, string text, bool isLinewise = false) { }
    public VimRegisterContent? GetRegister(VimRegisterName name) => null;
    public void SetMark(string name, int position, int line, int column) { }
    public VimMark? GetMark(string name) => null;
    public void StartMacroRecording(char register) { IsRecordingMacro = true; MacroRegister = register; }
    public void StopMacroRecording() { IsRecordingMacro = false; MacroRegister = null; }
    public bool ReplayMacro(char register, StringBuilder input, ref int cursorPosition) => false;

    public event EventHandler<VimMode>? ModeChanged;
#pragma warning disable CS0067
    public event EventHandler<VimCommandEventArgs>? CommandExecuted;
#pragma warning restore CS0067
}
