using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Microsoft.Win32;

namespace NetworkMonitor;

public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = AppState.Current;
        Closing += OnClosing;
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
        NetworkMonitor.Native.NativeWindow.ApplyCaptionTheme(this, dark: false);
    }

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
            Clipboard.SetText(connection.RemoteAddress.Length > 0 ? connection.RemoteDisplay : connection.LocalDisplay);
    }

    private void CopyRemote_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is NetConnection connection)
            Clipboard.SetText(connection.RemoteDisplay);
    }

    private void CopyProcess_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is NetConnection connection)
            Clipboard.SetText(connection.ProcessName);
    }

    private void CopyRow_Click(object sender, RoutedEventArgs e)
    {
        if (ConnectionsList.SelectedItem is NetConnection connection)
        {
            Clipboard.SetText(
                $"{connection.ProcessName}\t{connection.Pid}\t{connection.ProtocolText}\t{connection.LocalDisplay}\t{connection.RemoteDisplay}\t{connection.HostDisplay}\t{Format.State(connection.State)}");
        }
    }
}
