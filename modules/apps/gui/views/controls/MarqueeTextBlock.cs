namespace JoinCode.Gui.Views.Controls;

/// <summary>
/// 走马灯文本控件（学习 opencode 状态栏）— 文本超宽时匀速横向滚动循环，
/// 不超宽时静态右对齐。热路径仅此小控件内部计时器（50ms 步进），
/// 对齐 TS 原版「动画钟只在最小子组件」的热路径隔离原则。
/// </summary>
public sealed class MarqueeTextBlock : Control {
    private readonly TextBlock _inner = new() {
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        TextTrimming = TextTrimming.None,
        FontSize = 11
    };

    private double _offset;
    private bool _scrolling;
    private bool _attached;
    private DateTime _lastStep = DateTime.UtcNow;

    /// <summary>Text 依赖属性</summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, string>(nameof(Text), string.Empty);

    /// <summary>Foreground 依赖属性</summary>
    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, IBrush>(nameof(Foreground), Brushes.Gray);

    /// <summary>滚动速度（px/秒）</summary>
    public static readonly StyledProperty<double> SpeedProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, double>(nameof(Speed), 40);

    /// <summary>动效开关，关闭后完整文本可通过工具提示读取。</summary>
    public static readonly StyledProperty<bool> AnimationsEnabledProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, bool>(nameof(AnimationsEnabled), true);

    /// <summary>是否允许滚动。</summary>
    public bool AnimationsEnabled {
        get => GetValue(AnimationsEnabledProperty);
        set => SetValue(AnimationsEnabledProperty, value);
    }

    /// <summary>显示文本</summary>
    public string Text {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>前景色画刷</summary>
    public IBrush Foreground {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>滚动速度（px/秒）</summary>
    public double Speed {
        get => GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    private readonly DispatcherTimerStub _timer;

    /// <summary>初始化走马灯文本控件；仅挂载且需要滚动时启动计时器。</summary>
    public MarqueeTextBlock() {
        _timer = new DispatcherTimerStub(Step);
        VisualChildren.Add(_inner);
        LogicalChildren.Add(_inner);
    }

    /// <summary>测量布局</summary>
    protected override Size MeasureOverride(Size availableSize) {
        _inner.Measure(availableSize);
        return availableSize;
    }

    /// <summary>排列布局</summary>
    protected override Size ArrangeOverride(Size finalSize) {
        _inner.Arrange(new Rect(new Point(0, 0), _inner.DesiredSize));
        Clip = new RectangleGeometry(new Rect(0, 0, finalSize.Width, finalSize.Height));
        UpdateScrollState(finalSize.Width);
        return finalSize;
    }

    /// <summary>属性变更 — 文本/前景色同步到内层 TextBlock 并复位滚动</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e) {
        base.OnPropertyChanged(e);
        if (e.Property == TextProperty) {
            _inner.Text = e.NewValue as string ?? string.Empty;
            ToolTip.SetTip(this, _inner.Text);
            _offset = 0;
            InvalidateArrange();
        } else if (e.Property == ForegroundProperty) {
            _inner.Foreground = e.NewValue as IBrush ?? Brushes.Gray;
        } else if (e.Property == AnimationsEnabledProperty) {
            _offset = 0;
            _lastStep = DateTime.UtcNow;
            UpdateScrollState(Bounds.Width);
            InvalidateArrange();
        }
    }

    /// <summary>挂载时恢复动画。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateScrollState(Bounds.Width);
    }

    /// <summary>卸载时停止计时器，避免脱离窗口仍有回调。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
        _attached = false;
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateScrollState(double viewportWidth) {
        var textWidth = _inner.DesiredSize.Width;
        _scrolling = AnimationsEnabled && textWidth > viewportWidth && !string.IsNullOrEmpty(Text);
        if (_scrolling && _attached) _timer.Start(); else _timer.Stop();
        if (!_scrolling) {
            _offset = 0;
            // 静态时右对齐展示
            var x = AnimationsEnabled ? Math.Max(0, viewportWidth - textWidth) : 0;
            _inner.RenderTransform = new TranslateTransform(x, 0);
        } else {
            _inner.RenderTransform = new TranslateTransform(_offset, 0);
        }
    }

    private void Step() {
        if (!_scrolling || Bounds.Width <= 0)
            return;
        var now = DateTime.UtcNow;
        var dt = (now - _lastStep).TotalSeconds;
        _lastStep = now;
        var textWidth = _inner.DesiredSize.Width;
        var gap = 60; // 循环间隔空隙 px
        _offset -= Speed * dt;
        var total = textWidth + gap;
        if (-_offset > total)
            _offset = Bounds.Width; // 从右侧重新进入
        _inner.RenderTransform = new TranslateTransform(_offset, 0);
    }

    /// <summary>UI 线程计时器（50ms 步进，仅本控件热路径）</summary>
    private sealed class DispatcherTimerStub {
        private readonly Avalonia.Threading.DispatcherTimer _timer;

        /// <summary>只注册一次回调，重复启停不累计事件。</summary>
        public DispatcherTimerStub(Action callback) {
            _timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (_, _) => {
                try { callback(); } catch (Exception ex) { App.LogDiag($"[MarqueeTextBlock] tick 异常: {ex.Message}"); }
            };
        }

        /// <summary>启动计时器</summary>
        public void Start() => _timer.Start();

        /// <summary>停止计时器。</summary>
        public void Stop() => _timer.Stop();
    }
}
