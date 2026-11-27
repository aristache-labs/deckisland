using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DeckIsland.Core;

namespace DeckIsland;

// DeckIsland Dock's page. The switch is an island setting (DeckSettings.DockEnabled); everything else on the page is the dock's
// own, kept in dock.json (App.DockSettings, App.UpdateDockSettings), never in DeckSettings. Every setting has an info tip.
public sealed partial class SettingsWindow
{
    internal const int DockSettingsPage = 29;

    private void DockSettings()
    {
        var dock = _app.DockSettings;
        SectionTitle("DeckIsland Dock", description: Loc.T("代替 Windows 任务栏的程序坞，带应用和小组件。", "A dock with apps and widgets that replaces the Windows taskbar."));
        Group(Describe(Toggle(Loc.T("启用 Dock", "Turn on the dock"), S.DockEnabled, value => Change(s => s.DockEnabled = value),
                help: Loc.T("随时按 Ctrl+Alt+Shift+Q 关闭 Dock 并恢复任务栏。", "Press Ctrl+Alt+Shift+Q at any time to turn the dock off and get the taskbar back.")),
            Loc.T("关闭后 Windows 任务栏恢复原样。", "Off, the Windows taskbar is back as it was.")));

        SectionTitle(Loc.T("外观", "Appearance"), description: Loc.T("Dock 的样式和强调色。玻璃样式会透出 Dock 后面的内容。", "The dock's style and accent colour. The glass styles show what is behind the dock."));
        var appearance = new List<FrameworkElement>
        {
            Describe(Row(Loc.T("Dock 样式", "Dock style"), StyleTiles(dock),
                help: () => Loc.T("黑色、白色：不透明。Liquid、iOS、Midnight：透出后面内容的玻璃，Midnight 色调最深。透明：只留下小组件。\n玻璃样式开启时，Dock 不会出现在截图和录屏中。",
                    "Black and White are solid. Liquid, iOS and Midnight are glass that shows what is behind, Midnight under the deepest tint. Clear leaves only the widgets.\nWhile a glass style is on, the dock is left out of screenshots and recordings.")),
                Loc.T("六种样式。", "Six styles."))
        };
        if (dock.Style == DockStyle.Clear)
            appearance.Add(Describe(Row(Loc.T("透明样式的小组件", "Widgets on Clear"), ClearTiles(dock),
                help: () => Loc.T("透明样式下小组件所在的卡片。", "The cards the widgets sit on in the Clear style.")), Loc.T("黑色、磨砂或白色卡片。", "Black, frosted or white cards.")));
        appearance.Add(Describe(Row(Loc.T("强调色", "Accent"), AccentDots(dock),
            help: () => Loc.T("已打开应用的标记和高亮使用它。", "The open marker and highlights wear it.")), Loc.T("Dock 的强调色。", "The dock's accent colour.")));
        Group([.. appearance]);

        SectionTitle(Loc.T("小组件与配置", "Widgets and profiles"), description: Loc.T("Dock 上的实时小组件。每个配置有自己的一组小组件，可从 Dock 的右键菜单切换。",
            "Live widgets on the dock. Each profile has its own set, and you switch between them from the dock's right-click menu."));
        var pick = ActionButton(Loc.T("打开小组件…", "Open widgets…"), _app.OpenDockPicker);
        pick.IsEnabled = S.DockEnabled;
        var current = dock.Profiles.First(p => p.Id == dock.CurrentProfile);
        var profiles = new ComboBox { Width = 180, Style = (Style)FindResource("SettingsCombo"), ItemsSource = dock.Profiles.Select(p => p.Name).ToList(),
            SelectedIndex = dock.Profiles.IndexOf(current) };
        AutomationProperties.SetName(profiles, Loc.T("配置", "Profile")); PopupMotion.Attach(profiles);
        profiles.SelectionChanged += (_, _) =>
        {
            if (profiles.SelectedIndex < 0 || dock.Profiles[profiles.SelectedIndex].Id == dock.CurrentProfile) return;
            var id = dock.Profiles[profiles.SelectedIndex].Id; ChangeDock(d => d.CurrentProfile = id); ShowPage(DockSettingsPage);
        };
        var name = new TextBox { Width = 180, Style = (Style)FindResource("SettingsField"), Text = current.Name, MaxLength = DockWidgets.MaxProfileName };
        AutomationProperties.SetName(name, Loc.T("配置名称", "Profile name"));
        name.LostFocus += (_, _) =>
        {
            var text = name.Text.Trim();
            if (text.Length == 0 || text == current.Name) return;
            ChangeDock(d => d.Profiles = d.Profiles.Select(p => p.Id == current.Id ? p with { Name = text } : p).ToList()); ShowPage(DockSettingsPage);
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var add = ActionButton(Loc.T("新建", "New"), () =>
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            ChangeDock(d => { d.Profiles = [.. d.Profiles, new DockProfile(id, Loc.T("配置 ", "Profile ") + (d.Profiles.Count + 1), [])]; d.CurrentProfile = id; });
            ShowPage(DockSettingsPage);
        });
        add.IsEnabled = dock.Profiles.Count < DockWidgets.MaxProfiles;
        var delete = ActionButton(Loc.T("删除", "Delete"), () =>
        {
            if (!Confirm(Loc.T("删除配置", "Delete profile"), Loc.T($"删除“{current.Name}”及其小组件？", $"Delete “{current.Name}” and its widgets?"), Loc.T("删除", "Delete"))) return;
            ChangeDock(d => d.Profiles = d.Profiles.Where(p => p.Id != current.Id).ToList()); ShowPage(DockSettingsPage);
        });
        delete.IsEnabled = dock.Profiles.Count > 1; delete.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(add); buttons.Children.Add(delete);
        Group(
            Describe(Row(Loc.T("小组件", "Widgets"), pick, help: () => Loc.T("按类别浏览或搜索，点击添加或移除。小组件选择器打开时，可点击 Dock 上小组件的减号移除它，或拖动调整顺序。",
                "Browse by category or search, and click a tile to add or remove it. While the picker is open, click a widget's minus badge on the dock to remove it, or drag it to reorder.")),
                Loc.T($"当前配置中有 {dock.Widgets.Count} 个小组件。", $"{dock.Widgets.Count} widgets in this profile.")),
            Describe(Row(Loc.T("当前配置", "Profile"), profiles, help: () => Loc.T("每个配置有自己的一组小组件，应用保持不变。", "Each profile has its own set of widgets; the apps stay the same.")),
                Loc.T("Dock 正在使用的配置。", "The profile the dock uses.")),
            Describe(Row(Loc.T("名称", "Name"), name), Loc.T("当前配置的名称。", "The current profile's name.")),
            Describe(Row(Loc.T("配置", "Profiles"), buttons), Loc.T("新建一个空配置，或删除当前配置。", "Make a new, empty profile, or delete the current one.")));

        SectionTitle(Loc.T("布局", "Layout"), description: Loc.T("Dock 在屏幕上的位置、大小和形状。也可以按住 Dock 的空白处拖到屏幕另一半。",
            "Where the dock sits, how big it is and its shape. You can also drag the dock by an empty spot to the other half of the screen."));
        Group(
            Describe(Row(Loc.T("位置", "Position"), SegmentBorder(DockChoices("DockEdge",
                [(Loc.T("底部", "Bottom"), DockEdge.Bottom), (Loc.T("顶部", "Top"), DockEdge.Top)], dock.Edge, value => ChangeDock(d => d.Edge = value))),
                help: () => Loc.T("Dock 所在的屏幕边缘。菜单和预览朝屏幕中间打开。", "The screen edge the dock sits on. Its menus and previews open towards the middle of the screen.")),
                Loc.T("屏幕的底部或顶部边缘。", "The bottom or top edge of the screen.")),
            Describe(Row(Loc.T("显示器", "Displays"), SegmentBorder(DockChoices("DockDisplays",
                [(Loc.T("主显示器", "Main"), DockDisplays.Main), (Loc.T("全部", "All"), DockDisplays.All)], dock.Displays, value => ChangeDock(d => d.Displays = value))),
                help: () => Loc.T("全部：每个显示器都有自己的 Dock，并在其上方保留空间。", "All: each screen has its own dock, with its own space kept above that screen's windows.")),
                Loc.T("只在主显示器上，或每个显示器上一个。", "On the main screen only, or one on each screen.")),
            Describe(InlineSlider(Loc.T("大小", "Size"), dock.Size, Core.DockSettings.MinSize, Core.DockSettings.MaxSize, 1, "px", value => ChangeDock(d => d.Size = (int)value),
                help: Loc.T("从比 Windows 任务栏更小，到适合 1440p 屏幕。小、中、大分别约为 46、52 和 62。", "From smaller than the Windows taskbar up to big enough for a 1440p screen. Small, medium and large are about 46, 52 and 62.")),
                Loc.T("Dock 栏的高度。", "The height of the bar.")),
            Describe(InlineSlider(Loc.T("离边缘距离", "Distance from edge"), dock.Distance, 0, Core.DockSettings.MaxDistance, 1, "px", value => ChangeDock(d => d.Distance = (int)value),
                help: Loc.T("把 Dock 从屏幕边缘抬起；窗口最大化时停在它上方。", "Raises the dock off the screen edge; maximized windows still stop above it.")),
                Loc.T("Dock 与屏幕边缘之间的空隙。", "The gap between the dock and the screen edge.")),
            Describe(InlineSlider(Loc.T("间距", "Spacing"), dock.Spacing, 0, Core.DockSettings.MaxSpacing, 1, "px", value => ChangeDock(d => d.Spacing = (int)value),
                help: Loc.T("图标之间的空隙。", "The space between icons.")), Loc.T("图标之间的距离。", "How far apart the icons are.")),
            Describe(InlineSlider(Loc.T("内边距", "Padding"), dock.Padding, 0, Core.DockSettings.MaxPadding, 1, "px", value => ChangeDock(d => d.Padding = (int)value),
                help: Loc.T("Dock 两端与第一个和最后一个图标之间的空隙。", "The space between the dock's ends and its first and last icons.")), Loc.T("Dock 两端的空隙。", "The space at the dock's ends.")),
            Describe(InlineSlider(Loc.T("圆角", "Corner radius"), dock.Corner, 0, Core.DockSettings.MaxCorner, 5, "%", value => ChangeDock(d => d.Corner = (int)value),
                help: Loc.T("0% 为方角，100% 为胶囊形。图标的卡片跟随它。", "0% is square, 100% a pill. The icons' cards follow it.")), Loc.T("方角或圆角。", "Square or round.")),
            Describe(Toggle(Loc.T("全宽", "Full width"), dock.FullWidth, value => ChangeDock(d => d.FullWidth = value),
                help: Loc.T("像 Windows 任务栏一样横跨整个屏幕边缘，两端留出相同的空隙。", "Spans the whole screen edge like the Windows taskbar, with the same gap at both ends.")),
                Loc.T("横跨整个屏幕边缘，而不是按图标的宽度。", "Across the whole screen edge rather than as wide as its icons.")));

        SectionTitle(Loc.T("行为", "Behavior"), description: Loc.T("Dock 何时显示，以及它的图标如何响应。", "When the dock shows and how its icons respond."));
        var behavior = new List<FrameworkElement>
        {
            Describe(Toggle(Loc.T("自动隐藏", "Auto-hide"), dock.AutoHide, value => { ChangeDock(d => d.AutoHide = value); ShowPage(DockSettingsPage); },
                help: Loc.T("工作时 Dock 滑走，鼠标碰到屏幕边缘时回来。它不保留屏幕空间。", "The dock slides away while you work and comes back when the pointer touches the screen edge. It keeps no screen space.")),
                Loc.T("不用时滑出屏幕。", "Slides off the screen when not in use."))
        };
        if (dock.AutoHide)
            behavior.Add(Describe(Toggle(Loc.T("在桌面上显示", "Show on desktop"), dock.ShowOnDesktop, value => ChangeDock(d => d.ShowOnDesktop = value),
                help: Loc.T("前面只有桌面时 Dock 保持显示。", "Keeps the dock up while nothing but the desktop is in front.")), Loc.T("桌面空着时保持显示。", "Stays up on an empty desktop.")));
        behavior.Add(Describe(Toggle(Loc.T("锁定 Dock", "Lock dock"), dock.Locked, value => ChangeDock(d => d.Locked = value),
            help: Loc.T("锁定后图标不能拖动、拖出或放入，也不能拖动 Dock 换边。", "Locked, icons cannot be dragged, dragged off or dropped on, and the dock cannot be dragged to the other edge.")),
            Loc.T("防止意外移动图标。", "Keeps icons from moving by accident.")));
        behavior.Add(Describe(Toggle(Loc.T("合并窗口", "Group windows"), dock.GroupWindows, value => ChangeDock(d => d.GroupWindows = value),
            help: Loc.T("关闭后，应用的每个窗口都有自己的图标。", "Off, every window of an app gets its own icon.")), Loc.T("一个应用的窗口共用一个图标。", "An app's windows share one icon.")));
        behavior.Add(Describe(Toggle(Loc.T("放大", "Magnification"), dock.Magnification, value => { ChangeDock(d => d.Magnification = value); ShowPage(DockSettingsPage); },
            help: Loc.T("鼠标经过时图标像 Mac 程序坞一样变大。", "Icons grow as the pointer passes over them, as on a Mac dock.")), Loc.T("鼠标下的图标变大。", "Icons under the pointer grow.")));
        if (dock.Magnification)
        {
            behavior.Add(Describe(InlineSlider(Loc.T("放大大小", "Magnification size"), dock.MagnificationSize, Core.DockSettings.MinMagnification, Core.DockSettings.MaxMagnification, 5, "%",
                value => ChangeDock(d => d.MagnificationSize = (int)value), help: Loc.T("鼠标正下方的图标变大多少。", "How much larger the icon right under the pointer grows.")),
                Loc.T("最大的放大程度。", "How large icons grow at most.")));
            behavior.Add(Describe(Row(Loc.T("放大速度", "Magnification speed"), SegmentBorder(DockChoices("DockSpeed",
                [(Loc.T("慢", "Slow"), DockSpeed.Slow), (Loc.T("正常", "Normal"), DockSpeed.Normal), (Loc.T("快", "Fast"), DockSpeed.Fast)], dock.MagnificationSpeed,
                value => ChangeDock(d => d.MagnificationSpeed = value))), help: () => Loc.T("图标跟随鼠标变大和缩回的速度。", "How quickly icons grow and shrink back as the pointer moves.")),
                Loc.T("图标变化的快慢。", "How fast icons change.")));
        }
        Group([.. behavior]);

        SectionTitle(Loc.T("应用", "Apps"), description: Loc.T("Dock 上的按钮和应用。拖动图标可重新排序，拖出 Dock 可取消固定，把文件拖到 Dock 上可固定。",
            "The dock's buttons and apps. Drag an icon to reorder it, drag it off the dock to unpin it, drop a file on the dock to pin it."));
        Group(
            Describe(Row(Loc.T("应用图标", "App icons"), SegmentBorder(DockChoices("DockIconSize",
                [(Loc.T("小", "Small"), DockIconSize.Small), (Loc.T("中", "Medium"), DockIconSize.Medium), (Loc.T("大", "Large"), DockIconSize.Large)], dock.IconSize,
                value => ChangeDock(d => d.IconSize = value))), help: () => Loc.T("图标在其位置中所占的大小。", "How much of its place each icon fills.")),
                Loc.T("图标的大小。", "The size of the icons.")),
            Describe(Toggle(Loc.T("应用卡片", "App cards"), dock.AppCards, value => ChangeDock(d => d.AppCards = value),
                help: Loc.T("每个应用都放在与 Dock 同色的卡片上，而不是只显示图标。", "Each app sits on a card in the dock's shade, rather than as a bare icon.")), Loc.T("图标放在卡片上。", "Icons on cards.")),
            Describe(Row(Loc.T("打开指示", "Open indicator"), SegmentBorder(DockChoices("DockIndicator",
                [(Loc.T("圆点", "Dot"), DockIndicator.Dot), (Loc.T("大圆点", "Large dot"), DockIndicator.LargeDot), (Loc.T("线条", "Line"), DockIndicator.Line)], dock.Indicator,
                value => ChangeDock(d => d.Indicator = value))), help: () => Loc.T("已打开的应用下方的标记；前台应用的标记更宽。", "The mark under open apps; the app in front has a wider one.")),
                Loc.T("标记已打开的应用。", "Marks open apps.")),
            Describe(Toggle(Loc.T("悬停显示名称", "Names on hover"), dock.NamesOnHover, value => ChangeDock(d => d.NamesOnHover = value),
                help: Loc.T("鼠标停在图标上时显示应用名称。", "Shows an app's name while the pointer rests on its icon.")), Loc.T("显示应用名称。", "Shows app names.")),
            Describe(Toggle(Loc.T("窗口预览", "Window previews"), dock.WindowPreviews, value => ChangeDock(d => d.WindowPreviews = value),
                help: Loc.T("鼠标停在已打开的应用上时，显示其窗口的实时画面、标题和关闭按钮。", "While the pointer rests on an open app, live pictures of its windows with their titles and a close button.")),
                Loc.T("显示窗口的实时画面。", "Live pictures of windows.")),
            Describe(Toggle(Loc.T("开始按钮", "Start button"), dock.ShowStart, value => ChangeDock(d => d.ShowStart = value),
                help: Loc.T("在最左侧打开开始菜单，与 Windows 键相同。", "Opens Start, at the left end, as the Windows key does.")), Loc.T("在最左侧。", "At the left end.")),
            Describe(Toggle(Loc.T("回收站", "Recycle Bin"), dock.ShowTrash, value => ChangeDock(d => d.ShowTrash = value),
                help: Loc.T("拖到上面的文件会移到回收站，可以恢复。", "Files dropped on it go to the Recycle Bin, where they can be restored.")), Loc.T("可以把文件拖到上面。", "Files can be dropped on it.")),
            Describe(Toggle(Loc.T("显示桌面按钮", "Show desktop button"), dock.ShowDesktopButton, value => ChangeDock(d => d.ShowDesktopButton = value),
                help: Loc.T("最小化所有窗口，再点一次恢复。", "Minimizes every window; click again to bring them back.")), Loc.T("在最右侧。", "At the right end.")),
            Describe(Toggle(Loc.T("Win+1 到 Win+0", "Win+1 to Win+0"), dock.AppShortcuts, value => ChangeDock(d => d.AppShortcuts = value),
                help: Loc.T("打开 Dock 上的前十个应用，与任务栏相同。", "Open the dock's first ten apps, as on the taskbar.")), Loc.T("用键盘打开应用。", "Open apps from the keyboard.")));

        SectionTitle(Loc.T("时钟", "Clock"), description: Loc.T("Dock 右端的时钟。", "The clock at the dock's right end."));
        Group(
            Describe(Row(Loc.T("时间格式", "Time format"), SegmentBorder(DockChoices("DockClock",
                [(Loc.T("系统", "System"), (bool?)null), (Loc.T("12 小时", "12-hour"), false), (Loc.T("24 小时", "24-hour"), true)], dock.Clock24Hour,
                value => ChangeDock(d => d.Clock24Hour = value))), help: () => Loc.T("系统：跟随 Windows 的时间格式。", "System: follows Windows' time format.")),
                Loc.T("12 或 24 小时制。", "12 or 24 hours.")),
            Describe(Toggle(Loc.T("显示秒", "Show seconds"), dock.ClockSeconds, value => ChangeDock(d => d.ClockSeconds = value),
                help: Loc.T("时钟显示秒。", "The clock shows seconds.")), Loc.T("在时间中显示秒。", "Seconds in the time.")));

        SectionTitle(Loc.T("Windows 任务栏", "Windows taskbar"), description: Loc.T("Dock 运行时如何处理 Windows 任务栏。", "What happens to the Windows taskbar while the dock runs."));
        var taskbar = Row(Loc.T("Dock 运行时", "While the dock runs"), SegmentBorder(DockChoices("DockTaskbar",
            [(Loc.T("自动隐藏", "Auto-hide"), DockTaskbarMode.AutoHide), (Loc.T("完全隐藏", "Hide"), DockTaskbarMode.Hide), (Loc.T("不隐藏", "Leave"), DockTaskbarMode.Leave)],
            // Built again so the line under it says whether Windows kept the taskbar hidden.
            dock.TaskbarMode, value => { ChangeDock(d => d.TaskbarMode = value); ShowPage(DockSettingsPage); })),
            help: () => Loc.T("自动隐藏：鼠标移到屏幕边缘、打开开始菜单或应用闪烁提醒时任务栏仍会出现。\n完全隐藏：任务栏不再出现；如果 Windows 不允许，则改用自动隐藏。\n不隐藏：任务栏保持原样。",
                "Auto-hide: the taskbar still slides in at the screen edge, for Start, and while an app flashes for attention.\nHide: the taskbar stays away; where Windows will not allow it, auto-hide is used instead.\nLeave: the taskbar stays as it is."));
        Group(Native.DockTaskbar.CompleteFailed
            ? Describe(taskbar, Loc.T("Windows 不允许完全隐藏任务栏，已改用自动隐藏。", "Windows would not keep the taskbar hidden, so auto-hide is used instead."))
            : Describe(taskbar, Loc.T("关闭 Dock 或退出 DeckIsland 时恢复。", "Restored when the dock is turned off or DeckIsland exits.")));
    }

    private void ChangeDock(Action<DockSettings> edit)
    {
        var copy = _app.DockSettings.Copy(); edit(copy); _app.UpdateDockSettings(copy);
    }

    private UniformGrid DockChoices<T>(string group, (string Name, T Value)[] choices, T current, Action<T> chosen)
    {
        var grid = new UniformGrid { Columns = choices.Length, Width = 80 * choices.Length };
        foreach (var (name, value) in choices)
        {
            var choice = new RadioButton { Style = (Style)FindResource("SettingsSegment"), Content = name, GroupName = group, IsChecked = EqualityComparer<T>.Default.Equals(value, current) };
            AutomationProperties.SetName(choice, name);
            choice.Checked += (_, _) => chosen(value);
            grid.Children.Add(choice);
        }
        return grid;
    }

    // A small picture of each style, as lumabar shows them: the bar in the style's colours with three cards on it; the chosen one
    // ringed in the accent.
    private FrameworkElement StyleTiles(DockSettings dock)
    {
        (string Name, DockStyle Style)[] styles = [(Loc.T("黑色", "Black"), DockStyle.Black), (Loc.T("白色", "White"), DockStyle.White), ("Liquid", DockStyle.Liquid),
            ("iOS", DockStyle.IOS), ("Midnight", DockStyle.Midnight), (Loc.T("透明", "Clear"), DockStyle.Clear)];
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, style) in styles)
        {
            var look = DockLook.For(style, dock.ClearCards);
            panel.Children.Add(PreviewTile(name, look, style switch
            {
                DockStyle.Liquid => "#FF3A4258", DockStyle.IOS => "#FF3D5684", DockStyle.Midnight => "#FF1C2029", DockStyle.Clear => "#FF8E8E93", _ => null
            }, dock.Style == style, dock.Accent, () => { ChangeDock(d => d.Style = style); ShowPage(DockSettingsPage); }));
        }
        return panel;
    }

    private FrameworkElement ClearTiles(DockSettings dock)
    {
        (string Name, DockClearCards Cards)[] cards = [(Loc.T("黑色", "Black"), DockClearCards.Black), (Loc.T("磨砂", "Glass"), DockClearCards.Glass), (Loc.T("白色", "White"), DockClearCards.White)];
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, kind) in cards)
            panel.Children.Add(PreviewTile(name, DockLook.For(DockStyle.Clear, kind), "#FF8A6FD6", dock.ClearCards == kind, dock.Accent,
                () => { ChangeDock(d => d.ClearCards = kind); ShowPage(DockSettingsPage); }));
        return panel;
    }

    // backdrop stands for what shows behind glass or Clear; null for the solid styles.
    private FrameworkElement PreviewTile(string name, DockLook look, string? backdrop, bool chosen, string accent, Action choose)
    {
        var cards = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < 3; i++) cards.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Margin = new Thickness(1.5, 0, 1.5, 0), Background = UI.Brush(look.Card) });
        var bar = new Border { CornerRadius = new CornerRadius(8), Background = UI.Brush(look.Bar), BorderBrush = UI.Brush(look.Rim), BorderThickness = new Thickness(1), Child = cards };
        var face = new Border { Width = 58, Height = 40, CornerRadius = new CornerRadius(9), Child = bar, ClipToBounds = true,
            Background = backdrop is null ? Brushes.Transparent : new LinearGradientBrush((Color)ColorConverter.ConvertFromString(backdrop), (Color)ColorConverter.ConvertFromString("#FF14161C"), 90) };
        var ring = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(2), BorderThickness = new Thickness(2),
            BorderBrush = UI.Brush(chosen ? accent : "#00000000"), Child = face, Cursor = System.Windows.Input.Cursors.Hand };
        var label = TextLine(name, 11, chosen ? TextPrimary : TextSecondary, chosen ? FontWeights.SemiBold : FontWeights.Normal);
        label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 4, 0, 0);
        var tile = new StackPanel { Margin = new Thickness(3, 6, 3, 6) };
        tile.Children.Add(ring); tile.Children.Add(label);
        var button = new Button { Content = tile, Style = (Style)FindResource("SettingsGlyphButton"), Padding = new Thickness(0) };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => choose();
        return button;
    }

    private FrameworkElement AccentDots(DockSettings dock)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var colour in DockLook.Accents)
        {
            bool chosen = string.Equals(colour, dock.Accent, StringComparison.OrdinalIgnoreCase);
            var dot = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = UI.Brush(colour), Margin = new Thickness(3, 0, 3, 0),
                Child = chosen ? new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = Brushes.White } : null, Cursor = System.Windows.Input.Cursors.Hand };
            var button = new Button { Content = dot, Style = (Style)FindResource("SettingsGlyphButton"), Padding = new Thickness(0) };
            AutomationProperties.SetName(button, colour);
            string chosenColour = colour;
            button.Click += (_, _) => { ChangeDock(d => d.Accent = chosenColour); ShowPage(DockSettingsPage); };
            panel.Children.Add(button);
        }
        return panel;
    }
}
