namespace NativeTui.Core.Repl;

/// <summary>
/// 密码遮蔽（S8）— readPassword 输入的等宽 * 遮蔽纯函数（数据层）：
/// 渲染层消费方为输入框（PromptInputBox S7 域 — 接线未决，届时输入回显走本函数替换原文）。
/// 长度 1:1 保持（光标定位/退格语义与普通输入一致 — 渲染层无需特判宽度）。
/// </summary>
public static class PasswordMask
{
    /// <summary>遮蔽输入文本 — 每字符替换为 '*'（空输入返回空串；长度保持）。</summary>
    /// <param name="input">用户实际输入（明文，仅存于数据层）。</param>
    /// <returns>等宽遮蔽回显文本。</returns>
    public static string Of(string input) => new('*', input.Length);
}
