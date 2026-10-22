using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kxc.App.Controls;

/// <summary>
/// Campo della master password (S11). Il TextBox di Avalonia 12, anche con PasswordChar:
/// conserva copie del testo nella cronologia di annullamento, con «Mostra» permette di copiarlo
/// negli appunti (che Windows può sincronizzare nel cloud) e lo espone in chiaro all'automazione
/// dell'interfaccia. Qui nessuna delle tre cose; l'aspetto resta quello del TextBox.
/// </summary>
public class SecretTextBox : TextBox
{
    public SecretTextBox()
    {
        IsUndoEnabled = false;
        AddHandler(CopyingToClipboardEvent, Block, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        AddHandler(CuttingToClipboardEvent, Block, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
    }

    protected override Type StyleKeyOverride => typeof(TextBox);

    protected override AutomationPeer OnCreateAutomationPeer() => new SecretTextBoxAutomationPeer(this);

    private static void Block(object? sender, RoutedEventArgs e) => e.Handled = true;

    /// <summary>Per l'automazione è un campo di testo, ma il suo valore è sempre vuoto.</summary>
    private sealed class SecretTextBoxAutomationPeer(SecretTextBox owner) : ControlAutomationPeer(owner), IValueProvider
    {
        public bool IsReadOnly => owner.IsReadOnly;

        public string? Value => string.Empty;

        public void SetValue(string? value) => owner.Text = value;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
    }
}
