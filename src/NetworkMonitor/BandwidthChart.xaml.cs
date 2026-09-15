using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace NetworkMonitor;

public partial class BandwidthChart : UserControl
{
    private static readonly SolidColorBrush DownStroke = Freeze(Color.FromRgb(0x0D, 0x7A, 0x5F));
    private static readonly SolidColorBrush UpStroke = Freeze(Color.FromRgb(0x1A, 0x5F, 0xA8));
    private static readonly SolidColorBrush DownFill = Freeze(Color.FromArgb(0x48, 0x0D, 0x7A, 0x5F));
    private static readonly SolidColorBrush UpFill = Freeze(Color.FromArgb(0x48, 0x1A, 0x5F, 0xA8));
    private static readonly SolidColorBrush GridLine = Freeze(Color.FromRgb(0xE6, 0xEB, 0xF0));

    public bool Compact { get; set; }
    public bool UseOverlayChart { get; set; }

    public BandwidthChart()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        Loaded += (_, _) => Redraw();
    }

    private void Hook()
    {
        if (DataContext is AppState state)
            state.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(AppState.DownHistory) or nameof(AppState.UpHistory)
                    or nameof(AppState.ChartType) or nameof(AppState.OverlayChartType)
                    or nameof(AppState.DownText) or nameof(AppState.UpText))
                    Redraw();
            };
    }

    private void Plot_SizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    public void Redraw()
    {
        if (Plot is null || DataContext is not AppState state)
            return;

        Plot.Children.Clear();
        var width = Plot.ActualWidth;
        var height = Plot.ActualHeight;
        if (width < 8 || height < 8)
            return;

        var down = state.DownHistory;
        var up = state.UpHistory;
        var count = Math.Min(down.Count, up.Count);
        if (count < 1)
            return;

        var max = 1d;
        for (var i = 0; i < count; i++)
            max = Math.Max(max, Math.Max(down[i], up[i]));
        if (Legend is not null)
            Legend.Visibility = Compact ? Visibility.Collapsed : Visibility.Visible;
        if (ScaleLabel is not null)
        {
            ScaleLabel.Visibility = Compact ? Visibility.Collapsed : Visibility.Visible;
            ScaleLabel.Text = Format.Rate(max);
        }

        var padL = Compact ? 2d : 8d;
        var padR = Compact ? 2d : 8d;
        var padT = Compact ? 2d : 20d;
        var padB = Compact ? 2d : 6d;
        var plotW = Math.Max(1, width - padL - padR);
        var plotH = Math.Max(1, height - padT - padB);

        if (!Compact)
        {
            for (var g = 1; g <= 3; g++)
            {
                var y = padT + plotH * g / 4;
                Plot.Children.Add(new Line
                {
                    X1 = padL,
                    X2 = width - padR,
                    Y1 = y,
                    Y2 = y,
                    Stroke = GridLine,
                    StrokeThickness = 1
                });
            }
        }

        var kind = UseOverlayChart ? state.OverlayChartType : state.ChartType;
        var stroke = Compact ? 1.3 : 1.8;
        switch (kind)
        {
            case ChartKind.Bar:
                DrawBars(down, up, count, max, padL, padT, plotW, plotH);
                break;
            case ChartKind.Area:
                DrawArea(down, count, max, padL, padT, plotW, plotH, DownFill, DownStroke, stroke);
                DrawArea(up, count, max, padL, padT, plotW, plotH, UpFill, UpStroke, stroke);
                break;
            default:
                DrawLine(down, count, max, padL, padT, plotW, plotH, DownStroke, stroke);
                DrawLine(up, count, max, padL, padT, plotW, plotH, UpStroke, stroke);
                break;
        }
    }

    private void DrawLine(IReadOnlyList<double> data, int count, double max, double left, double top, double w, double h, Brush stroke, double thickness)
    {
        var polyline = new Polyline { Stroke = stroke, StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round };
        for (var i = 0; i < count; i++)
            polyline.Points.Add(PointAt(i, data[i], count, max, left, top, w, h));
        Plot.Children.Add(polyline);
    }

    private void DrawArea(IReadOnlyList<double> data, int count, double max, double left, double top, double w, double h, Brush fill, Brush stroke, double thickness)
    {
        var polygon = new Polygon { Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        polygon.Points.Add(new Point(left, top + h));
        for (var i = 0; i < count; i++)
            polygon.Points.Add(PointAt(i, data[i], count, max, left, top, w, h));
        polygon.Points.Add(new Point(left + w, top + h));
        Plot.Children.Add(polygon);
    }

    private void DrawBars(IReadOnlyList<double> down, IReadOnlyList<double> up, int count, double max, double left, double top, double w, double h)
    {
        var slot = w / count;
        var bar = Math.Max(1, slot * 0.38);
        for (var i = 0; i < count; i++)
        {
            var x = left + i * slot;
            AddBar(x, down[i], max, top, h, bar, DownStroke);
            AddBar(x + bar + 1, up[i], max, top, h, bar, UpStroke);
        }
    }

    private void AddBar(double x, double value, double max, double top, double h, double width, Brush fill)
    {
        var barH = Math.Max(1, (value / max) * h);
        var rect = new Rectangle
        {
            Width = width,
            Height = barH,
            Fill = fill
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, top + h - barH);
        Plot.Children.Add(rect);
    }

    private static Point PointAt(int i, double value, int count, double max, double left, double top, double w, double h)
    {
        var x = count == 1 ? left : left + i * (w / (count - 1));
        var y = top + h - (value / max) * h;
        return new Point(x, y);
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
