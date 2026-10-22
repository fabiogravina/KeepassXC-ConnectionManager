using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kxc.App.Views;

/// <summary>Conferma dell'impronta alla prima connessione: Annulla è il pulsante predefinito di Esc.</summary>
public sealed partial class HostKeyDialog : Window
{
    public HostKeyDialog()
    {
        InitializeComponent();
        TrustButton.Click += (_, _) => Close(true);
        CancelButton.Click += (_, _) => Close(false);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Nessuna scelta con Invio: fidarsi deve essere un gesto esplicito.
        CancelButton.Focus();
    }
}
