using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NetworkMonitor.Native;

namespace NetworkMonitor;

internal static class Theme
{
    public static bool ResolveLight(string? mode)
    {
        if (string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(mode, "Light", StringComparison.OrdinalIgnoreCase))
            return true;
        return SystemIsLight();
    }

    public static bool SystemIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i)
                return i != 0;
        }
        catch
        {
            // default light
        }
        return true;
    }

    public static void Apply(string? mode)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var light = ResolveLight(mode);
        var r = app.Resources;
        if (light)
        {
            Set(r, "BgColor", "#F4F6F8", "BgBrush");
            Set(r, "SurfaceColor", "#FFFFFF", "SurfaceBrush");
            Set(r, "Surface2Color", "#EEF1F4", "Surface2Brush");
            Set(r, "BorderColor", "#D5DCE3", "BorderBrush");
            Set(r, "TextColor", "#1A2433", "TextBrush");
            Set(r, "MutedColor", "#5C6773", "MutedBrush");
            Set(r, "HeaderColor", "#12344D", "HeaderBrush");
            Set(r, "PeakBannerColor", "#FEF3F2", "PeakBannerBrush");
            Set(r, "PeakBannerBorderColor", "#F5C2C0", "PeakBannerBorderBrush");
            Set(r, "RowHoverColor", "#EEF3F8", "RowHoverBrush");
        }
        else
        {
            Set(r, "BgColor", "#0F141A", "BgBrush");
            Set(r, "SurfaceColor", "#1A2129", "SurfaceBrush");
            Set(r, "Surface2Color", "#222A33", "Surface2Brush");
            Set(r, "BorderColor", "#2E3844", "BorderBrush");
            Set(r, "TextColor", "#E8EEF4", "TextBrush");
            Set(r, "MutedColor", "#9AA7B5", "MutedBrush");
            Set(r, "HeaderColor", "#0C1A26", "HeaderBrush");
            Set(r, "PeakBannerColor", "#3A1A16", "PeakBannerBrush");
            Set(r, "PeakBannerBorderColor", "#7A2E28", "PeakBannerBorderBrush");
            Set(r, "RowHoverColor", "#24303C", "RowHoverBrush");
        }

        foreach (Window window in app.Windows)
        {
            if (window is OverlayWindow)
                continue;
            NativeWindow.ApplyCaptionTheme(window, dark: !light);
        }
    }

    private static void Set(ResourceDictionary resources, string colorKey, string hex, string brushKey)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        resources[colorKey] = color;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[brushKey] = brush;
    }
}
