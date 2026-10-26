using System.Windows;
using System.Windows.Media;
using PodPeek.Native;

namespace PodPeek;

/// <summary>Colores al estilo de macOS, en claro u oscuro según Windows.</summary>
static class Theme
{
    public static bool Dark => Dwm.AppsUseDarkTheme;

    public static readonly Color Green = Color.FromRgb(0x34, 0xC7, 0x59);
    public static readonly Color Orange = Color.FromRgb(0xFF, 0x9F, 0x0A);
    public static readonly Color Red = Color.FromRgb(0xFF, 0x3B, 0x30);

    public static void Apply(ResourceDictionary resources)
    {
        bool dark = Dark;
        resources["TextBrush"] = Brush(dark ? "#F5F5F7" : "#1D1D1F");
        resources["SecondaryTextBrush"] = Brush(dark ? "#98989D" : "#6E6E73");
        resources["TrackBrush"] = Brush(dark ? "#2EFFFFFF" : "#17000000");
        resources["IconBrush"] = Brush(dark ? "#E5E5EA" : "#3A3A3C");
        resources["ButtonBrush"] = Brush(dark ? "#26FFFFFF" : "#14000000");
        resources["ButtonHoverBrush"] = Brush(dark ? "#3DFFFFFF" : "#24000000");
        resources["AccentBrush"] = Brush(dark ? "#0A84FF" : "#007AFF");
        // Con acrílico, un velo suave encima; sin él (Windows 10), un fondo sólido
        resources["PanelBrush"] = Dwm.SupportsBackdrop
            ? Brush(dark ? "#66161618" : "#99F5F5F7")
            : Brush(dark ? "#2C2C2E" : "#F2F2F7");
    }

    public static Color ForLevel(int percent) => percent > 20 ? Green : percent > 10 ? Orange : Red;

    static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
