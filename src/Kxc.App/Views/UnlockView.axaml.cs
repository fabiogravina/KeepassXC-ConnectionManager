using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Kxc.App.ViewModels;

namespace Kxc.App.Views;

public sealed partial class UnlockView : UserControl
{
    private UnlockViewModel? _viewModel;

    public UnlockView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.FocusPasswordRequested -= OnFocusPasswordRequested;
        }

        _viewModel = DataContext as UnlockViewModel;
        if (_viewModel is not null)
        {
            _viewModel.FocusPasswordRequested += OnFocusPasswordRequested;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        PasswordBox.Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_viewModel is not null)
        {
            _viewModel.FocusPasswordRequested -= OnFocusPasswordRequested;
        }
    }

    private void OnFocusPasswordRequested(object? sender, EventArgs e) => PasswordBox.Focus();
}
