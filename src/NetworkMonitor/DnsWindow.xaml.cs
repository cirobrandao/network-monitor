using System.Windows;
using NetworkMonitor.Native;

namespace NetworkMonitor;

public partial class DnsWindow : Window
{
    public DnsWindow()
    {
        InitializeComponent();
        DataContext = AppState.Current;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindow.ApplyCaptionTheme(this, dark: !AppState.Current.IsLightTheme);
    }

    private async void RunDns_Click(object sender, RoutedEventArgs e)
        => await AppState.Current.RunDnsTestAsync();
}
