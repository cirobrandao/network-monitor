using System.Windows;
using System.Windows.Input;
using NetworkMonitor.Native;

namespace NetworkMonitor;

public partial class OverlayWindow : Window
{
    private bool _placing;
    private DateTime _dragStartedAt;
    private System.Windows.Point _dragStart;

    public OverlayWindow()
    {
        InitializeComponent();
        DataContext = AppState.Current;
        Opacity = AppState.Current.OverlayOpacity;
        Loaded += OnLoaded;
        LocationChanged += OnLocationChanged;
        SizeChanged += (_, _) => ClampToVirtualScreen();
        MouseDoubleClick += (_, _) => (System.Windows.Application.Current as App)?.ShowMainWindow();
        AppState.Current.OverlayStyleChanged += ApplyStyle;
    }

    public void ApplyStyle()
    {
        Opacity = AppState.Current.OverlayOpacity;
        Width = AppState.Current.OverlayShowConnections ? 380 : 300;
        var compact = AppState.Current.OverlayCompact;
        var height = 40.0;
        if (AppState.Current.OverlayChartVisible)
            height += 52;
        if (!compact && AppState.Current.OverlayDetailsVisible)
            height += 20;
        Height = height;
        if (IsLoaded)
            NetworkMonitor.Native.NativeWindow.ApplyOverlayStyle(this, AppState.Current.OverlayClickThrough);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NetworkMonitor.Native.NativeWindow.ApplyOverlayStyle(this, AppState.Current.OverlayClickThrough);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _placing = true;
        ApplyStyle();
        PlaceInitial();
        _placing = false;
    }

    private void PlaceInitial()
    {
        var settings = AppState.Current.Settings;
        if (!double.IsNaN(settings.OverlayLeft) && !double.IsNaN(settings.OverlayTop))
        {
            Left = settings.OverlayLeft;
            Top = settings.OverlayTop;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 18;
            Top = work.Bottom - Height - 18;
        }
        ClampToVirtualScreen();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (_placing || !IsVisible)
            return;
        AppState.Current.SaveOverlayPosition(Left, Top);
    }

    private void ClampToVirtualScreen()
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;
        if (Left + Width < left + 40) Left = left;
        if (Top + Height < top + 40) Top = top;
        if (Left > right - 40) Left = right - Width;
        if (Top > bottom - 40) Top = bottom - Height;
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartedAt = DateTime.UtcNow;
        _dragStart = e.GetPosition(this);
        try
        {
            DragMove();
        }
        catch
        {
            // DragMove throws if the mouse is already released
        }
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((DateTime.UtcNow - _dragStartedAt).TotalMilliseconds < 220)
        {
            var delta = e.GetPosition(this) - _dragStart;
            if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4)
                (System.Windows.Application.Current as App)?.ShowMainWindow();
        }
    }

    private void OpenMain_Click(object sender, RoutedEventArgs e)
        => (System.Windows.Application.Current as App)?.ShowMainWindow();

    private void HideOverlay_Click(object sender, RoutedEventArgs e)
        => AppState.Current.ShowOverlay = false;

    private void Exit_Click(object sender, RoutedEventArgs e)
        => (System.Windows.Application.Current as App)?.ExitApp();
}
