using CommunityToolkit.Mvvm.Input;
using Kxc.App.Services;

namespace Kxc.App.ViewModels;

/// <summary>Primo avvio: un solo passo, scegliere il database.</summary>
public sealed partial class FirstRunViewModel : ViewModelBase
{
    private readonly IFileDialogService _files;
    private readonly Action<string> _onChosen;

    public FirstRunViewModel(IFileDialogService files, Action<string> onChosen)
    {
        _files = files;
        _onChosen = onChosen;
    }

    [RelayCommand]
    private async Task ChooseDatabaseAsync()
    {
        if (await _files.PickDatabaseAsync() is { } path)
        {
            _onChosen(path);
        }
    }
}
