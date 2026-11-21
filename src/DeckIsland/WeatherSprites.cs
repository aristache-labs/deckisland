using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace BrimDeck;

public partial class MainWindow
{
    private static FrameworkElement? ColourfulWeatherIcon(string icon, bool animate)
    {
        if (icon == RainIcon) return ColourfulRainIcon(48, animate);
        // Sprite sheets sampled from the supplied SVG animations, 72 px per frame and 12 frames per row.
        var (name, frames, milliseconds) = icon switch
        {
            "☀" => ("sunny", 20, 1000),
            "☾" => ("clear-night", 120, 6000),
            "⛅" => ("partly-cloudy-day", 20, 1000),
            PartlyCloudyNight => ("partly-cloudy-night", 60, 3000),
            "☁" => ("cloudy", 40, 2000),
            "≋" => ("fog", 57, 2833),
            "❄" => ("snow", 20, 1000),
            "ϟ" => ("thunderstorm", 60, 3000),
            _ => ("", 0, 0)
        };
        if (frames == 0) return null;
        var sheet = new BitmapImage(new Uri($"pack://application:,,,/BrimDeck;component/Assets/Weather/{name}.png"));
        CroppedBitmap Frame(int index)
        {
            const int cell = 72;
            var part = new CroppedBitmap(sheet, new Int32Rect(index % 12 * cell, index / 12 * cell, cell, cell));
            part.Freeze();
            return part;
        }
        var image = new Image { Width = 48, Height = 48, Source = Frame(frames / 4),
            Stretch = Stretch.Uniform, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center };
        if (!animate) return image;
        var cycle = new ObjectAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(milliseconds), RepeatBehavior = RepeatBehavior.Forever
        };
        for (int index = 0; index < frames; index++)
            cycle.KeyFrames.Add(new DiscreteObjectKeyFrame(Frame(index), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(index * milliseconds / (double)frames))));
        cycle.Freeze();
        image.IsVisibleChanged += (_, _) => image.BeginAnimation(Image.SourceProperty, image.IsVisible ? cycle : null);
        return image;
    }
}
