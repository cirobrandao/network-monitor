using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Win32;

using NetworkMonitor.Services;

namespace NetworkMonitor;

public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = AppState.Current;
        Closing += OnClosing;
        AppState.Current.ThemeChanged += ApplyCaptionTheme;
    }

    public void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyCaptionTheme();
    }

    private void ApplyCaptionTheme()
        => NetworkMonitor.Native.NativeWindow.ApplyCaptionTheme(this, dark: !AppState.Current.IsLightTheme);

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose)
            return;
        e.Cancel = true;
        Hide();
        if (AppState.Current.Settings.FirstRun)
        {
            AppState.Current.MarkFirstRunComplete();
        }
    }

    private void Site_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void CopyIp_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var ip = (sender as FrameworkElement)?.Tag as string;
        if (string.IsNullOrWhiteSpace(ip) || ip is "—" or "0.0.0.0" or "::")
            return;
        if (TryCopyToClipboard(ip))
            AppState.Current.FlashStatus("Copiado: " + ip);
        e.Handled = true;
    }

    /// <summary>
    /// Copies text to the clipboard, swallowing the transient
    /// COM exceptions that Clipboard.SetText can throw when another
    /// process (a clipboard manager, remote desktop, antivirus, etc.)
    /// briefly holds the clipboard open. Previously an unhandled
    /// exception here would crash the whole application.
    /// </summary>
    private static bool TryCopyToClipboard(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                System.Threading.Thread.Sleep(30);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                System.Threading.Thread.Sleep(30);
            }
        }
        return false;
    }

    private void OpenDns_Click(object sender, RoutedEventArgs e)
        => AppState.Current.DnsOpen = !AppState.Current.DnsOpen;

    private void CloseDns_Click(object sender, RoutedEventArgs e)
        => AppState.Current.DnsOpen = false;

    private void CloseDnsScrim_MouseDown(object sender, MouseButtonEventArgs e)
        => AppState.Current.DnsOpen = false;

    private async void RunDns_Click(object sender, RoutedEventArgs e)
        => await AppState.Current.RunDnsTestAsync();

    private void ToggleChart_Click(object sender, RoutedEventArgs e)
        => AppState.Current.ShowChart = !AppState.Current.ShowChart;

    private void ToggleOverlay_Click(object sender, RoutedEventArgs e)
        => AppState.Current.ShowOverlay = !AppState.Current.ShowOverlay;

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
        => AppState.Current.SettingsOpen = true;

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
        => AppState.Current.SettingsOpen = false;

    private void CloseSettingsScrim_MouseDown(object sender, MouseButtonEventArgs e)
        => AppState.Current.SettingsOpen = false;

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"conexoes-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog(this) == true)
            AppState.Current.ExportCsv(dialog.FileName);
    }

    private void Connections_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListView { SelectedItem: NetConnection connection })
            TryCopyToClipboard(connection.RemoteAddress.Length > 0 ? connection.RemoteDisplay : connection.LocalDisplay);
    }

    private void CopyRemote_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is NetConnection connection)
            TryCopyToClipboard(connection.RemoteDisplay);
    }

    private void CopyProcess_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is NetConnection connection)
            TryCopyToClipboard(connection.ProcessName);
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is NetConnection connection)
        {
            TryCopyToClipboard(
                $"{connection.ProcessName}\t{connection.Pid}\t{connection.ProtocolText}\t{connection.LocalDisplay}\t{connection.RemoteDisplay}\t{connection.HostDisplay}\t{Format.State(connection.State)}");
        }
    }

    private void BlockProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: ProcessGroup group })
            ShowBlockResult(AppState.Current.ToggleBlockProgram(group));
    }

    private void BlockIp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: IpGroup group })
            ShowBlockResult(AppState.Current.ToggleBlockAddress(group.Address));
    }

    private void BlockConnectionIp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: NetConnection connection })
            ShowBlockResult(AppState.Current.ToggleBlockAddress(connection.RemoteAddress));
    }

    private void BlockConnectionApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: NetConnection connection })
        {
            ShowBlockResult(AppState.Current.ToggleBlockProgram(new ProcessGroup
            {
                Name = connection.ProcessName,
                Path = connection.ProcessPath,
                PidSummary = "",
                ConnectionCount = 0,
                IpCount = 0,
                Connections = Array.Empty<NetConnection>()
            }));
        }
    }

    private void BlockSelectedApp_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is NetConnection connection)
        {
            ShowBlockResult(AppState.Current.ToggleBlockProgram(new ProcessGroup
            {
                Name = connection.ProcessName,
                Path = connection.ProcessPath,
                PidSummary = "",
                ConnectionCount = 0,
                IpCount = 0,
                Connections = Array.Empty<NetConnection>()
            }));
        }
    }

    private void BlockSelectedIp_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is NetConnection connection)
            ShowBlockResult(AppState.Current.ToggleBlockAddress(connection.RemoteAddress));
    }

    private void BlockSelectedAppIp_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is NetConnection connection)
            ShowBlockResult(AppState.Current.ToggleBlockProgramAddress(connection.ProcessPath, connection.RemoteAddress));
    }

    private void RemoveBlock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: BlockRule rule })
            ShowBlockResult(AppState.Current.RemoveBlock(rule));
    }

    private void RestartAdmin_Click(object sender, RoutedEventArgs e)
        => (System.Windows.Application.Current as App)?.RestartElevated();

    private static void ShowBlockResult(string message)
    {
        var ok = message.StartsWith("Bloqueio aplicado", StringComparison.Ordinal)
              || message.StartsWith("Bloqueio removido", StringComparison.Ordinal);
        var image = ok ? MessageBoxImage.Information : MessageBoxImage.Warning;
        System.Windows.MessageBox.Show(message, "Network Monitor", MessageBoxButton.OK, image);
    }

    private async void ApplyUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn)
            btn.IsEnabled = false;
        try { await UpdateUi.CheckAndPromptAsync(quietWhenCurrent: false); }
        finally
        {
            if (sender is System.Windows.Controls.Button b)
                b.IsEnabled = true;
        }
    }
}
