using System.Windows;
using NetworkMonitor.Services;

namespace NetworkMonitor;

internal static class UpdateUi
{
    public static async Task CheckAndPromptAsync(bool quietWhenCurrent, CancellationToken ct = default)
    {
        var service = new UpdateService();
        var result = await service.CheckAsync(ct);

        if (!result.Ok)
        {
            if (!quietWhenCurrent)
            {
                MessageBox.Show(
                    "Nao foi possivel verificar atualizacoes.\n\n" + result.Error,
                    "Network Monitor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return;
        }

        if (!result.IsUpdateAvailable)
        {
            if (!quietWhenCurrent)
            {
                MessageBox.Show(
                    $"Voce ja esta na versao {UpdateService.GetCurrentVersionLabel()}.",
                    "Network Monitor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return;
        }

        var notes = string.IsNullOrWhiteSpace(result.ReleaseNotes)
            ? ""
            : "\n\n" + TrimNotes(result.ReleaseNotes!, 600);

        var answer = MessageBox.Show(
            $"Nova versao disponivel: {result.LatestVersion} (atual: {result.CurrentVersion}).{notes}\n\nAtualizar agora? O app vai fechar e reabrir.",
            "Atualizacao do Network Monitor",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            await service.ApplyAsync(result, ct: ct);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Falha ao baixar ou aplicar a atualizacao.\n\n" + ex.Message,
                "Network Monitor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string TrimNotes(string notes, int max)
    {
        var flat = notes.Replace("\r\n", "\n").Trim();
        if (flat.Length <= max) return flat;
        return flat[..max] + "...";
    }
}
