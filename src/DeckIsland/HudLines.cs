using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DeckIsland.Core;

namespace DeckIsland;

// A notification card's line (its title or its text) as it shows: cut with an ellipsis while it fits or cannot scroll, else scrolling
// left once (HudScroll), started where `elapsed` (seconds since the card appeared) has it, so a card built again carries on. Used by
// the island's card (MainWindow.Hud.cs) and the floating window's (HudWindow.cs).
internal static class HudLines
{
    // Returns what to place for the line and how long the card must stay for it, in seconds.
    public static (FrameworkElement View, double Hold) Fit(TextBlock line, double room, double scale, double holdSeconds, int limitSeconds, double elapsed)
    {
        line.ClearValue(FrameworkElement.WidthProperty); line.RenderTransform = null;
        line.TextTrimming = TextTrimming.CharacterEllipsis; line.TextWrapping = TextWrapping.NoWrap; line.HorizontalAlignment = HorizontalAlignment.Stretch;
        line.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double natural = line.DesiredSize.Width, overflow = natural - room;
        var (scroll, total) = HudScroll.Plan(overflow / Math.Max(.1, scale), holdSeconds, limitSeconds);
        if (scroll <= 0 || holdSeconds <= 0) return (line, holdSeconds);
        line.TextTrimming = TextTrimming.None; line.HorizontalAlignment = HorizontalAlignment.Left; line.Width = natural;
        var move = new TranslateTransform();
        line.RenderTransform = move;
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -Math.Min(overflow, scroll * HudScroll.Speed * scale), TimeSpan.FromSeconds(scroll))
            { BeginTime = TimeSpan.FromSeconds(HudScroll.Lead - elapsed), FillBehavior = FillBehavior.HoldEnd });
        // The room clips the line; the canvas leaves it its whole width, so the end that scrolls in is drawn.
        double height = double.IsNaN(line.Height) ? line.DesiredSize.Height : line.Height;
        return (new Canvas { Height = height, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Stretch, Children = { line } }, total);
    }
}
