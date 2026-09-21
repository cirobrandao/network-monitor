using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using NetworkMonitor;
using NetworkMonitor.Services;

internal static class UiChecks
{
    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var source = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../src/NetworkMonitor/App.xaml"));
            var document = XDocument.Parse(File.ReadAllText(source).Replace("clr-namespace:NetworkMonitor", "clr-namespace:NetworkMonitor;assembly=NetworkMonitor"));
            var dictionary = document.Descendants().Single(element => element.Name.LocalName == "ResourceDictionary");
            dictionary.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
            dictionary.SetAttributeValue(XNamespace.Xmlns + "local", "clr-namespace:NetworkMonitor;assembly=NetworkMonitor");
            app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            var state = AppState.Current;
            SetState(nameof(AppState.DownText), "999.9 MB/s");
            SetState(nameof(AppState.UpText), "999.9 MB/s");
            SetState(nameof(AppState.ConnectionCount), 99999);
            state.SetPublicIp("2001:db8:abcd:1234:5678:90ab:cdef:1234");
            var overlay = new OverlayWindow { Left = 20, Top = 20 };
            overlay.Show();
            SetState(nameof(AppState.AdapterName), "Ethernet");
            SetState(nameof(AppState.InternalIp), "192.168.1.24");
            SetState(nameof(AppState.DownTotalText), "12.4 GB");
            SetState(nameof(AppState.UpTotalText), "1.8 GB");
            state.OverlayChartType = ChartKind.Bar;
            SetState(nameof(AppState.WidgetDownHistory), Enumerable.Range(0, 32).Select(index => (index % 9 + 1) * 100d).ToArray());
            SetState(nameof(AppState.WidgetUpHistory), Enumerable.Range(0, 32).Select(index => (index % 7 + 1) * 90d).ToArray());

            state.OverlayCompact = false;
            overlay.ApplyStyle();
            Flush(overlay);
            Require(Math.Abs(overlay.ActualWidth - 240) < 1, "Complete widget width drifted");
            Require(Math.Abs(overlay.ActualHeight - 300) < 1, "Complete widget height drifted");
            var card = (Border)overlay.FindName("Card");
            Require(((Grid)overlay.FindName("CompleteLayout")).IsVisible, "Complete layout hidden");
            Require(!((Grid)overlay.FindName("SimpleLayout")).IsVisible, "Simple layout shown in complete mode");
            CheckBounds((FrameworkElement)overlay.FindName("PublicIpLabel"), card);
            CheckBounds((FrameworkElement)overlay.FindName("InternalIpLabel"), card);
            CheckBounds((FrameworkElement)overlay.FindName("ConnectionLabel"), card);
            CheckBounds((FrameworkElement)overlay.FindName("TrafficChart"), card);
            SetState(nameof(AppState.ConnectionCount), 12);
            Flush(overlay);
            var completeWidth = overlay.ActualWidth;
            SetState(nameof(AppState.ConnectionCount), 99999);
            Flush(overlay);
            Require(Math.Abs(overlay.ActualWidth - completeWidth) < 1, "Connections resized the complete widget");
            SaveImage(overlay, "network-monitor-complete.png");

            state.OverlayCompact = true;
            overlay.ApplyStyle();
            Flush(overlay);
            Require(Math.Abs(overlay.ActualWidth - 280) < 1, "Simple widget width drifted");
            Require(Math.Abs(overlay.ActualHeight - 48) < 1, "Simple widget height drifted");
            Require(((Grid)overlay.FindName("SimpleLayout")).IsVisible, "Simple layout hidden");
            Require(!((Grid)overlay.FindName("CompleteLayout")).IsVisible, "Complete layout shown in simple mode");
            CheckBounds((FrameworkElement)overlay.FindName("Rates"), card);
            CheckBounds((FrameworkElement)overlay.FindName("SimpleChart"), card);
            SaveImage(overlay, "network-monitor-simple.png");

            var traffic = (BandwidthChart)overlay.FindName("SimpleChart");
            traffic.Redraw();
            var plot = (Canvas)traffic.FindName("Plot");
            var baseline = plot.Children.OfType<Line>().Single().Y1;
            var rectangles = plot.Children.OfType<Rectangle>().ToArray();
            Require(rectangles.Length > 0, "No traffic bars rendered");
            foreach (var rectangle in rectangles)
            {
                var color = ((SolidColorBrush)rectangle.Fill).Color;
                var position = Canvas.GetTop(rectangle);
                Require(color.G > color.B ? position + rectangle.Height < baseline : position > baseline,
                    "Download/upload bars cross the baseline");
            }
            SetState(nameof(AppState.WidgetDownHistory), new double[32]);
            SetState(nameof(AppState.WidgetUpHistory), new double[32]);
            traffic.Redraw();
            Require(!plot.Children.OfType<Rectangle>().Any(), "Zero traffic produces fake bars");

            var store = typeof(AppSettings).Assembly.GetType("NetworkMonitor.Services.SettingsStore")!;
            var options = (JsonSerializerOptions)store.GetField("JsonOptions", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var serialized = JsonSerializer.Serialize(new AppSettings { OverlayCompact = true }, options);
            var restored = JsonSerializer.Deserialize<AppSettings>(serialized, options)!;
            Require(restored.OverlayCompact && double.IsNaN(restored.OverlayLeft),
                "Widget mode fails settings round-trip");

            var loading = new LoadingWindow();
            loading.Show();
            Flush(loading);
            SaveImage(loading, "network-monitor-loading.png");
            loading.Close();
            var main = new MainWindow();
            main.Show();
            state.SettingsOpen = true;
            Flush(main);
            SaveImage(main, "network-monitor-settings.png");
            main.ForceClose();
            overlay.Close();
            Console.WriteLine("PASS: complete and simple widgets; bars, settings round-trip, loading and main windows.");
            app.Shutdown();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void SetState(string property, object value)
        => typeof(AppState).GetProperty(property)!.SetValue(AppState.Current, value);

    private static void Flush(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }

    private static void CheckBounds(FrameworkElement element, FrameworkElement parent)
    {
        var origin = element.TranslatePoint(new Point(), parent);
        Require(origin.X >= 0 && origin.Y >= 0 && origin.X + element.ActualWidth <= parent.ActualWidth + 1
            && origin.Y + element.ActualHeight <= parent.ActualHeight + 1, $"Clipped element: {element.Name}");
    }

    private static void SaveImage(Window window, string name)
    {
        Flush(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        if (name == "network-monitor-simple.png")
        {
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var coloredPixels = 0;
            for (var row = 0; row < bitmap.PixelHeight; row++)
            for (var column = bitmap.PixelWidth - 108; column < bitmap.PixelWidth - 8; column++)
            {
                var offset = (row * bitmap.PixelWidth + column) * 4;
                if (pixels[offset + 1] > pixels[offset + 2] + 30 || pixels[offset] > pixels[offset + 2] + 30)
                    coloredPixels++;
            }
            Require(coloredPixels > 20, "Simple chart pixels are blank");
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
        using var output = File.Create(path);
        encoder.Save(output);
        Console.WriteLine(path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}