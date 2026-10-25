namespace NativeTui.Core.Repl;

/// <summary>输入模式（对齐基准 inputModes.ts — prompt/bash 两态）。
/// bash 模式：空输入+! 进入（剥前缀，后续输入当 bash 命令），提交后退出回 Prompt；
/// Esc+空输入退 Prompt（对齐基准 useCancelRequest.ts:131-135）。</summary>
public enum InputMode : byte {
    /// <summary>普通提示模式（默认）— 输入发引擎。</summary>
    Prompt,
    /// <summary>bash 直通模式（! 前缀进入）— 输入当 bash 命令本地执行，提交后退出回 Prompt。</summary>
    Bash,
}
