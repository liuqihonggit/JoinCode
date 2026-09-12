namespace JoinCode.Gui.Views;

/// <summary>
/// 权限确认弹窗 — 引擎权限待确认时由 MainWindow 注入回调弹出。
/// 三个决策按钮：拒绝 / 允许本次 / 始终允许；关闭窗口等价于拒绝。
/// UI 由 PermissionDialog.axaml 定义，按钮通过 Command 绑定关闭返回决策。
/// 根据 DangerLevel 区分盾徽/标题/边框颜色（黄灯黄/绿灯绿/红灯红/黑灯深红），
/// 黄灯/红灯触发 X 轴震动动画引起用户警觉。
/// </summary>
public sealed partial class PermissionDialog : Window
{
    /// <summary>拒绝命令（关闭窗口返回 Deny）</summary>
    public System.Windows.Input.ICommand DenyCommand { get; }

    /// <summary>允许本次命令（关闭窗口返回 Allow）</summary>
    public System.Windows.Input.ICommand AllowCommand { get; }

    /// <summary>始终允许命令（关闭窗口返回 AlwaysAllow）</summary>
    public System.Windows.Input.ICommand AlwaysAllowCommand { get; }

    public PermissionDialog()
    {
        InitializeComponent();
        DenyCommand = new RelayCommand(() => Close(PermissionConfirmationDecision.Deny));
        AllowCommand = new RelayCommand(() => Close(PermissionConfirmationDecision.Allow));
        AlwaysAllowCommand = new RelayCommand(() => Close(PermissionConfirmationDecision.AlwaysAllow));
    }

    /// <summary>以权限请求为 DataContext 构建弹窗</summary>
    public PermissionDialog(PermissionConfirmationRequest request) : this()
    {
        DataContext = request;
        ApplyDangerLevelColors(request.DangerLevel);
        if (request.ShouldShake)
            StartShakeAnimation();
    }

    /// <summary>
    /// 根据危险等级设置盾徽背景/标题前景/内容边框颜色。
    /// null → 默认中性色；黄灯 → 黄；绿灯 → 绿；红灯 → 红；黑灯 → 深红。
    /// </summary>
    private void ApplyDangerLevelColors(CommandDangerLevel? dangerLevel)
    {
        var resourceKey = dangerLevel switch
        {
            CommandDangerLevel.Unknown => "GuiDangerLevelYellow",
            CommandDangerLevel.LightValidation => "GuiDangerLevelGreen",
            CommandDangerLevel.Execution => "GuiDangerLevelRed",
            CommandDangerLevel.Dangerous => "GuiDangerLevelBlack",
            _ => null
        };

        if (resourceKey is null)
            return;

        var brush = this.TryFindResource(resourceKey, out var value) ? value as IBrush : null;
        if (brush is null)
            return;

        ShieldIcon.Background = brush;
        TitleText.Foreground = brush;
        ContentBorder.BorderBrush = brush;
    }

    /// <summary>
    /// X 轴阻尼震动动画 — 左右快速偏移逐步衰减（-8→+8→-6→+6→-4→+4→-2→+2→0），
    /// 每步 50ms，总时长 ~450ms。黄灯(未知命令)/红灯(不可撤回)触发以引起用户警觉。
    /// </summary>
    private void StartShakeAnimation()
    {
        var shakeTransform = new TranslateTransform();
        RootBorder.RenderTransform = shakeTransform;

        var offsets = new double[] { -8, 8, -6, 6, -4, 4, -2, 2, 0 };
        var stepMs = 50;
        var stepIndex = 0;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(stepMs) };
        timer.Tick += (_, _) =>
        {
            if (stepIndex >= offsets.Length)
            {
                timer.Stop();
                return;
            }
            shakeTransform.X = offsets[stepIndex];
            stepIndex++;
        };
        timer.Start();
    }
}
