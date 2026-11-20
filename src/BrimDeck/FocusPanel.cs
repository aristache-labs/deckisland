using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using BrimDeck.Core;

namespace BrimDeck;

public partial class MainWindow
{
    private TextBlock? _focusBadge;
    private FocusDial? _focusBadgeDial;
    private TextBlock? _focusHeading, _focusDetail, _focusReading;
    private FocusDial? _focusDial;
    private Button? _focusToggle, _focusReset, _focusShorter, _focusLonger;
    private (int Theme, string Language) _focusAppearance;

    private void InitializeFocus()
    {
        _app.FocusChanged += FocusChanged;
        Closed += (_, _) => _app.FocusChanged -= FocusChanged;
    }

    private void FocusPage_Click(object sender, RoutedEventArgs e) => SelectPage(DeckPage.Focus);

    private bool FocusCompactOnly => Settings.FocusSimplifiesCompact(EffectiveStyle) && _app.FocusSession.Phase != FocusPhase.Idle;

    private void FocusChanged(bool rebuild)
    {
        if (rebuild && (Settings.ShowsFocus(EffectiveStyle) || Settings.FocusSimplifiesCompact(EffectiveStyle))) RenderCompact();
        UpdateFocus();
    }

    private static string FocusTime(TimeSpan remaining)
    {
        int seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private string FocusPhaseLabel() => _app.FocusSession.Phase switch
    {
        FocusPhase.Focus => Loc.T("专注", "Focus"),
        FocusPhase.Break => Loc.T("休息", "Break"),
        _ => Loc.T("专注计时器", "Focus timer")
    };

    private string FocusNoticeLabel() => _app.FocusNotice switch
    {
        FocusPhase.Focus => Loc.T("专注完成，休息一下", "Focus done, take a break"),
        FocusPhase.Break => Loc.T("休息结束", "Break complete"),
        _ => ""
    };

    private string FocusBadgeLabel() => _app.FocusNotice == FocusPhase.Idle
        ? $"{FocusTime(_app.FocusSession.Remaining(DateTimeOffset.UtcNow))}  {FocusPhaseLabel()}"
        : FocusNoticeLabel();

    private FrameworkElement? CompactFocus(double scale)
    {
        if ((!Settings.ShowsFocus(EffectiveStyle) && !Settings.FocusSimplifiesCompact(EffectiveStyle)) ||
            _app.FocusSession.Phase == FocusPhase.Idle && _app.FocusNotice == FocusPhase.Idle)
            return null;
        var badge = WeatherText(FocusBadgeLabel(), CompactMusicLayout.CollapsedTextSize(Settings.QuotaRingStyle, scale, 11));
        Typography.SetNumeralAlignment(badge, FontNumeralAlignment.Tabular);
        double size = CompactMusicLayout.StatusRingSize(Settings.QuotaRingStyle, scale);
        _focusBadge = badge;
        _focusBadgeDial = new FocusDial { Width = size, Height = size, Margin = new Thickness(0, 0, 4 * scale, 0) };
        _focusBadgeDial.Update(_app.FocusSession, Animates);
        var host = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Children = { _focusBadgeDial, badge },
            ToolTip = Loc.T("打开专注计时器", "Open Focus timer") };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Width = Math.Ceiling(host.DesiredSize.Width + 4 * scale);
        host.Height = Math.Ceiling(host.DesiredSize.Height);
        host.MouseLeftButtonUp += (_, e) => { OpenFromHotkey(DeckPage.Focus); e.Handled = true; };
        return host;
    }

    private void RenderFocus()
    {
        RefreshStatus();
        UsageContent.Visibility = MusicContent.Visibility = MonitorContent.Visibility = ShelfContent.Visibility =
            DevicesContent.Visibility = WeatherContent.Visibility = Visibility.Collapsed;
        ClockLabel.Visibility = Visibility.Visible;
        ModelToolbar.Visibility = ApplicationPages.Visibility = Visibility.Collapsed;
        double height = ContentCanvas.Height;
        if (!double.IsFinite(height)) return;
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        FocusContent.Margin = new Thickness(_header.Side, 0, _header.Side, MonitorBottom(height));
        var appearance = (Look.Generation, Loc.Language);
        if (FocusContent.Children.Count == 0 || _focusAppearance != appearance)
        {
            _focusAppearance = appearance;
            FocusContent.Children.Clear();
            BuildFocusCard();
        }
        UpdateFocus();
    }

    private void BuildFocusCard()
    {
        var face = new Grid { Width = 78, Height = 78, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        _focusDial = new FocusDial();
        face.Children.Add(_focusDial);
        _focusReading = WeatherText("", 16);
        _focusReading.HorizontalAlignment = HorizontalAlignment.Center;
        _focusReading.FontWeight = FontWeights.SemiBold;
        Typography.SetNumeralAlignment(_focusReading, FontNumeralAlignment.Tabular);
        face.Children.Add(_focusReading);

        Button Control(string text, Action action, string? label = null)
        {
            var control = new Button { Content = text, Style = (Style)FindResource("PanelOutlineButton"), Margin = new Thickness(0, 0, 6, 0) };
            if (label is not null) AutomationProperties.SetName(control, label);
            control.Click += (_, _) => action();
            return control;
        }
        _focusHeading = WeatherText("", 16);
        _focusHeading.FontWeight = FontWeights.SemiBold;
        _focusDetail = WeatherText("", 12);
        _focusToggle = Control("", () => _app.ToggleFocus(Profile));
        _focusReset = Control(Loc.T("重置", "Reset"), _app.ResetFocus);
        _focusShorter = Control("−", () => AdjustFocus(-1), Loc.T("减少一分钟", "Decrease focus by one minute"));
        _focusLonger = Control("+", () => AdjustFocus(1), Loc.T("增加一分钟", "Increase focus by one minute"));
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0),
            Children = { _focusToggle, _focusReset, _focusShorter, _focusLonger } };
        var settings = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { _focusHeading, _focusDetail, controls } };
        var body = new DockPanel { Children = { face, settings } };
        FocusContent.Children.Add(new Border
        {
            Child = body, Padding = new Thickness(12, 6, 12, 6), CornerRadius = new CornerRadius(12),
            BorderBrush = UI.Brush(Look.Hex("rim")), BorderThickness = new Thickness(1), Background = UI.Brush(Look.Hex("surface"))
        });
    }

    private void AdjustFocus(int change)
    {
        var edited = Settings.Copy();
        edited.FocusMinutes = FocusTimer.ClampMinutes(edited.FocusMinutes + change);
        _app.UpdateSettingsFor(Profile, edited);
    }

    private void UpdateFocus()
    {
        if (_focusBadge is not null)
        {
            _focusBadge.Text = FocusBadgeLabel();
            _focusBadgeDial?.Update(_app.FocusSession, Animates);
        }
        if (_focusReading is null || CurrentPage != DeckPage.Focus || FocusContent.Children.Count == 0) return;
        var timer = _app.FocusSession;
        bool idle = timer.Phase == FocusPhase.Idle;
        var remaining = idle ? TimeSpan.FromMinutes(Settings.FocusMinutes) : timer.Remaining(DateTimeOffset.UtcNow);
        _focusReading.Text = FocusTime(remaining);
        _focusHeading!.Text = FocusPhaseLabel();
        var notice = FocusNoticeLabel();
        _focusDetail!.Text = notice.Length > 0 ? notice : idle
            ? Loc.T($"{Settings.FocusMinutes} + {Settings.BreakMinutes} 分钟", $"{Settings.FocusMinutes} + {Settings.BreakMinutes} min")
            : timer.Running ? Loc.T("进行中", "In progress") : Loc.T("已暂停", "Paused");
        _focusToggle!.Content = idle ? Loc.T("开始", "Start") : timer.Running ? Loc.T("暂停", "Pause") : Loc.T("继续", "Resume");
        _focusReset!.IsEnabled = !idle;
        _focusShorter!.IsEnabled = idle && Settings.FocusMinutes > FocusTimer.MinimumMinutes;
        _focusLonger!.IsEnabled = idle && Settings.FocusMinutes < FocusTimer.MaximumMinutes;
        _focusDial!.Update(timer, Animates);
    }

    private sealed class FocusDial : FrameworkElement
    {
        private FocusTimer? _timer;
        private bool _smooth;
        private double _progress;

        public FocusDial() => IsVisibleChanged += (_, _) => UpdateMotion();

        internal void Update(FocusTimer timer, bool smooth)
        {
            _timer = timer;
            _smooth = smooth;
            UpdateMotion();
        }

        private void UpdateMotion()
        {
            if (AdvanceFrame(0)) Motion.Start(this, AdvanceFrame);
        }

        private bool AdvanceFrame(double _)
        {
            if (!IsVisible || _timer is not { } timer) return false;
            var remaining = timer.Remaining(DateTimeOffset.UtcNow);
            _progress = timer.Duration > TimeSpan.Zero
                ? 1 - Math.Clamp(remaining.TotalSeconds / timer.Duration.TotalSeconds, 0, 1) : 0;
            InvalidateVisual();
            return _smooth && timer.Running && remaining > TimeSpan.Zero;
        }
        protected override void OnRender(DrawingContext drawing)
        {
            var look = PanelLook.Of(this);
            double size = Math.Max(1, Math.Min(ActualWidth, ActualHeight) - 2);
            double thickness = Math.Min(size / 2, Math.Clamp(size / 15, 1.8, 5));
            DevicesView.Ring(drawing, new Point(ActualWidth / 2, ActualHeight / 2), size, thickness, _progress,
                look.Hex(_timer?.Phase == FocusPhase.Break ? "breakProgress" : "focusProgress"), look: look);
        }
    }
}
