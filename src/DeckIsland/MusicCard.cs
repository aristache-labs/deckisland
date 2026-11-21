using System.Windows;
using System.Windows.Automation;
using DeckIsland.Core;

namespace DeckIsland;

// The expanded music card's background and its palette button: the cover's colours in soft streaks behind the page, or the island's plain
// surface. The button in the card's corner switches between them and keeps the choice (Settings › Music › Music card says the same).
public partial class MainWindow
{
    private string _backdropSong = "";

    private void InitializeMusicCard()
    {
        MusicContent.IsVisibleChanged += (_, _) => UpdateMusicBackdrop();
        AutomationProperties.SetName(MusicPaletteButton, Loc.T("切换卡片背景", "Switch the card's background"));
    }

    private void UpdateMusicBackdrop()
    {
        bool onPage = MusicContent.IsVisible && _mediaTrack is not null && _expanded;
        MusicPaletteButton.Visibility = onPage ? Visibility.Visible : Visibility.Collapsed;
        bool streaks = onPage && Settings.MusicBackground == MusicCardBackground.AlbumColours;
        if (streaks && _mediaTrack is { } track)
        {
            string key = track.SongKey + "|" + (_mediaCover is null ? "none" : _mediaCover.GetHashCode().ToString());
            if (key != _backdropSong) { _backdropSong = key; MusicBackdropView.SetColors(MusicBackdrop.Palette(_mediaCover)); }
        }
        MusicBackdropView.Visibility = streaks ? Visibility.Visible : Visibility.Collapsed;
        MusicPaletteButton.ToolTip = Settings.MusicBackground == MusicCardBackground.AlbumColours
            ? Loc.T("背景：专辑配色（点击切换为纯色）", "Background: album colours (click for plain)")
            : Loc.T("背景：纯色（点击切换为专辑配色）", "Background: plain (click for album colours)");
    }

    private void MusicPalette_Click(object sender, RoutedEventArgs e)
    {
        var edited = Settings.Copy();
        edited.MusicBackground = Settings.MusicBackground == MusicCardBackground.AlbumColours ? MusicCardBackground.Plain : MusicCardBackground.AlbumColours;
        _app.UpdateSettingsFor(Profile, edited);
        UpdateMusicBackdrop();
    }
}
