using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Kxc.App.ViewModels;
using Kxc.App.Views;
using Kxc.Core.Settings;
using Kxc.Tests.TestData;

namespace Kxc.Tests.Ui;

/// <summary>S11: il campo della master password non deve lasciare copie evitabili della password.</summary>
public class PasswordFieldTests
{
    private const string Typed = "segreto-digitato";

    private static (Window Window, TextBox Box, UnlockViewModel ViewModel) Show()
    {
        var settings = new AppSettings { DatabasePath = Fixture.DatabasePath };
        var viewModel = new UnlockViewModel(null!, null!, settings, null!, _ => { });
        var view = new UnlockView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 800, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var box = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PasswordBox");
        box.Focus();
        window.KeyTextInput(Typed);
        Dispatcher.UIThread.RunJobs();
        return (window, box, viewModel);
    }

    [AvaloniaFact]
    public void Undo_history_is_disabled()
    {
        var (_, box, viewModel) = Show();

        Assert.Equal(Typed, viewModel.Password);
        Assert.False(box.IsUndoEnabled);
        Assert.False(box.CanUndo);
    }

    [AvaloniaFact]
    public async Task Password_never_reaches_the_clipboard_even_when_shown()
    {
        var (window, box, viewModel) = Show();
        viewModel.IsPasswordVisible = true;
        Dispatcher.UIThread.RunJobs();
        var clipboard = TopLevel.GetTopLevel(window)!.Clipboard!;
        await clipboard.SetTextAsync("prima");

        box.SelectAll();
        box.Copy();
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        box.Cut();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("prima", await clipboard.TryGetTextAsync());
        Assert.Equal(Typed, viewModel.Password);
    }

    [AvaloniaFact]
    public void Automation_does_not_expose_the_password()
    {
        var (_, box, _) = Show();

        var peer = ControlAutomationPeer.CreatePeerForElement(box);
        var value = peer.GetProvider<IValueProvider>()?.Value ?? string.Empty;

        Assert.DoesNotContain(Typed, value, StringComparison.Ordinal);
    }
}
