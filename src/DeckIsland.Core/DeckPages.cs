namespace DeckIsland.Core;

// The pages of the open panel, in one place: their order in the page switch, whether each is shown, the size each asks for,
// and which page the panel falls back to. The panel and the settings window ask here instead of comparing pages themselves.
public static class DeckPages
{
    // The page switch, left to right, until the order is changed in the settings (DeckSettings.PageOrder). The clipboard comes
    // before the devices, as their groups do in the settings' list.
    public static readonly IReadOnlyList<DeckPage> Order = [DeckPage.Usage, DeckPage.Music, DeckPage.Monitor, DeckPage.Clipboard, DeckPage.Devices, DeckPage.Weather, DeckPage.Focus, DeckPage.Reminders, DeckPage.Widgets, DeckPage.Notifications, DeckPage.Calls, DeckPage.Scratchpad, DeckPage.Converter, DeckPage.Terminal, DeckPage.Colour, DeckPage.Capture];
    // Each known page once, in the saved order; a page missing from the file takes its default place after the others.
    public static List<DeckPage> Normalize(IEnumerable<DeckPage>? order) => [.. (order ?? []).Where(page => Enum.IsDefined(page)).Concat(Order).Distinct()];

    public static bool Shown(DeckSettings settings, DeckPage page) => page switch
    {
        DeckPage.Usage => settings.UsagePage, DeckPage.Music => settings.MusicPage, DeckPage.Monitor => settings.MonitorPage, DeckPage.Devices => settings.DevicesPage,
        DeckPage.Clipboard => settings.ClipboardPage, DeckPage.Weather => settings.WeatherEnabled && settings.WeatherPage, DeckPage.Focus => settings.FocusPage, DeckPage.Reminders => settings.RemindersPage, DeckPage.Widgets => settings.WidgetsPage, DeckPage.Notifications => settings.NotificationsPage, DeckPage.Calls => settings.CallsPage,
        DeckPage.Scratchpad => settings.ScratchpadPage, DeckPage.Converter => settings.ConverterPage, DeckPage.Terminal => settings.TerminalPage, DeckPage.Colour => settings.ColourTool, DeckPage.Capture => settings.CapturePage, _ => false
    };

    public static IReadOnlyList<DeckPage> Visible(DeckSettings settings) => settings.PageOrder.Where(page => Shown(settings, page)).ToList();

    // The page shown when wanted is asked for: wanted while it is shown, otherwise the first shown page in switch order.
    // With every page switched off it is the usage page, which then says that it is off.
    public static DeckPage Resolve(DeckSettings settings, DeckPage wanted)
        => Shown(settings, wanted) ? wanted : Visible(settings) is [var first, ..] ? first : DeckPage.Usage;

    // The page the panel opens on; null keeps the last viewed page.
    public static DeckPage? Opening(DefaultDeckPage choice) => choice switch
    {
        DefaultDeckPage.Usage => DeckPage.Usage, DefaultDeckPage.Music => DeckPage.Music, DefaultDeckPage.Monitor => DeckPage.Monitor, DefaultDeckPage.Devices => DeckPage.Devices,
        DefaultDeckPage.Clipboard => DeckPage.Clipboard, DefaultDeckPage.Weather => DeckPage.Weather, DefaultDeckPage.Focus => DeckPage.Focus, DefaultDeckPage.Reminders => DeckPage.Reminders, DefaultDeckPage.Widgets => DeckPage.Widgets, DefaultDeckPage.Notifications => DeckPage.Notifications, DefaultDeckPage.Calls => DeckPage.Calls,
        DefaultDeckPage.Scratchpad => DeckPage.Scratchpad, DefaultDeckPage.Converter => DeckPage.Converter, DefaultDeckPage.Terminal => DeckPage.Terminal, DefaultDeckPage.Colour => DeckPage.Colour, DefaultDeckPage.Capture => DeckPage.Capture, _ => null
    };

    // Every page's size lies within these; the usage page is also at least as wide as its columns need (MinimumWidth).
    // Sizes are before the scale (PanelScales): drawn at 150 %, a page of the largest size is 1800 x 600 DIP.
    public const double MinWidth = 440, MaxWidth = 1200, MinHeight = 140, MaxHeight = 400;
    // The widest a page can be on a monitor with this work area: drawn at the scale, the work area's width less 12 DIP each side.
    public static double MaxWidthOn(double workWidth, double scale = 1) => Math.Max(MinWidth, Math.Min(MaxWidth, Math.Floor((workWidth - 24) / scale)));

    public static (double MinWidth, double MaxWidth, double MinHeight, double MaxHeight) Limits(DeckSettings settings, DeckPage page)
        => (Math.Max(page == DeckPage.Usage ? Math.Max(MinWidth, settings.MinimumWidth) : MinWidth, Math.Min(MaxWidth, SwitchWidth(settings, page))), MaxWidth, page == DeckPage.Widgets ? 220 : MinHeight, MaxHeight);

    // Sets a page's size, as dragging an edge of the open panel does; the values are kept within the page's limits.
    public static void SetSize(DeckSettings settings, DeckPage page, double width, double height)
    {
        var (minWidth, maxWidth, minHeight, maxHeight) = Limits(settings, page);
        width = Math.Clamp(width, minWidth, Math.Max(minWidth, maxWidth)); height = Math.Clamp(height, minHeight, maxHeight);
        switch (page)
        {
            case DeckPage.Widgets: settings.WidgetsWidth = width; settings.WidgetsHeight = height; break;
            case DeckPage.Notifications: settings.NotificationsWidth = width; settings.NotificationsHeight = height; break;
            case DeckPage.Calls: settings.CallsWidth = width; settings.CallsHeight = height; break;
            case DeckPage.Scratchpad: settings.ScratchpadWidth = width; settings.ScratchpadHeight = height; break;
            case DeckPage.Converter: settings.ConverterWidth = width; settings.ConverterHeight = height; break;
            case DeckPage.Terminal: settings.TerminalWidth = width; settings.TerminalHeight = height; break;
            case DeckPage.Colour: settings.ColourWidth = width; settings.ColourHeight = height; break;
            case DeckPage.Capture: settings.CaptureWidth = width; settings.CaptureHeight = height; break;
            case DeckPage.Music: settings.MusicWidth = width; settings.MusicHeight = height; break;
            case DeckPage.Monitor: settings.MonitorWidth = width; settings.MonitorHeight = height; break;
            case DeckPage.Reminders: settings.RemindersWidth = width; settings.RemindersHeight = height; break;
            case DeckPage.Focus: settings.FocusWidth = width; settings.FocusHeight = height; break;
            case DeckPage.Weather: settings.WeatherWidth = width; settings.WeatherHeight = height; break;
            case DeckPage.Devices: settings.DevicesWidth = width; settings.DevicesHeight = height; break;
            case DeckPage.Clipboard: settings.ClipboardWidth = width; settings.ClipboardHeight = height; break;
            default: settings.Width = width; settings.Height = height; break;
        }
    }

    // A page is never narrower than its limits allow (Limits), so a size saved before the page switch grew still holds it.
    public static (double Width, double Height) Size(DeckSettings settings, DeckPage page)
    {
        var (width, height) = Saved(settings, page);
        return (Math.Max(width, Limits(settings, page).MinWidth), height);
    }

    private static (double Width, double Height) Saved(DeckSettings settings, DeckPage page) => page switch
    {
        DeckPage.Widgets => (settings.WidgetsWidth, settings.WidgetsHeight),
        DeckPage.Notifications => (settings.NotificationsWidth, settings.NotificationsHeight),
        DeckPage.Calls => (settings.CallsWidth, settings.CallsHeight),
        DeckPage.Scratchpad => (settings.ScratchpadWidth, settings.ScratchpadHeight),
        DeckPage.Converter => (settings.ConverterWidth, settings.ConverterHeight),
        DeckPage.Terminal => (settings.TerminalWidth, settings.TerminalHeight),
        DeckPage.Colour => (settings.ColourWidth, settings.ColourHeight),
        DeckPage.Capture => (settings.CaptureWidth, settings.CaptureHeight),
        DeckPage.Music => (settings.MusicWidth, settings.MusicHeight),
        DeckPage.Monitor => (settings.MonitorWidth, settings.MonitorHeight),
        DeckPage.Reminders => (settings.RemindersWidth, settings.RemindersHeight),
        DeckPage.Focus => (settings.FocusWidth, settings.FocusHeight),
        DeckPage.Weather => (settings.WeatherWidth, settings.WeatherHeight),
        DeckPage.Devices => (settings.DevicesWidth, settings.DevicesHeight),
        DeckPage.Clipboard => (settings.ClipboardWidth, settings.ClipboardHeight),
        _ => (Math.Max(settings.Width, settings.MinimumWidth), settings.Height)
    };

    // The quick sizes, as shares of the work area of the panel's monitor so they scale with its resolution and Windows scale.
    // The shares were taken on the reference work area: there Compact is the page's default size, Standard the old spacious
    // size, and Spacious about 800 DIP wide and the full height the page allows. Each is kept within the page's limits.
    // The reference is a 1920 x 1200 monitor at the Windows 125 % scale, less the taskbar, in DIP.
    public const double ReferenceWorkWidth = 1536, ReferenceWorkHeight = 922, SpaciousWidth = 800;
    // The header's width on a page: the left inset, the page switch's buttons (28 DIP and a 1 DIP gap each), the gap before
    // the right group, the pin (the tool pages only) and the gear, and the gear's margin, at the tab bar's size. No page is
    // narrower, so the switch is never shrunk. shortcut: mirrors MainWindow.xaml's button sizes; update it with them.
    public static double SwitchWidth(DeckSettings settings, DeckPage page)
    {
        double factor = TabBarScales.Factor(settings);
        bool pin = page is DeckPage.Scratchpad or DeckPage.Converter or DeckPage.Terminal or DeckPage.Colour or DeckPage.Capture;
        return Math.Ceiling(PanelHeader.CornerClear + (Math.Max(1, Visible(settings).Count) * 29 + 4) * factor + 10
            + (PanelHeader.SettingsWidth + (pin ? 30 : 0)) * factor + PanelHeader.RightButtonsMargin);
    }
    public static (double Width, double Height) QuickSize(DeckSettings settings, DeckPage page, PanelSize size, double workWidth, double workHeight, double scale = 1)
    {
        var (compact, standard) = Reference(settings, page);
        var (width, height) = size switch
        {
            PanelSize.Compact => compact,
            PanelSize.Standard => standard,
            _ => (Math.Max(SpaciousWidth, standard.Width), ReferenceWorkHeight)
        };
        var (minWidth, maxWidth, minHeight, maxHeight) = Limits(settings, page);
        maxWidth = Math.Max(minWidth, Math.Min(maxWidth, MaxWidthOn(workWidth, scale)));
        return (Math.Clamp(Math.Round(width / ReferenceWorkWidth * workWidth / scale), minWidth, maxWidth),
            Math.Clamp(Math.Round(height / ReferenceWorkHeight * workHeight / scale), minHeight, maxHeight));
    }

    // Compact and Standard on the reference work area: the page's default size and the larger size the page had before.
    private static ((double Width, double Height) Compact, (double Width, double Height) Standard) Reference(DeckSettings settings, DeckPage page) => page switch
    {
        DeckPage.Usage => (PanelSizes.For(PanelSize.Standard, settings.ConfiguredApps.Count), PanelSizes.For(PanelSize.Spacious, settings.ConfiguredApps.Count)),
        DeckPage.Music => (MusicSizes.For(PanelSize.Standard), MusicSizes.For(PanelSize.Spacious)),
        _ => (Size(new DeckSettings(), page), page switch
        {
            DeckPage.Monitor or DeckPage.Clipboard => (860, 320),
            DeckPage.Widgets => (700, 360),
            DeckPage.Notifications or DeckPage.Reminders => (620, 380),
            DeckPage.Calls => (560, 260),
            DeckPage.Scratchpad or DeckPage.Terminal or DeckPage.Colour or DeckPage.Capture => (760, 380),
            DeckPage.Converter or DeckPage.Weather => (620, page == DeckPage.Weather ? 320 : 240),
            DeckPage.Focus => (560, 240),
            _ => (620, 280)
        })
    };

    // The largest width and height among the shown pages (the usage page when none is): the window holds any of them, so
    // switching pages never resizes the window itself.
    public static (double Width, double Height) Largest(DeckSettings settings)
    {
        var pages = Visible(settings) is { Count: > 0 } visible ? visible : [DeckPage.Usage];
        return (pages.Max(page => Size(settings, page).Width), pages.Max(page => Size(settings, page).Height));
    }
}

// The scale DeckIsland is drawn at (DeckSettings.PanelScale), in percent: one of Choices; any other value is the default.
public static class PanelScales
{
    public const int Default = 100;
    public static readonly IReadOnlyList<int> Choices = [100, 150];
    public static int Fit(int percent) => Choices.Contains(percent) ? percent : Default;
    public static double Factor(DeckSettings settings) => Fit(settings.PanelScale) / 100d;
}
