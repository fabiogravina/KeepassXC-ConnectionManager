using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kxc.App.ViewModels;

namespace Kxc.App.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        BackButton.Focus();
    }

    /// <summary>Esc torna alla lista, salvo quando chiude una tendina aperta.</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.Source is not ComboBox { IsDropDownOpen: true } && DataContext is SettingsViewModel vm)
        {
            vm.BackCommand.Execute(null);
            e.Handled = true;
        }
    }
}
