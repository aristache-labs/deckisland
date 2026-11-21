using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DeckIsland.Core;

namespace DeckIsland;

// The settings window's slider knobs and switches move in code, on the app's one rule (Soft.Enabled): their storyboards in
// SettingsResources.xaml played even with animations off or in high contrast (motion-review F5). A knob grows a little under
// the pointer on the monitor's hover glide; a switch's knob slides across in the short time a press lets go.
internal static class SettingsMotion
{
    private const double KnobHover = 1.08, KnobDrag = 1.18, SwitchTravel = 16, SwitchKnob = 18, SwitchStretch = 22.0 / 18;

    // On a slider's thumb whose template has a "KnobScale".
    public static readonly DependencyProperty KnobProperty = DependencyProperty.RegisterAttached(
        "Knob", typeof(bool), typeof(SettingsMotion), new PropertyMetadata(false, KnobChanged));
    public static bool GetKnob(DependencyObject element) => (bool)element.GetValue(KnobProperty);
    public static void SetKnob(DependencyObject element, bool value) => element.SetValue(KnobProperty, value);

    // On a switch (a check box) whose template has a "KnobShift", the knob's TranslateTransform.
    public static readonly DependencyProperty SwitchProperty = DependencyProperty.RegisterAttached(
        "Switch", typeof(bool), typeof(SettingsMotion), new PropertyMetadata(false, SwitchChanged));
    public static bool GetSwitch(DependencyObject element) => (bool)element.GetValue(SwitchProperty);
    public static void SetSwitch(DependencyObject element, bool value) => element.SetValue(SwitchProperty, value);

    // On a tooltip: it fades in as it drops into place (Soft.Enter), as the window's pop-ups do, also when keyboard focus opens it.
    public static readonly DependencyProperty TipProperty = DependencyProperty.RegisterAttached(
        "Tip", typeof(bool), typeof(SettingsMotion), new PropertyMetadata(false, TipChanged));
    public static bool GetTip(DependencyObject element) => (bool)element.GetValue(TipProperty);
    public static void SetTip(DependencyObject element, bool value) => element.SetValue(TipProperty, value);

    private static void TipChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ToolTip tip) return;
        tip.Opened -= TipOpened;
        if ((bool)e.NewValue) tip.Opened += TipOpened;
    }

    private static void TipOpened(object sender, RoutedEventArgs e) => Soft.Enter((ToolTip)sender, drop: 3);

    private static void KnobChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Thumb thumb) return;
        thumb.MouseEnter -= KnobHovered; thumb.MouseLeave -= KnobHovered;
        thumb.DragStarted -= KnobDragged; thumb.DragCompleted -= KnobDropped;
        if (!(bool)e.NewValue) return;
        thumb.MouseEnter += KnobHovered; thumb.MouseLeave += KnobHovered;
        thumb.DragStarted += KnobDragged; thumb.DragCompleted += KnobDropped;
    }

    private static void KnobHovered(object sender, System.Windows.Input.MouseEventArgs e)
    {
        // A drag holds its own size until it ends.
        if (sender is not Thumb { IsDragging: false } thumb) return;
        // Touch and a pen do not hover (motion-review F4).
        GrowKnob(thumb, PressFeedback.Hovers(thumb.IsMouseOver, e.StylusDevice is not null) ? KnobHover : 1);
    }

    // Held, the knob grows a little more; let go, it settles back to its hover size, or to its own off the pointer.
    private static void KnobDragged(object sender, DragStartedEventArgs e) => GrowKnob((Thumb)sender, KnobDrag);
    private static void KnobDropped(object sender, DragCompletedEventArgs e)
    {
        var thumb = (Thumb)sender;
        GrowKnob(thumb, PressFeedback.Hovers(thumb.IsMouseOver, System.Windows.Input.Stylus.CurrentStylusDevice is not null) ? KnobHover : 1);
    }

    private static void GrowKnob(Thumb thumb, double to)
    {
        if (thumb.Template?.FindName("KnobScale", thumb) is not ScaleTransform scale) return;
        Soft.Glide(scale, ScaleTransform.ScaleXProperty, to, Soft.Enabled, Soft.Quick);
        Soft.Glide(scale, ScaleTransform.ScaleYProperty, to, Soft.Enabled, Soft.Quick);
    }

    private static void SwitchChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not ToggleButton toggle) return;
        toggle.Checked -= Toggled; toggle.Unchecked -= Toggled; toggle.Indeterminate -= Toggled; toggle.Loaded -= Placed;
        toggle.PreviewMouseLeftButtonDown -= SwitchPressed; toggle.PreviewMouseLeftButtonUp -= SwitchReleased; toggle.LostMouseCapture -= SwitchReleased;
        if (!(bool)e.NewValue) return;
        toggle.Checked += Toggled; toggle.Unchecked += Toggled; toggle.Indeterminate += Toggled; toggle.Loaded += Placed;
        toggle.PreviewMouseLeftButtonDown += SwitchPressed; toggle.PreviewMouseLeftButtonUp += SwitchReleased; toggle.LostMouseCapture += SwitchReleased;
    }

    // Pressed, the knob stretches from 18 to 22 wide, held at the side it rests on so it reaches toward where it will go; let
    // go, it narrows back while it slides (Slide).
    private static void SwitchPressed(object sender, System.Windows.Input.MouseButtonEventArgs e) => Stretch((ToggleButton)sender, true);
    private static void SwitchReleased(object sender, System.Windows.Input.MouseEventArgs e) => Stretch((ToggleButton)sender, false);

    private static void Stretch(ToggleButton toggle, bool pressed)
    {
        if (toggle.Template?.FindName("KnobStretch", toggle) is not ScaleTransform stretch) return;
        if (pressed) stretch.CenterX = toggle.IsChecked == true ? SwitchKnob : 0;
        Soft.Glide(stretch, ScaleTransform.ScaleXProperty, pressed ? SwitchStretch : 1, Soft.Enabled, Soft.Quick);
    }

    // Shown the first time (or again), the knob is where the switch is, without moving; only later changes slide.
    private static void Placed(object sender, RoutedEventArgs e) => Slide((ToggleButton)sender, animate: false);
    private static void Toggled(object sender, RoutedEventArgs e) => Slide((ToggleButton)sender, animate: ((ToggleButton)sender).IsLoaded);

    private static void Slide(ToggleButton toggle, bool animate)
    {
        toggle.ApplyTemplate();
        if (toggle.Template?.FindName("KnobShift", toggle) is not TranslateTransform shift) return;
        Soft.Glide(shift, TranslateTransform.XProperty, toggle.IsChecked == true ? SwitchTravel : 0, animate && Soft.Enabled, Soft.Quick);
    }
}
