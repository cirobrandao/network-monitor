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

    public bool ForWidget { get; set; }

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
                if (ForWidget)
                {
                    if (e.PropertyName is nameof(AppState.WidgetDownHistory) or nameof(AppState.WidgetUpHistory)
                        or nameof(AppState.OverlayChartType))
                        Redraw();
                    return;
                }

                if (e.PropertyName is nameof(AppState.DownHistory) or nameof(AppState.UpHistory)
                    or nameof(AppState.ChartType))
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

        var down = ForWidget ? state.WidgetDownHistory : state.DownHistory;
        var up = ForWidget ? state.WidgetUpHistory : state.UpHistory;
        var kind = ForWidget ? state.OverlayChartType : state.ChartType;
        var count = Math.Min(down.Count, up.Count);
        if (count < 2)
            return;

        var max = 1d;
        for (var i = 0; i < count; i++)
            max = Math.Max(max, Math.Max(down[i], up[i]));

        if (Legend is not null)
            Legend.Visibility = ForWidget ? Visibility.Collapsed : Visibility.Visible;
        if (ScaleLabel is not null)
        {
            ScaleLabel.Visibility = ForWidget ? Visibility.Collapsed : Visibility.Visible;
            ScaleLabel.Text = Format.Rate(max);
        }

        var padL = ForWidget ? 2d : 8d;
        var padR = ForWidget ? 2d : 8d;
        var padT = ForWidget ? 2d : 20d;
        var padB = ForWidget ? 2d : 6d;
        var plotW = Math.Max(1, width - padL - padR);
        var plotH = Math.Max(1, height - padT - padB);

        if (!ForWidget && kind != ChartKind.Bar)
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

        switch (kind)
        {
            case ChartKind.Bar:
                DrawBars(down, up, count, max, padL, padT, plotW, plotH);
                break;
            case ChartKind.Area:
                DrawArea(down, count, max, padL, padT, plotW, plotH, DownFill, DownStroke);
                DrawArea(up, count, max, padL, padT, plotW, plotH, UpFill, UpStroke);
                break;
            default:
                DrawLine(down, count, max, padL, padT, plotW, plotH, DownStroke);
                DrawLine(up, count, max, padL, padT, plotW, plotH, UpStroke);
                break;
        }
    }

    private void DrawLine(IReadOnlyList<double> data, int count, double max, double left, double top, double w, double h, Brush stroke)
    {
        var polyline = new Polyline
        {
            Stroke = stroke,
            StrokeThickness = ForWidget ? 1.4 : 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        for (var i = 0; i < count; i++)
            polyline.Points.Add(PointAt(i, data[i], count, max, left, top, w, h));
        Plot.Children.Add(polyline);
    }

    private void DrawArea(IReadOnlyList<double> data, int count, double max, double left, double top, double w, double h, Brush fill, Brush stroke)
    {
        var polygon = new Polygon { Fill = fill, Stroke = stroke, StrokeThickness = ForWidget ? 1.2 : 1.6 };
        polygon.Points.Add(new Point(left, top + h));
        for (var i = 0; i < count; i++)
            polygon.Points.Add(PointAt(i, data[i], count, max, left, top, w, h));
        polygon.Points.Add(new Point(left + w, top + h));
        Plot.Children.Add(polygon);
    }

    private void DrawBars(IReadOnlyList<double> down, IReadOnlyList<double> up, int count, double max, double left, double top, double w, double h)
    {
        var baseline = top + h / 2;
        var half = Math.Max(1, h / 2 - 2);
        var gap = ForWidget ? 1.0 : 2.0;
        var barW = Math.Max(ForWidget ? 2.4 : 5.0, (w - (count - 1) * gap) / count);
        var step = barW + gap;

        Plot.Children.Add(new Line
        {
            X1 = left,
            X2 = left + w,
            Y1 = baseline,
            Y2 = baseline,
            Stroke = GridLine,
            StrokeThickness = 1
        });

        for (var i = 0; i < count; i++)
        {
            var x = left + i * step;
            var width = i == count - 1 ? Math.Max(1, left + w - x) : barW;
            var downH = down[i] / max * half;
            var upH = up[i] / max * half;
            if (downH > 0.4)
                AddBar(x, baseline - 1 - downH, width, downH, DownStroke);
            if (upH > 0.4)
                AddBar(x, baseline + 1, width, upH, UpStroke);
        }
    }

    private void AddBar(double x, double y, double width, double height, Brush fill)
    {
        var rect = new Rectangle
        {
            Width = Math.Max(1, width),
            Height = height,
            Fill = fill
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        Plot.Children.Add(rect);
    }

    private static Point PointAt(int i, double value, int count, double max, double left, double top, double w, double h)
    {
        var x = count <= 1 ? left : left + i * (w / (count - 1));
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
