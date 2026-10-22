using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Kxc.App.Views;

public sealed partial class FirstRunView : UserControl
{
    public FirstRunView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ChooseButton.Focus();
    }
}
