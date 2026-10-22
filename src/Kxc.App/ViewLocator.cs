using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Kxc.App.ViewModels;
using Kxc.App.Views;

namespace Kxc.App;

/// <summary>Vista per ciascuna pagina, senza riflessione.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param) => param switch
    {
        FirstRunViewModel => new FirstRunView(),
        UnlockViewModel => new UnlockView(),
        MainViewModel => new MainView(),
        SettingsViewModel => new SettingsView(),
        _ => null,
    };

    public bool Match(object? data) => data is FirstRunViewModel or UnlockViewModel or MainViewModel or SettingsViewModel;
}
