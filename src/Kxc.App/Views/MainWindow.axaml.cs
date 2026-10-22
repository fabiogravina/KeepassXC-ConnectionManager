using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Kxc.App.ViewModels;

namespace Kxc.App.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Solo l'attività dentro questa finestra rinvia il blocco automatico.
        AddHandler(KeyDownEvent, (_, _) => RegisterActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(TextInputEvent, (_, _) => RegisterActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => RegisterActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, (_, _) => RegisterActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, (_, _) => RegisterActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void RegisterActivity() => (DataContext as ShellViewModel)?.RegisterActivity();
}
