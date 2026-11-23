using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DeckIsland.Core;

namespace DeckIsland;

public partial class MainWindow
{
    private TextBlock? _focusBadge;
    private FocusDial? _focusBadgeDial;

    private void InitializeFocus()
    {
        _app.FocusChanged += FocusChanged;
        Closed += (_, _) => _app.FocusChanged -= FocusChanged;
    }

    // A screen recording also leaves the collapsed island showing only its badge (CaptureBadge).
    private bool FocusCompactOnly => _app.Recording is not null || Settings.FocusSimplifiesCompact(EffectiveStyle) && _app.FocusSession.Phase != FocusPhase.Idle;

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

    private string FocusBadgeLabel()
    {
        if (_app.FocusNotice != FocusPhase.Idle) return FocusNoticeLabel();
        var session = _app.FocusSession;
        var label = session.Running ? FocusPhaseLabel() : FocusPausedLabel();
        return $"{FocusTime(session.Remaining(DateTimeOffset.UtcNow))}  {label}";
    }

    private static string FocusPausedLabel() => Loc.T("已暂停", "Paused");

    private FrameworkElement? CompactFocus(double scale)
    {
        if ((!Settings.ShowsFocus(EffectiveStyle) && !Settings.FocusSimplifiesCompact(EffectiveStyle)) ||
            _app.FocusSession.Phase == FocusPhase.Idle && _app.FocusNotice == FocusPhase.Idle)
            return null;
        var badge = WeatherText(FocusBadgeLabel(), CompactMusicLayout.CollapsedTextSize(Settings.QuotaRingStyle, scale, 11));
        Typography.SetNumeralAlignment(badge, FontNumeralAlignment.Tabular);
        if (_app.FocusNotice == FocusPhase.Idle)
        {
            string actual = badge.Text;
            string time = FocusTime(_app.FocusSession.Duration);
            double width = 0;
            foreach (string label in new[] { FocusPhaseLabel(), FocusPausedLabel() })
            {
                badge.Text = $"{time}  {label}";
                badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                width = Math.Max(width, badge.DesiredSize.Width);
            }
            badge.Text = actual;
            badge.MinWidth = Math.Ceiling(width) + 1;
        }
        double size = CompactMusicLayout.StatusRingSize(Settings.QuotaRingStyle, scale);
        _focusBadge = badge;
        _focusBadgeDial = new FocusDial { Width = size, Height = size, Margin = new Thickness(0, 0, 4 * scale, 0) };
        _focusBadgeDial.Update(_app.FocusSession, Animates);
        var host = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Children = { _focusBadgeDial, badge } };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Width = Math.Ceiling(host.DesiredSize.Width + 4 * scale);
        host.Height = Math.Ceiling(host.DesiredSize.Height);
        if (Settings.WidgetsPage && Settings.WidgetSlots.Contains(WidgetKind.Focus))
        {
            host.ToolTip = WidgetLayout.Name(WidgetKind.Focus);
            host.MouseLeftButtonUp += (_, e) => { OpenFromHotkey(DeckPage.Widgets); e.Handled = true; };
        }
        return host;
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
