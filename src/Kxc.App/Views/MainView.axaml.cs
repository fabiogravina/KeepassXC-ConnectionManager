using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Kxc.App.ViewModels;

namespace Kxc.App.Views;

/// <summary>
/// Tastiera della finestra principale: il campo di ricerca tiene il focus, frecce e Invio agiscono
/// sulla lista, e si può scrivere subito anche se il focus è altrove.
/// </summary>
public sealed partial class MainView : UserControl
{
    private const int PageSize = 10;

    public MainView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnPreviewTextInput, RoutingStrategies.Tunnel);
        EntryList.AddHandler(PointerReleasedEvent, (_, _) => FocusSearch(), RoutingStrategies.Bubble, handledEventsToo: true);
        EntryList.DoubleTapped += OnEntryDoubleTapped;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FocusSearch();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm is null)
        {
            return;
        }

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var focusOnButton = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Button;

        switch (e.Key)
        {
            case Key.Up:
                vm.MoveSelection(-1);
                break;
            case Key.Down:
                vm.MoveSelection(1);
                break;
            case Key.PageUp:
                vm.MoveSelection(-PageSize);
                break;
            case Key.PageDown:
                vm.MoveSelection(PageSize);
                break;
            case Key.Enter when ctrl:
                vm.OpenSftpSelectedCommand.Execute(null);
                break;
            case Key.Enter when !focusOnButton:
                vm.ConnectSelectedCommand.Execute(null);
                break;
            case Key.D when ctrl:
                vm.ToggleFavoriteCommand.Execute(null);
                break;
            case Key.L when ctrl:
                vm.LockCommand.Execute(null);
                break;
            case Key.OemComma when ctrl:
                vm.OpenSettingsCommand.Execute(null);
                break;
            case Key.Escape:
                vm.ClearSearchCommand.Execute(null);
                FocusSearch();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Se il focus è su un pulsante o sulla lista, il testo digitato va comunque nella ricerca.</summary>
    private void OnPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source is TextBox || string.IsNullOrEmpty(e.Text) || ViewModel is not { } vm)
        {
            return;
        }

        vm.SearchText += e.Text;
        FocusSearch();
        SearchBox.CaretIndex = vm.SearchText.Length;
        e.Handled = true;
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>() is null && source is not Button)
        {
            ViewModel?.ConnectSelectedCommand.Execute(null);
        }
    }

    private void FocusSearch() => SearchBox.Focus();
}
