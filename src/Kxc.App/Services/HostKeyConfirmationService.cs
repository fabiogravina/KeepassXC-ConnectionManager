using Avalonia.Controls;
using Avalonia.Threading;
using Kxc.App.ViewModels;
using Kxc.App.Views;
using Kxc.Core.Connections;

namespace Kxc.App.Services;

/// <summary>
/// Mostra la finestra con l'impronta sopra la finestra dell'app (il terminale di ssh resta dietro).
/// Se il canale scade prima della risposta la finestra si chiude e la risposta è no.
/// </summary>
internal sealed class HostKeyConfirmationService : IHostKeyConfirmation
{
    public Window? Owner { get; set; }

    public Task<bool> ConfirmAsync(HostKeyQuestion question, CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (Owner is null)
            {
                return false;
            }

            var dialog = new HostKeyDialog { DataContext = new HostKeyDialogViewModel(question) };
            await using var closeOnCancel = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
            Owner.Activate();
            return await dialog.ShowDialog<bool?>(Owner) == true;
        });
}
