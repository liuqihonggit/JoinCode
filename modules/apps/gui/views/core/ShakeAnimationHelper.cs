namespace JoinCode.Gui.Views;

/// <summary>
/// 窗口震动动画公共工具 — X 轴阻尼震动，可对任意 <see cref="Visual"/> 施加。
/// 提取自 <see cref="PermissionDialog"/> 的 StartShakeAnimation 私有方法，供 MCP shake_window 工具复用。
/// </summary>
public static class ShakeAnimationHelper {
    /// <summary>
    /// X 轴偏移序列 — 阻尼衰减: -8→+8→-6→+6→-4→+4→-2→+2→0
    /// </summary>
    private static readonly double[] s_offsets = { -8, 8, -6, 6, -4, 4, -2, 2, 0 };

    /// <summary>
    /// 每步间隔（毫秒）
    /// </summary>
    private const int StepMs = 50;

    /// <summary>
    /// 对指定 <see cref="Visual"/> 施加 X 轴阻尼震动动画。
    /// 总时长约 450ms，动画结束后 RenderTransform 归零。
    /// </summary>
    /// <param name="target">要震动的视觉元素（如 Window、Border 等）。</param>
    /// <param name="cancellationToken">取消令牌（可选）。</param>
    public static void Shake(Visual target, CancellationToken cancellationToken = default) {
        var shakeTransform = new TranslateTransform();
        target.RenderTransform = shakeTransform;

        var stepIndex = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StepMs) };
        timer.Tick += (_, _) => {
            if (cancellationToken.IsCancellationRequested || stepIndex >= s_offsets.Length) {
                timer.Stop();
                shakeTransform.X = 0;
                return;
            }
            shakeTransform.X = s_offsets[stepIndex];
            stepIndex++;
        };
        timer.Start();
    }

    /// <summary>
    /// 对指定 <see cref="Window"/> 的整个内容施加震动动画。
    /// 便捷方法，等价于 <see cref="Shake(Visual, CancellationToken)"/> 传入 window.Content。
    /// </summary>
    /// <param name="window">要震动的窗口。</param>
    /// <param name="cancellationToken">取消令牌（可选）。</param>
    public static void ShakeWindow(Window window, CancellationToken cancellationToken = default) {
        Shake(window, cancellationToken);
    }
}