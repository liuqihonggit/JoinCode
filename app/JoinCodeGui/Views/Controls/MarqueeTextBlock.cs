using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace JoinCode.Gui.Views.Controls;

/// <summary>
/// 走马灯文本控件（学习 opencode 状态栏）— 文本超宽时匀速横向滚动循环，
/// 不超宽时静态右对齐。热路径仅此小控件内部计时器（50ms 步进），
/// 对齐 ClaudeCode「动画钟只在最小子组件」的热路径隔离原则。
/// </summary>
public sealed class MarqueeTextBlock : Control
{
    private readonly TextBlock _inner = new()
    {
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        TextTrimming = TextTrimming.None,
        FontSize = 11
    };

    private double _offset;
    private bool _scrolling;
    private DateTime _lastStep = DateTime.UtcNow;

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, string>(nameof(Text), string.Empty);

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, IBrush>(nameof(Foreground), Brushes.Gray);

    /// <summary>滚动速度（px/秒）</summary>
    public static readonly StyledProperty<double> SpeedProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, double>(nameof(Speed), 40);

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Speed
    {
        get => GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    private readonly DispatcherTimerStub _timer;

    public MarqueeTextBlock()
    {
        _timer = new DispatcherTimerStub(Step);
        _timer.Start();
        VisualChildren.Add(_inner);
        LogicalChildren.Add(_inner);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _inner.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _inner.Arrange(new Rect(new Point(0, 0), _inner.DesiredSize));
        Clip = new RectangleGeometry(new Rect(0, 0, finalSize.Width, finalSize.Height));
        UpdateScrollState(finalSize.Width);
        return finalSize;
    }

    /// <summary>属性变更 — 文本/前景色同步到内层 TextBlock 并复位滚动</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TextProperty)
        {
            _inner.Text = e.NewValue as string ?? string.Empty;
            _offset = 0;
            InvalidateArrange();
        }
        else if (e.Property == ForegroundProperty)
        {
            _inner.Foreground = e.NewValue as IBrush ?? Brushes.Gray;
        }
    }

    private void UpdateScrollState(double viewportWidth)
    {
        var textWidth = _inner.DesiredSize.Width;
        _scrolling = textWidth > viewportWidth && !string.IsNullOrEmpty(Text);
        if (!_scrolling)
        {
            _offset = 0;
            // 静态时右对齐展示
            var x = Math.Max(0, viewportWidth - textWidth);
            _inner.RenderTransform = new TranslateTransform(x, 0);
        }
        else
        {
            _inner.RenderTransform = new TranslateTransform(_offset, 0);
        }
    }

    private void Step()
    {
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
    private sealed class DispatcherTimerStub(Action callback)
    {
        private readonly Avalonia.Threading.DispatcherTimer _timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };

        public void Start()
        {
            _timer.Tick += (_, _) =>
            {
                try { callback(); }
                catch (Exception ex) { App.LogDiag($"[MarqueeTextBlock] tick 异常: {ex.Message}"); }
            };
            _timer.Start();
        }
    }
}
